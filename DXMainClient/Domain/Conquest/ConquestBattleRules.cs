#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using ClientCore;

using Rampastring.Tools;

namespace DTAClient.Domain.Conquest
{
    /// <summary>
    /// Battle-local rules. Player production clones follow the randomizers' isolated
    /// country / Vinifera house-mask approach; stock engines use separate handicaps.
    /// Installed rules and native enemy ownership are never replaced by player stats.
    /// </summary>
    public sealed class ConquestBattleRules
    {
        private readonly IniFile rules;
        private readonly ClientType game;
        private readonly int difficulty;
        private readonly bool nativeProfiles;
        private readonly HashSet<string> cloneTypes;
        private readonly string originalCountry;
        private readonly string aiCountry;
        private readonly string playerCountry;
        private readonly int playerIndex;
        private readonly bool clones;
        private readonly Dictionary<string, string> aliases = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> occupiedIds = new(StringComparer.OrdinalIgnoreCase);
        private int nextCloneId;
        private static readonly string[] mobileLists = { "InfantryTypes", "VehicleTypes", "AircraftTypes" };

        public ConquestBattleRules(IniFile rules, ClientType game, int difficulty, int playerSide, int aiSide, ConquestBattleConfiguration configuration)
        {
            this.rules = rules;
            this.game = game;
            this.difficulty = difficulty;
            nativeProfiles = configuration.NativeProfiles;
            cloneTypes = new HashSet<string>(configuration.CloneTypes, StringComparer.OrdinalIgnoreCase);
            string countryList = game == ClientType.TS ? "Houses" : "Countries";
            var countries = Entries(rules, countryList).Select(e => e.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            originalCountry = playerSide >= 0 && playerSide < countries.Count ? countries[playerSide] : string.Empty;
            aiCountry = aiSide >= 0 && aiSide < countries.Count ? countries[aiSide] : string.Empty;
            bool typesAvailable = mobileLists.Any(list => Entries(rules, list).Any());
            clones = configuration.EnableUnitClones && typesAvailable && originalCountry.Length > 0;
            playerCountry = originalCountry;
            playerIndex = playerSide;
            if (configuration.EnableUnitClones && !clones)
                throw new InvalidDataException("Conquest cloning requires complete mobile type and country definitions.");
            if (clones && !mobileLists.Any(list => Entries(rules, list).Any(entry => cloneTypes.Contains(entry.Value) &&
                Csv(rules.GetStringValue(entry.Value, "Owner", string.Empty)).Any(owner => Same(owner, originalCountry)) &&
                !Csv(rules.GetStringValue(entry.Value, "ForbiddenHouses", string.Empty)).Any(owner => Same(owner, originalCountry)))))
            {
                if (!nativeProfiles)
                    throw new InvalidDataException("No configured Conquest clone types support the selected player faction.");
                clones = false;
            }
            if (!clones)
                return;

            if (game == ClientType.TS)
            {
                // Vinifera checks RequiredHouses against ActsLike. Reuse a registered
                // unused house mask, as the DTA randomizer does; do not invent a bit.
                playerIndex = configuration.ProductionHouseIndex;
                if (playerIndex < 6 || playerIndex >= countries.Count || playerIndex == playerSide || playerIndex == aiSide)
                {
                    throw new InvalidDataException("Conquest ProductionHouseIndex must reserve a registered mask unused by either faction.");
                }
                playerCountry = countries[playerIndex];
            }
            else
            {
                playerCountry = "CQPLAYER";
                if (countries.Contains(playerCountry, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException("The campaign country is already reserved.");

                playerIndex = countries.Count;
            }
        }

        public static double PlayerMultiplier(ConquestBattle battle) =>
            1 + battle.PlayerStats.GetValueOrDefault(ConquestStat.Damage) / 100.0;

        public static double AIMultiplier(ConquestBattle battle) =>
            1 + battle.AIStats.GetValueOrDefault(ConquestStat.Damage) / 100.0;

        public string ResolvePlayerType(string source) => aliases.TryGetValue(source, out string? clone) ? clone : source;

        public void ApplySpawn(IniFile spawn)
        {
            if (clones)
            {
                spawn.SetIntValue("Settings", "Side", playerIndex);
                return;
            }

            // Separate profiles also isolate GDI-vs-GDI on unextended TS and on
            // YR installations whose native rules are only in encrypted archives.
            if (!nativeProfiles)
                return;

            int human = HumanHandicap;
            spawn.SetIntValue(game == ClientType.TS ? "HouseHandicaps" : "HandicapDifficulty", "Multi1", human);
            if (game != ClientType.TS)
                spawn.SetIntValue("HandicapDifficulty", "Multi2", 2 - difficulty);
        }

        private int HumanHandicap => difficulty == 1 ? 0 : 1;

        public void ApplyMap(IniFile map, ConquestBattle battle)
        {
            var combined = new IniFile();
            IniFile.ConsolidateIniFiles(combined, rules);
            IniFile.ConsolidateIniFiles(combined, map);
            if (!clones)
            {
                if (nativeProfiles)
                    ApplyProfiles(map, combined, battle);
                return;
            }

            aliases.Clear();
            occupiedIds.Clear();
            occupiedIds.UnionWith(combined.GetSections());
            nextCloneId = 0;
            CopySection(combined, map, originalCountry, playerCountry);
            map.SetBooleanValue(playerCountry, "Multiplay", true);
            if (game == ClientType.TS)
            {
                map.SetIntValue(playerCountry, "ActsLike", playerIndex);
                int houseKey = Entries(map, "Houses").Select(e => int.TryParse(e.Key, out int n) ? n : -1).DefaultIfEmpty(-1).Max() + 1;
                map.SetStringValue("Houses", houseKey.ToString(CultureInfo.InvariantCulture), playerCountry);
                string side = combined.GetStringValue(originalCountry, "Side", string.Empty);
                foreach (var entry in Entries(combined, "Sides"))
                {
                    var members = Csv(entry.Value).Where(n => !Same(n, playerCountry)).ToList();
                    if (Same(entry.Key, side))
                        members.Add(playerCountry);
                    map.SetStringValue("Sides", entry.Key, string.Join(",", members));
                }
            }
            else
            {
                int key = Entries(combined, "Countries").Select(e => int.TryParse(e.Key, out int n) ? n : -1).DefaultIfEmpty(-1).Max() + 1;
                map.SetStringValue("Countries", key.ToString(CultureInfo.InvariantCulture), playerCountry);
                map.SetStringValue(playerCountry, "ParentCountry", playerCountry);
            }

            // Inherit normal access before forbidding native mobile production for
            // the player. Enemy masks and original unit IDs remain intact.
            foreach (string section in combined.GetSections())
            {
                foreach (var entry in Entries(combined, section))
                {
                    if (!new[] { "Owner", "RequiredHouses", "ForbiddenHouses", "FactoryOwners", "FactoryOwners.Forbidden" }
                        .Contains(entry.Key, StringComparer.OrdinalIgnoreCase))
                        continue;

                    var members = Csv(entry.Value).Where(n => !Same(n, playerCountry)).ToList();
                    if (members.Any(n => Same(n, originalCountry)))
                        members.Add(playerCountry);
                    if (entry.Value != string.Join(",", members))
                        map.SetStringValue(section, entry.Key, string.Join(",", members));
                }
            }

            var sources = mobileLists.SelectMany(list => Entries(combined, list).Select(e => (List: list, Id: e.Value)))
                .Distinct().ToList();
            foreach (var source in sources)
            {
                // A clone always starts from the effective native definition, not
                // from an already buffed enemy definition.
                string required = combined.GetStringValue(source.Id, "RequiredHouses", string.Empty);
                if (!cloneTypes.Contains(source.Id) || Csv(combined.GetStringValue(source.Id, "ForbiddenHouses", string.Empty)).Any(n => Same(n, originalCountry)) || Same(combined.GetStringValue(source.Id, "Buildability", string.Empty), "AIOnly") ||
                    (!string.IsNullOrEmpty(required) && !Same(required, "none") && !Csv(required).Any(n => Same(n, originalCountry))) ||
                    !Csv(combined.GetStringValue(source.Id, "Owner", string.Empty)).Any(n => Same(n, originalCountry)))
                    continue;
                aliases[source.Id] = CloneId("CQU");
            }

            // Deployment forms need their own isolated definitions as well.
            foreach (string source in aliases.Keys.ToArray())
            {
                string linked = combined.GetStringValue(source, "DeploysInto", string.Empty);
                if (linked.Length > 0 && combined.GetSection(linked) != null && !aliases.ContainsKey(linked))
                    aliases[linked] = CloneId("CQD");
            }

            foreach (var alias in aliases)
            {
                string list = sources.FirstOrDefault(s => Same(s.Id, alias.Key)).List ?? "BuildingTypes";
                int key = Entries(map, list).Concat(Entries(combined, list))
                    .Select(e => int.TryParse(e.Key, out int n) ? n : -1).DefaultIfEmpty(-1).Max() + 1;
                map.SetStringValue(list, key.ToString(CultureInfo.InvariantCulture), alias.Value);
                CopySection(combined, map, alias.Key, alias.Value);
                map.SetStringValue(alias.Value, "Image", combined.GetStringValue(alias.Key, "Image", alias.Key));
                map.SetStringValue(alias.Value, "Owner", playerCountry);
                map.SetStringValue(alias.Value, "RequiredHouses", playerCountry);
                map.SetStringValue(alias.Value, "ForbiddenHouses", "none");
                map.SetBooleanValue(alias.Value, "AllowedToStartInMultiplayer", false);
                map.SetBooleanValue(alias.Value, "CrateGoodie", false);
                if (game == ClientType.TS)
                    map.SetStringValue(alias.Value, "Buildability", "HumanOnly");
                foreach (string link in new[] { "DeploysInto", "UndeploysInto", "TransformsInto" })
                {
                    string target = combined.GetStringValue(alias.Key, link, string.Empty);
                    if (aliases.TryGetValue(target, out string? cloneTarget))
                        map.SetStringValue(alias.Value, link, cloneTarget);
                }
                ScaleUnit(map, combined, alias.Key, alias.Value, battle.PlayerStats);
                var forbidden = Csv(map.GetStringValue(alias.Key, "ForbiddenHouses",
                    combined.GetStringValue(alias.Key, "ForbiddenHouses", string.Empty))).Where(n => !Same(n, "none")).ToList();
                forbidden.Add(playerCountry);
                map.SetStringValue(alias.Key, "ForbiddenHouses", string.Join(",", forbidden.Distinct(StringComparer.OrdinalIgnoreCase)));
            }

            // Shared native types outside the explicit clone list stay unchanged.
            // Their human and AI production cannot be isolated safely by guessing.
            var enemyTypes = aliases.Keys.Concat(sources.Where(s => cloneTypes.Contains(s.Id) &&
                (!Csv(combined.GetStringValue(s.Id, "Owner", string.Empty)).Any(n => Same(n, originalCountry)) ||
                Same(combined.GetStringValue(s.Id, "Buildability", string.Empty), "AIOnly"))).Select(s => s.Id));
            foreach (string source in enemyTypes.Distinct(StringComparer.OrdinalIgnoreCase))
                ScaleUnit(map, combined, source, source, battle.AIStats);

            ApplyVeteranProduction(map, combined, battle);
        }

        private void ApplyVeteranProduction(IniFile map, IniFile combined, ConquestBattle battle)
        {
            if (game != ClientType.YR && game != ClientType.Ares)
                return;

            string[] fields = { "VeteranInfantry", "VeteranUnits", "VeteranAircraft" };
            for (int i = 0; i < mobileLists.Length; i++)
            {
                var native = Entries(combined, mobileLists[i]).Select(e => e.Value).ToArray();
                if (battle.PlayerPerks.GetValueOrDefault("veteran-production") > 0)
                {
                    var eligible = native.Where(id => aliases.ContainsKey(id) && combined.GetBooleanValue(id, "Trainable", true)).Select(id => aliases[id]);
                    map.SetStringValue(playerCountry, fields[i], string.Join(",", Csv(map.GetStringValue(playerCountry, fields[i], string.Empty)).Concat(eligible).Distinct()));
                }
                if (battle.AIPerks.GetValueOrDefault("veteran-production") > 0 && aiCountry.Length > 0)
                {
                    var eligible = native.Where(id => cloneTypes.Contains(id) && combined.GetBooleanValue(id, "Trainable", true) && Csv(combined.GetStringValue(id, "Owner", string.Empty)).Any(owner => Same(owner, aiCountry)));
                    map.SetStringValue(aiCountry, fields[i], string.Join(",", Csv(combined.GetStringValue(aiCountry, fields[i], string.Empty)).Concat(eligible).Distinct()));
                }
            }
        }

        private string CloneId(string prefix)
        {
            string id;
            do
            {
                id = prefix + (nextCloneId++).ToString(CultureInfo.InvariantCulture);
            } while (!occupiedIds.Add(id));

            return id;
        }

        private void ScaleUnit(IniFile map, IniFile original, string source, string target,
            Dictionary<ConquestStat, int> stats)
        {
            // Armor is effective durability, matching the randomizers' Strength conversion.
            double health = Factor(stats, ConquestStat.Health) * Factor(stats, ConquestStat.Armor);
            Scale(map, original, source, target, "Strength", health);
            Scale(map, original, source, target, "Speed", Factor(stats, ConquestStat.Speed));
            foreach (var entry in Entries(original, source))
            {
                bool weapon = entry.Key == "Primary" || entry.Key == "Secondary" || entry.Key == "Elite" ||
                    entry.Key == "ElitePrimary" || entry.Key == "EliteSecondary" ||
                    (entry.Key.StartsWith("Weapon", StringComparison.Ordinal) && int.TryParse(entry.Key.Substring(6), out _)) ||
                    (entry.Key.StartsWith("EliteWeapon", StringComparison.Ordinal) && int.TryParse(entry.Key.Substring(11), out _));
                if (!weapon || original.GetSection(entry.Value) == null)
                    continue;

                string cloneWeapon = CloneId("CQW");
                CopySection(original, map, entry.Value, cloneWeapon);
                Scale(map, original, entry.Value, cloneWeapon, "Damage", Factor(stats, ConquestStat.Damage));
                Scale(map, original, entry.Value, cloneWeapon, "ROF", 1 / Factor(stats, ConquestStat.AttackSpeed));
                map.SetStringValue(target, entry.Key, cloneWeapon);
            }
        }

        private static void Scale(IniFile map, IniFile original, string source, string target, string key, double factor)
        {
            int value = original.GetIntValue(source, key, 0);
            if (value > 0 && factor != 1)
                map.SetIntValue(target, key, Math.Max(1, (int)Math.Round(value * factor)));
        }

        private static double Factor(Dictionary<ConquestStat, int> stats, ConquestStat stat) => 1 + stats.GetValueOrDefault(stat) / 100.0;

        private void ApplyProfiles(IniFile map, IniFile combined, ConquestBattle battle)
        {
            string[] profiles = { "Easy", "Normal", "Difficult" };
            string human = profiles[HumanHandicap];
            string ai = profiles[2 - difficulty];
            string[] keys = { "Airspeed", "Groundspeed", "BuildTime", "Armor", "ROF", "Cost", "Firepower" };
            var baseline = keys.ToDictionary(k => k, k => combined.GetDoubleValue("Normal", k, 1));
            var enemy = keys.ToDictionary(k => k, k => combined.GetDoubleValue(ai, k, 1));
            foreach (string key in keys)
            {
                if (double.IsNaN(baseline[key]) || double.IsInfinity(baseline[key]) || baseline[key] <= 0 ||
                    double.IsNaN(enemy[key]) || double.IsInfinity(enemy[key]) || enemy[key] <= 0)
                    throw new InvalidDataException("Conquest requires positive finite handicap multipliers.");

                map.SetDoubleValue(human, key, baseline[key] * ProfileFactor(key, battle.PlayerStats));
                double factor = ProfileFactor(key, battle.AIStats);
                if (factor != 1)
                    map.SetDoubleValue(ai, key, enemy[key] * factor);
            }
        }

        private static double ProfileFactor(string key, Dictionary<ConquestStat, int> stats) => key switch
        {
            "Armor" => Factor(stats, ConquestStat.Health) * Factor(stats, ConquestStat.Armor),
            "Airspeed" or "Groundspeed" => Factor(stats, ConquestStat.Speed),
            "ROF" => 1 / Factor(stats, ConquestStat.AttackSpeed),
            "Firepower" => Factor(stats, ConquestStat.Damage),
            _ => 1
        };

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        private static IEnumerable<string> Csv(string value) => value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0);
        private static IEnumerable<KeyValuePair<string, string>> Entries(IniFile ini, string section) =>
            ini.GetSection(section)?.Keys ?? Enumerable.Empty<KeyValuePair<string, string>>();

        private static void CopySection(IniFile source, IniFile target, string from, string to, int depth = 0)
        {
            if (depth > 24)
                throw new InvalidDataException("Cyclic Conquest rule inheritance.");

            foreach (string inheritance in new[] { "BaseSection", "$Inherits" })
            {
                string parent = source.GetStringValue(from, inheritance, string.Empty);
                if (parent.Length > 0)
                    CopySection(source, target, parent, to, depth + 1);
            }
            foreach (var entry in Entries(source, from))
            {
                if (entry.Key != "BaseSection" && entry.Key != "$Inherits")
                    target.SetStringValue(to, entry.Key, entry.Value);
            }
        }

        public static IniFile Flatten(IniFile rules)
        {
            var flattened = new IniFile();
            foreach (string section in rules.GetSections())
                CopySection(rules, flattened, section, section);

            return flattened;
        }
    }
}
