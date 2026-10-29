#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using ClientCore;
using ClientCore.Extensions;

using Rampastring.Tools;

namespace DTAClient.Domain.Conquest
{
    /// <summary>
    /// Engine capabilities are explicit. Cloning requires a mod-owned effective rule
    /// source and, on Vinifera, a reserved production mask; DLL presence is insufficient.
    /// </summary>
    public sealed class ConquestBattleConfiguration
    {
        public bool NativeProfiles { get; private set; }
        public bool EnableUnitClones { get; private set; }
        public int ProductionHouseIndex { get; private set; } = -1;
        public List<string> CloneTypes { get; private set; } = new();
        private ClientType game;
        private IniFile perkConfiguration = new();
        public string RulesFile { get; private set; } = string.Empty;
        public List<ConquestStat> SupportedStats { get; private set; } = new();

        public static ConquestBattleConfiguration Read(ClientType game, IniFile configuration)
        {
            bool knownEngine = game == ClientType.TS || game == ClientType.YR || game == ClientType.Ares;
            var result = new ConquestBattleConfiguration
            {
                game = game,
                perkConfiguration = configuration,
                CloneTypes = configuration.GetStringValue("Conquest", "CloneTypes", string.Empty).Split(',')
                    .Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                NativeProfiles = knownEngine && configuration.GetBooleanValue("Conquest", "NativeProfiles", true),
                EnableUnitClones = knownEngine && configuration.GetBooleanValue("Conquest", "EnableUnitClones", false),
                ProductionHouseIndex = configuration.GetIntValue("Conquest", "ProductionHouseIndex", -1),
                RulesFile = configuration.GetStringValue("Conquest", "RulesFile", string.Empty)
            };
            // No accidental fallback to installed global rule names for cloning.
            result.EnableUnitClones &= result.RulesFile.Length > 0 && result.CloneTypes.Count > 0 &&
                (game != ClientType.TS || (result.ProductionHouseIndex >= 6 && result.ProductionHouseIndex < 31));

            string allowed = configuration.GetStringValue("Conquest", "SupportedStats",
                (result.NativeProfiles || result.EnableUnitClones) ? "Health,Armor,Speed,AttackSpeed,Damage" : string.Empty);
            foreach (string name in allowed.Split(','))
            {
                if (Enum.TryParse(name.Trim(), true, out ConquestStat stat) && Enum.IsDefined(typeof(ConquestStat), stat) &&
                    !result.SupportedStats.Contains(stat))
                    result.SupportedStats.Add(stat);
            }

            return result;
        }

        public List<ConquestPerkDefinition> LoadPerks(string gamePath, int? playerSide = null, int? aiSide = null)
        {
            var result = ConquestPerkDefinition.StrategicDefaults();
            var effective = EnableUnitClones ? LoadRules(gamePath) : new IniFile();
            var countries = effective.GetSection("Countries")?.Keys.Select(e => e.Value).Distinct().ToList() ?? new List<string>();
            bool rosterFor(int? side)
            {
                if (side.HasValue && (side.Value < 0 || side.Value >= countries.Count))
                    return false;
                string country = side.HasValue ? countries[side.Value] : string.Empty;
                bool includes(string value) => value.Split(',').Any(s => string.Equals(s.Trim(), country, StringComparison.OrdinalIgnoreCase));

                return new[] { "InfantryTypes", "VehicleTypes", "AircraftTypes" }.Any(list =>
                    effective.GetSection(list)?.Keys.Any(entry => CloneTypes.Contains(entry.Value, StringComparer.OrdinalIgnoreCase) &&
                        effective.GetBooleanValue(entry.Value, "Trainable", true) &&
                        (!side.HasValue || (includes(effective.GetStringValue(entry.Value, "Owner", string.Empty)) &&
                        !includes(effective.GetStringValue(entry.Value, "ForbiddenHouses", string.Empty)) &&
                        (string.IsNullOrEmpty(effective.GetStringValue(entry.Value, "RequiredHouses", string.Empty)) ||
                        string.Equals(effective.GetStringValue(entry.Value, "RequiredHouses", string.Empty), "none", StringComparison.OrdinalIgnoreCase) ||
                        includes(effective.GetStringValue(entry.Value, "RequiredHouses", string.Empty)))))) == true);
            }
            bool veteranRoster = rosterFor(playerSide) && rosterFor(aiSide);
            if (EnableUnitClones && veteranRoster && (game == ClientType.YR || game == ClientType.Ares))
                result.Add(new ConquestPerkDefinition
                {
                    Id = "veteran-production", Kind = ConquestPerkKind.VeteranProduction,
                    MaximumLevel = 1, AutoCombatStrength = 1
                });

            foreach (string section in perkConfiguration.GetSections().Where(s => s.StartsWith("ConquestPerk:", StringComparison.Ordinal)))
            {
                string id = section.Substring("ConquestPerk:".Length);
                if (id.Length == 0 || id.Length > 80 || result.Any(p => p.Id == id) ||
                    !Enum.TryParse(perkConfiguration.GetStringValue(section, "Kind", string.Empty), true, out ConquestPerkKind kind) ||
                    !Enum.IsDefined(typeof(ConquestPerkKind), kind))
                    throw new InvalidDataException("Invalid or duplicate Conquest perk definition.");

                // Engine-specific rewards must explicitly name supported engines and
                // provide actual rules. A label alone never advertises an unsupported grant.
                if (!perkConfiguration.GetStringValue(section, "Games", string.Empty).Split(',')
                    .Any(s => string.Equals(s.Trim(), game.ToString(), StringComparison.OrdinalIgnoreCase)))
                    continue;

                string[] allowedSides = perkConfiguration.GetStringValue(section, "Sides", string.Empty).Split(',');
                if (allowedSides.Any(s => !string.IsNullOrWhiteSpace(s)))
                {
                    var sides = new List<int>();
                    foreach (string value in allowedSides)
                    {
                        if (!int.TryParse(value.Trim(), out int side) || side < 0)
                            throw new InvalidDataException("Invalid Conquest perk side restriction.");
                        sides.Add(side);
                    }
                    if ((playerSide.HasValue && !sides.Contains(playerSide.Value)) || (aiSide.HasValue && !sides.Contains(aiSide.Value)))
                        continue;
                }

                var perk = new ConquestPerkDefinition
                {
                    Id = id, Kind = kind,
                    Name = perkConfiguration.GetStringValue(section, "Name", string.Empty),
                    Description = perkConfiguration.GetStringValue(section, "Description", string.Empty),
                    MaximumLevel = Math.Max(1, Math.Min(3, perkConfiguration.GetIntValue(section, "MaximumLevel", 1))),
                    Amount = Math.Max(0, Math.Min(100, perkConfiguration.GetIntValue(section, "Amount", 0))),
                    AutoCombatStrength = Math.Max(0, Math.Min(10, perkConfiguration.GetIntValue(section, "AutoCombatStrength", 0))),
                    MapCodeFile = perkConfiguration.GetStringValue(section, "MapCode", string.Empty),
                    SpawnCodeFile = perkConfiguration.GetStringValue(section, "SpawnCode", string.Empty)
                };
                if (kind != ConquestPerkKind.Recruitment && kind != ConquestPerkKind.Income &&
                    perk.MapCodeFile.Length == 0 && perk.SpawnCodeFile.Length == 0)
                    throw new InvalidDataException("Starting grants and veterancy require an explicit engine-specific perk adapter.");

                foreach (string path in new[] { perk.MapCodeFile, perk.SpawnCodeFile }.Where(s => s.Length > 0))
                    LoadGameIni(gamePath, path);
                result.Add(perk);
            }
            if (result.Count > 32)
                throw new InvalidDataException("Too many Conquest perk definitions.");

            return result;
        }

        public static IniFile LoadGameIni(string gamePath, string relativePath)
        {
            string root = Path.GetFullPath(gamePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                throw new InvalidDataException("Conquest adapters must name existing INI files inside this game installation.");

            return new IniFile(path, FileExtensions.GetDetectedEncoding(path));
        }

        public IniFile LoadRules(string gamePath)
        {
            if (RulesFile.Length == 0)
            {
                var baseline = new IniFile();
                string name = game == ClientType.TS ? "rules.ini" : "rulesmd.ini";
                if (!NativeProfiles)
                    return baseline;

                foreach (string directory in new[] { gamePath, Path.Combine(gamePath, "INI") })
                {
                    if (!Directory.Exists(directory))
                        continue;
                    string? baselinePath = Directory.EnumerateFiles(directory).FirstOrDefault(p => string.Equals(Path.GetFileName(p), name, StringComparison.OrdinalIgnoreCase));
                    if (baselinePath != null)
                        IniFile.ConsolidateIniFiles(baseline, new IniFile(baselinePath));
                }

                return ConquestBattleRules.Flatten(baseline);
            }

            string root = Path.GetFullPath(gamePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(root, RulesFile));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                throw new InvalidDataException("Conquest RulesFile must name an existing effective rules file inside the game installation.");

            return ConquestBattleRules.Flatten(new IniFile(path));
        }
    }
}
