#nullable enable

using System;
using System.IO;
using System.Diagnostics;
using System.Linq;

using ClientCore;
using ClientCore.Statistics;
using ClientCore.Statistics.GameParsers;

using DTAClient.Domain.Conquest;

using Rampastring.Tools;

internal static class Program
{
    private static int assertions;

    private static void Main(string[] args)
    {
        string directory = Path.Combine(Path.GetTempPath(), "conquest-regressions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Purchases();
            DefensesAndPersistence(directory);
            Generation();
            MovementQueryBudget();
            NativeGrants(directory, ClientType.TS);
            NativeGrants(directory, ClientType.YR);
            Results(directory);
            PaintedGround();
            FailedLaunchRecovery(directory);
            TheaterSelection(directory);
            Adapters(directory, ClientType.TS);
            Adapters(directory, ClientType.YR);
            if (args.Contains("--benchmark"))
                BenchmarkPlacement();
            Console.WriteLine($"Passed {assertions} Conquest regression assertions.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);

        assertions++;
    }

    private static ConquestCampaign Campaign()
    {
        var settings = new ConquestSettings { MapSize = 3, StartingCredits = 10000, Seed = 5 };
        settings.Perks.Add(new ConquestPerkDefinition
        {
            Id = "defense",
            Kind = ConquestPerkKind.StartingDefenses,
            TerritoryOnly = true,
            PurchaseCost = 300,
            AutoCombatStrength = 3
        });
        settings.Perks.Add(new ConquestPerkDefinition
        {
            Id = "troops",
            Kind = ConquestPerkKind.StartingTroops,
            PurchaseCost = 450,
            AutoCombatStrength = 2
        });
        var campaign = ConquestCampaign.Create(settings);
        campaign.Start();
        foreach (var territory in campaign.State.Territories)
        {
            territory.Perks.Clear();
            territory.Buffs.Clear();
        }
        campaign.State.Player.Perks.Clear();
        campaign.State.AI.Perks.Clear();

        return campaign;
    }

    private static void Purchases()
    {
        var campaign = Campaign();
        Check(campaign.BuyPerk(ConquestFaction.Player, "recruitment", 0), "Training must be purchasable.");
        Check(campaign.RecruitmentStrength(ConquestFaction.Player) == 6, "Training must affect recruitment.");
        Check(campaign.BuyPerk(ConquestFaction.Player, "income", 0), "Treasury must be purchasable.");
        Check(campaign.IncomePerTurn(ConquestFaction.Player) == campaign.State.Territories[0].Income + 25, "Treasury must affect income.");
        Check(campaign.BuyPerk(ConquestFaction.Player, "defense", 0), "Owned node can build defenses.");
        Check(campaign.State.Territories[0].Perks["defense"] == 1 && !campaign.State.Player.Perks.ContainsKey("defense"), "Defenses must stay local.");
        Check(!campaign.BuyPerk(ConquestFaction.Player, "defense", 1), "Cannot improve unowned node.");
        Check(!campaign.BuyPerk(ConquestFaction.Neutral, "income", 0), "Neutral cannot shop.");
        Check(!campaign.BuyPerk(ConquestFaction.Player, "missing", 0), "Unknown perk rejected.");
        Check(campaign.BuyPerk(ConquestFaction.Player, "defense", 0) && campaign.BuyPerk(ConquestFaction.Player, "defense", 0), "Can build to cap.");
        int credits = campaign.State.Player.Credits;
        Check(!campaign.BuyPerk(ConquestFaction.Player, "defense", 0) && campaign.State.Player.Credits == credits, "Capped purchase cannot spend credits.");
        campaign.State.Player.Credits = 1;
        Check(!campaign.BuyPerk(ConquestFaction.Player, "troops", 0), "Insufficient credits rejected.");
        campaign.State.Phase = ConquestPhase.AITurn;
        Check(!campaign.BuyPerk(ConquestFaction.Player, "income", 0), "Cannot purchase outside own turn.");
        Check(campaign.BuyPerk(ConquestFaction.AI, "defense", 8), "AI can build local defenses.");
    }

    private static void DefensesAndPersistence(string directory)
    {
        var campaign = Campaign();
        var target = campaign.Neighbors(0).First();
        target.Perks["defense"] = 2;
        Check(campaign.BuyPerk(ConquestFaction.Player, "troops", 0), "Starting troops can be purchased.");
        Check(campaign.BuyPerk(ConquestFaction.Player, "defense", 0), "Source can have defenses.");
        Check(campaign.Move(ConquestFaction.Player, 0, target.Id), "Attack starts.");
        var battle = campaign.State.PendingBattle!;
        Check(battle.DefensePower == 8, "Neutral garrison must include local defenses.");
        Check(battle.AIPerks["defense"] == 2, "Neutral battle opponent receives target defenses.");
        Check(!battle.PlayerPerks.ContainsKey("defense") && battle.PlayerPerks["troops"] == 1, "Attacker must not bring node defenses.");
        Check(!campaign.BuyPerk(ConquestFaction.Player, "troops", 0), "Battle blocks purchases.");
        campaign.State.Player.Perks["troops"] = 2;
        target.Perks["defense"] = 3;
        Check(battle.PlayerPerks["troops"] == 1 && battle.AIPerks["defense"] == 2, "Committed inventories cannot change with live state.");
        var store = new ConquestSaveStore(Path.Combine(directory, "Campaign.json"));
        store.Save(campaign.State);
        campaign = new ConquestCampaign(store.Load());
        Check(campaign.State.PendingBattle!.DefensePower == 8 && campaign.State.PendingBattle.AIPerks["defense"] == 2, "Pending defenses survive reload.");
        Check(campaign.ResolveBattle(battle.Id, true, false), "Capture resolves.");
        Check(target.Id == campaign.State.Armies.Single(a => a.Faction == ConquestFaction.Player).TerritoryId, "Army captures target.");
        Check(campaign.State.Territories[target.Id].Perks["defense"] == 3, "Captured defenses remain on node.");
        Check(campaign.State.Player.Perks["troops"] == 2, "Capture cannot erase purchased perks.");

        campaign = Campaign();
        target = campaign.Neighbors(8).First(t => t.Id != 0);
        target.Owner = ConquestFaction.Player;
        target.Perks["defense"] = 1;
        campaign.State.Phase = ConquestPhase.AITurn;
        Check(campaign.Move(ConquestFaction.AI, 8, target.Id), "AI can attack player defense.");
        Check(campaign.State.PendingBattle!.PlayerPerks["defense"] == 1 && !campaign.State.PendingBattle.AIPerks.ContainsKey("defense"), "Defender snapshot follows human role.");

        campaign = Campaign();
        campaign.State.Version = 4;
        store.Save(campaign.State);
        Check(store.Load().Version == 6, "Version 4 saves remain loadable.");
        campaign.State.Territories[0].Perks["troops"] = 1;
        try
        {
            store.Save(campaign.State);
            throw new Exception("Invalid node perk was saved.");
        }
        catch (InvalidDataException)
        {
            assertions++;
        }
    }

    private static void Generation()
    {
        bool generated = false;
        for (int seed = 1; seed <= 20; seed++)
        {
            var settings = new ConquestSettings { MapSize = 3, Seed = seed, BuffDensity = 2 };
            settings.Perks.Add(new ConquestPerkDefinition { Id = "defense", Kind = ConquestPerkKind.StartingDefenses, TerritoryOnly = true });
            var campaign = ConquestCampaign.Create(settings);
            Check(campaign.State.Territories.All(t => t.RewardId != "defense"), "Local defenses must not become permanent capture rewards.");
            generated |= campaign.State.Territories.Any(t => t.Perks.ContainsKey("defense"));
        }
        Check(generated, "Some new nodes must begin fortified.");
    }

    private static IniFile Roster(ClientType game)
    {
        var rules = new IniFile();
        string[] countries = game == ClientType.TS ? new[] { "GDI", "Nod" } : new[] { "Americans", "Russians", "YuriCountry" };
        string[] infantry = game == ClientType.TS ? new[] { "E1", "E1" } : new[] { "E1", "E2", "INIT" };
        string[] vehicles = game == ClientType.TS ? new[] { "MMCH", "BGGY" } : new[] { "MTNK", "HTNK", "LTNK" };
        string[][] buildings = game == ClientType.TS
            ? new[] { new[] { "GAPOWR", "PROC", "GAPILE", "GAVULC" }, new[] { "NAPOWR", "PROC", "NAHAND", "NALASR" } }
            : new[] { new[] { "GAPOWR", "GAREFN", "GAPILE", "GAPILL" }, new[] { "NAPOWR", "NAREFN", "NAHAND", "NALASR" }, new[] { "YAPOWR", "YAREFN", "YABRCK", "YAGGUN" } };
        for (int side = 0; side < countries.Length; side++)
        {
            rules.SetStringValue(game == ClientType.TS ? "Houses" : "Countries", side.ToString(), countries[side]);
            for (int category = 0; category < 6; category++)
            {
                string id = category == 0 ? infantry[side] : category == 1 ? vehicles[side] : buildings[side][category - 2];
                string list = category == 0 ? "InfantryTypes" : category == 1 ? "VehicleTypes" : "BuildingTypes";
                rules.SetStringValue(list, side + "_" + category, id);
                string owners = rules.GetStringValue(id, "Owner", "");
                rules.SetStringValue(id, "Owner", owners.Length == 0 ? countries[side] : owners + "," + countries[side]);
                rules.SetIntValue(id, "TechLevel", 1);
                rules.SetIntValue(id, "Cost", 300 + category * 100);
                if (category < 2 || category == 5)
                    rules.SetStringValue(id, "Primary", "Weapon");
                if (category == 5)
                    rules.SetBooleanValue(id, "IsBaseDefense", true);
            }
        }
        foreach (var (key, index) in new[] { ("BuildPower", 0), ("BuildRefinery", 1), ("BuildBarracks", 2) })
            rules.SetStringValue("AI", key, string.Join(",", buildings.Select(b => b[index]).Distinct()));
        if (game == ClientType.TS)
        {
            rules.SetStringValue("BuildingTypes", "parent", "GACTWR");
            rules.SetStringValue("GACTWR", "Owner", "GDI");
            rules.SetIntValue("GACTWR", "TechLevel", 2);
            rules.SetIntValue("GACTWR", "Cost", 200);
            rules.SetStringValue("GAVULC", "PowersUpBuilding", "gactwr");
        }

        return rules;
    }

    private static void NativeGrants(string directory, ClientType game)
    {
        var rules = Roster(game);
        int aiSide = game == ClientType.TS ? 1 : 2;
        var catalog = ConquestStartingForces.Catalog(rules, 0, aiSide);
        Check(catalog.Count == 7, game + " must offer escorts, three base structures, local defense and veteran escorts.");
        Check(catalog.Single(p => p.Id == "starting-vehicles").StartingObjects.Single(g => g.Side == aiSide).Type ==
            (game == ClientType.TS ? "BGGY" : "LTNK"), "AI grant uses its own game's faction roster.");
        Check(ConquestStartingForces.Catalog(rules, 0, 99).Count == 0, "Unknown factions cannot receive guessed grants.");
        var sameSide = ConquestStartingForces.Catalog(rules, 0, 0);
        Check(sameSide.All(p => p.StartingObjects.Count <= 1), "Mirror matches have one roster entry per faction.");
        foreach (string fixture in new[] { "terrace.map", "duel.map" })
        {
            var map = new IniFile(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
            var settings = new ConquestSettings { Perks = catalog };
            var battle = new ConquestBattle();
            foreach (var perk in catalog)
            {
                battle.PlayerPerks[perk.Id] = 1;
                battle.AIPerks[perk.Id] = 1;
            }
            battle.PlayerPerks["starting-infantry"] = 2;
            int existing = map.GetSection("Structures")?.Keys.Count ?? 0;
            long beforePlacement = GC.GetAllocatedBytesForCurrentThread();
            ConquestStartingForces.Apply(map, settings, battle, 0, 1, 0, aiSide, id => "CLONE_" + id);
            Check(GC.GetAllocatedBytesForCurrentThread() - beforePlacement < 8 * 1024 * 1024, "Native map placement must stay below the 8 MiB transient allocation budget.");
            var structures = map.GetSection("Structures").Keys.Where(k => k.Key.StartsWith("CQ")).Select(k => k.Value.Split(',')).ToList();
            Check(structures.Count == 8 && map.GetSection("Structures").Keys.Count == existing + 8, "Both factions get base buildings and defenses without replacing map objects.");
            Check(structures.Count(v => v[0] == "Spawn1") == 4 && structures.Count(v => v[0] == "Spawn2") == 4, "Buildings belong to each resolved start house.");
            var escort = map.GetSection("Infantry").Keys.Where(k => k.Key.StartsWith("CQ")).Select(k => k.Value.Split(',')).ToList();
            Check(escort.Count == 9 && escort.Count(v => v[1].StartsWith("CLONE_")) == 6 && escort.All(v => v[9] == "1"), "Escort levels, aliases and veteran rank apply to both factions.");
            var vehicles = map.GetSection("Units").Keys.Where(k => k.Key.StartsWith("CQ")).Select(k => k.Value.Split(',')).ToList();
            Check(vehicles.Count == 2 && vehicles.All(v => v[8] == "1"), "Vehicle escorts start veteran after purchase.");
            foreach (var building in structures)
            {
                int x = int.Parse(building[3]);
                int y = int.Parse(building[4]);
                Check(structures.Where(v => !ReferenceEquals(v, building)).All(v =>
                    Math.Abs(x - int.Parse(v[3])) >= 5 || Math.Abs(y - int.Parse(v[4])) >= 5), "Starting structures have distinct reserved foundations.");
            }
            if (game == ClientType.TS)
                Check(structures.Any(v => v[1] == "CLONE_GACTWR" && v[10] == "1" && v[12] == "GAVULC"), "GDI component tower receives its native weapon upgrade.");
            var broken = new IniFile();
            broken.SetStringValue("IsoMapPack5", "1", "invalid base64");
            try
            {
                ConquestStartingForces.Apply(broken, settings, battle, 0, 1, 0, aiSide);
                throw new Exception("Invalid terrain was accepted.");
            }
            catch (InvalidDataException)
            {
                Check(!broken.SectionExists("Units") && !broken.SectionExists("Structures"), "Failed placement cannot partially grant objects.");
            }
            var state = ConquestCampaign.Create(settings).State;
            var store = new ConquestSaveStore(Path.Combine(directory, "Native" + game + ".json"));
            store.Save(state);
            Check(store.Load().Settings.Perks.Single(p => p.Id == "starting-vehicles").StartingObjects.Count == 2, "Native faction grants survive saves.");
        }
        rules.SetStringValue(game == ClientType.TS ? "MMCH" : "MTNK", "ForbiddenHouses", game == ClientType.TS ? "GDI" : "Americans");
        Check(!ConquestStartingForces.Catalog(rules, 0, aiSide).Any(p => p.Id == "starting-vehicles"), "Forbidden faction units cannot be sold as escorts.");
    }

    private static void PaintedGround()
    {
        string fixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var configuration = ConquestBattleConfiguration.Read(ClientType.TS, new IniFile());
        var terrain = configuration.LoadTerrain(fixtureRoot, "TEMPERATE");
        Check(terrain.GetIntValue("General", "SandTile", -1) >= 0, "Load this game's native theater definitions for painted ground.");
        var settings = new ConquestSettings { Perks = ConquestStartingForces.Catalog(Roster(ClientType.TS), 1, 0) };
        var battle = new ConquestBattle();
        foreach (string id in new[] { "starting-power", "starting-refinery", "starting-barracks" })
            battle.PlayerPerks[id] = 1;
        string path = Path.Combine(fixtureRoot, "pain_redefined.map");
        var map = new IniFile(path);
        ConquestStartingForces.Apply(map, settings, battle, 0, 1, 1, 0, terrain: terrain);
        var buildings = map.GetSection("Structures").Keys.Where(k => k.Key.StartsWith("CQ")).Select(k => k.Value.Split(',')).ToList();
        Check(buildings.Count == 3 && buildings.All(v => v[0] == "Spawn1"), "The reported Pain Redefined battle must place all three purchased Nod buildings.");
        Check(buildings.Select(v => v[1]).OrderBy(s => s).SequenceEqual(new[] { "NAHAND", "NAPOWR", "PROC" }), "Painted-ground recovery must preserve the purchased building types.");
        var restricted = new IniFile(path);
        try
        {
            ConquestStartingForces.Apply(restricted, settings, battle, 0, 1, 1, 0, terrain: terrain, canBuildOn: _ => false);
            throw new Exception("Non-buildable ground was accepted.");
        }
        catch (InvalidDataException)
        {
            Check(restricted.GetSection("Structures")?.Keys.All(k => !k.Key.StartsWith("CQ")) != false,
                "Mod ground restrictions must not be bypassed to force a launch.");
        }
        var mirror = new IniFile(path);
        foreach (var perk in settings.Perks.Where(p => p.StartingObjects.Count > 0))
        {
            battle.PlayerPerks[perk.Id] = perk.MaximumLevel;
            battle.AIPerks[perk.Id] = perk.MaximumLevel;
        }
        ConquestStartingForces.Apply(mirror, settings, battle, 1, 0, 1, 0, terrain: terrain);
        Check(mirror.GetSection("Structures").Keys.Count(k => k.Key.StartsWith("CQ")) == 8, "Both Pain Redefined starts must support native base and defense grants.");
    }

    private static void FailedLaunchRecovery(string directory)
    {
        var campaign = Campaign();
        int target = campaign.Neighbors(0).First().Id;
        Check(campaign.Move(ConquestFaction.Player, 0, target), "Prepare a pending battle for launch recovery.");
        var battle = campaign.State.PendingBattle!;
        battle.SkirmishLaunched = true;
        int power = battle.AttackPower;
        int history = campaign.State.History.Count;
        bool started = ConquestSkirmish.TryLaunch(battle, () =>
            throw new InvalidDataException("No safe starting-force placement."), out var error);
        Check(!started && error is InvalidDataException && !battle.SkirmishLaunched, "Placement errors must return to the campaign instead of escaping to the client crash handler.");
        Check(ReferenceEquals(campaign.State.PendingBattle, battle) && battle.AttackPower == power && campaign.State.History.Count == history,
            "Failed launch must retain the pending attack and committed bonuses without inventing a result.");
        var store = new ConquestSaveStore(Path.Combine(directory, "FailedLaunch.json"));
        store.Save(campaign.State);
        Check(!store.Load().PendingBattle!.SkirmishLaunched, "Recovery must persist a retryable battle.");
        battle.SkirmishLaunched = true;
        Check(ConquestSkirmish.TryLaunch(battle, () => { }, out error) && error == null && battle.SkirmishLaunched,
            "A corrected preparation can retry the same battle.");
    }

    private static void TheaterSelection(string directory)
    {
        string root = Path.Combine(directory, "TheaterSelection");
        Directory.CreateDirectory(Path.Combine(root, "INI"));
        File.WriteAllText(Path.Combine(root, "INI", "temperat.ini"), "[General]\nSandTile=1\n");
        File.WriteAllText(Path.Combine(root, "INI", "temperatmd.ini"), "[General]\nSandTile=2\n");
        var ts = ConquestBattleConfiguration.Read(ClientType.TS, new IniFile());
        var yr = ConquestBattleConfiguration.Read(ClientType.YR, new IniFile());
        Check(ts.LoadTerrain(root, "TEMPERATE").GetIntValue("General", "SandTile", 0) == 1, "TS must use its native theater definition.");
        Check(yr.LoadTerrain(root, "TEMPERATE").GetIntValue("General", "SandTile", 0) == 2, "YR must prefer its own expansion theater definition.");
        Check(yr.LoadTerrain(root, "../outside").GetSections().Count == 0, "A theater name cannot escape the selected game installation.");
    }

    private static void MovementQueryBudget()
    {
        var campaign = Campaign();
        int target = campaign.Neighbors(0).First().Id;
        for (int i = 0; i < 32; i++)
            campaign.CanMove(ConquestFaction.Player, 0, target);

        long before = GC.GetAllocatedBytesForCurrentThread();
        bool allowed = true;
        for (int i = 0; i < 1000; i++)
            allowed &= campaign.CanMove(ConquestFaction.Player, 0, target);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allowed && allocated <= 1024, "Repeated movement previews must not create per-query iterators or closures.");
    }

    private static void BenchmarkPlacement()
    {
        var settings = new ConquestSettings { Perks = ConquestStartingForces.Catalog(Roster(ClientType.TS), 0, 1) };
        var battle = new ConquestBattle();
        foreach (var perk in settings.Perks)
        {
            battle.PlayerPerks[perk.Id] = perk.MaximumLevel;
            battle.AIPerks[perk.Id] = perk.MaximumLevel;
        }
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "terrace.map");
        ConquestStartingForces.Apply(new IniFile(path), settings, battle, 0, 1, 0, 1);
        long allocated = 0;
        long ticks = 0;
        for (int i = 0; i < 8; i++)
        {
            var map = new IniFile(path);
            long before = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            ConquestStartingForces.Apply(map, settings, battle, 0, 1, 0, 1);
            ticks += Stopwatch.GetTimestamp() - start;
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Console.WriteLine($"Conquest placement: {ticks * 1000.0 / Stopwatch.Frequency / 8:F2} ms/run; {allocated / 8 / 1024.0:F1} KiB allocated/run (Terrace, maximum native grants).");
    }

    private static void Results(string directory)
    {
        foreach (string outcome in new[] { "Winner", "Loser", "Losser" })
        {
            var campaign = Campaign();
            campaign.Move(ConquestFaction.Player, 0, campaign.Neighbors(0).First().Id);
            var battle = campaign.State.PendingBattle!;
            battle.SkirmishLaunched = true;
            battle.SkirmishGameId = 123;
            battle.LocalPlayerName = "Commander";
            var statistics = new MatchStatistics("test", 123, "Map", "Conquest", 1);
            statistics.AddPlayer("Commander", true, false, false, 0, 0, 0, 0);
            statistics.AddPlayer("Computer", false, true, false, 1, 0, 1, 1);
            string file = "Result.log";
            File.WriteAllText(Path.Combine(directory, file), "Commander: " + outcome + "\n Lost = 3\nComputer: " + (outcome == "Winner" ? "Loser" : "Winner") + "\n");
            new LogFileStatisticsParser(statistics, false).ParseStats(directory, file, false);
            Check(statistics.SawCompletion && statistics.Players[0].SawEnd, "Native scoreboard result is explicit, including legacy TS Losser.");
            Check(ConquestSkirmish.TryResolve(campaign, statistics), "Parsed native result resolves campaign battle.");
            Check(campaign.State.History.GetEvents(ConquestHistoryFilter.All).Last(e => e.EventType == ConquestEventType.SkirmishResult).Success ==
                (outcome == "Winner"), "Native victory and defeat produce the correct campaign outcome.");
            Check(!ConquestSkirmish.TryResolve(campaign, statistics), "Repeated completion cannot award a second victory.");
        }
        var missing = new MatchStatistics("test", 123, "Map", "Conquest", 1);
        new LogFileStatisticsParser(missing, false).ParseStats(directory, "Missing.log", false);
        Check(!missing.SawCompletion, "Missing logs never imply victory.");
    }

    private static void Adapters(string directory, ClientType game)
    {
        File.WriteAllText(Path.Combine(directory, "Grant.ini"), """
            [Player@0:Units]
            CQP={PlayerSpawn},{PlayerUnit:TANK},256,{PlayerStartX:4},{PlayerStartY:-3},64,Guard,None,0,-1,0,-1,1,0
            [AI@1:Structures]
            CQA={AISpawn},DEFENSE,256,{AIStartX:-4},{AIStartY:3},64,None,1,0,1,0,0,None,None,None,0,0
            [Player@1:Units]
            WRONG=wrong faction
            [Player:GrantTeam]
            VeteranLevel=2
            Waypoint={PlayerStartWaypoint}
            [AI:GrantLocation]
            Cell={AIStartCell:-4:3}
            """);
        var ini = new IniFile();
        ini.SetStringValue("ConquestPerk:garrison", "Kind", "StartingDefenses");
        ini.SetStringValue("ConquestPerk:garrison", "Games", game.ToString());
        ini.SetStringValue("ConquestPerk:garrison", "MapCode", "Grant.ini");
        var configuration = ConquestBattleConfiguration.Read(game, ini);
        var settings = new ConquestSettings { Perks = configuration.LoadPerks(directory, 0, 1) };
        Check(settings.Perks.Single(p => p.Id == "garrison").TerritoryOnly, "Configured starting defenses default to local scope.");
        var other = ConquestBattleConfiguration.Read(game == ClientType.TS ? ClientType.YR : ClientType.TS, ini);
        Check(!other.LoadPerks(directory, 0, 1).Any(p => p.Id == "garrison"), "Perks cannot leak across engine packs.");
        var battle = new ConquestBattle();
        battle.PlayerPerks["garrison"] = 1;
        battle.AIPerks["garrison"] = 2;
        var map = new IniFile();
        map.SetIntValue("Waypoints", "3", 60050);
        map.SetIntValue("Waypoints", "6", 90080);
        map.SetStringValue("Units", "EXISTING", "retain this map unit");
        ConquestPerkRules.Apply(map, settings, battle, directory, false, 3, 6, id => "PRIVATE_" + id, 0, 1);
        Check(map.GetStringValue("Units", "CQP", "").StartsWith("Spawn4,PRIVATE_TANK,256,54,57,"), "Player grant follows resolved base and clone alias.");
        Check(map.GetStringValue("Structures", "CQA", "").StartsWith("Spawn7,DEFENSE,256,76,93,"), "AI defense follows its own base.");
        Check(!map.KeyExists("Units", "WRONG"), "Wrong faction adapter must be skipped.");
        Check(map.GetStringValue("Units", "EXISTING", "") == "retain this map unit", "Existing map units must survive.");
        Check(map.GetIntValue("GrantTeam", "VeteranLevel", 0) == 2, "Adapter preserves explicit starting veterancy.");
        Check(map.GetIntValue("GrantLocation", "Cell", 0) == 93076, "Packed cell offsets must preserve axes.");
        try
        {
            ConquestPerkRules.Apply(new IniFile(), settings, battle, directory, false, -1, -1, null, 0, 1);
            throw new Exception("Missing start was accepted.");
        }
        catch (InvalidOperationException)
        {
            assertions++;
        }
    }
}