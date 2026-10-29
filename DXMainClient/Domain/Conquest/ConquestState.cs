#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DTAClient.Domain.Conquest
{
    public enum ConquestFaction
    {
        Player,
        AI,
        Neutral
    }
    public enum ConquestPhase
    {
        PlayerTurn,
        AITurn,
        AwaitingBattle,
        Finished
    }
    public enum ConquestBonus
    {
        None,
        Income,
        Attack,
        Defense
    }

    public enum ConquestStat
    {
        Health, Armor, Speed, AttackSpeed, Damage
    }

    public sealed class ConquestMapAssignment
    {
        public string Hash { get; set; } = string.Empty;
        public string GameMode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public sealed class ConquestSettings
    {
        public int MapSize { get; set; } = 5;
        public int StartingCredits { get; set; } = 500;
        public int Seed { get; set; } = 1;
        public int Difficulty { get; set; } = 1;
        public int BuffDensity { get; set; } = 1;
        public List<ConquestStat> SupportedStats { get; set; } = new()
        {
            ConquestStat.Health, ConquestStat.Armor, ConquestStat.Speed, ConquestStat.AttackSpeed, ConquestStat.Damage
        };
        public List<ConquestPerkDefinition> Perks { get; set; } = ConquestPerkDefinition.StrategicDefaults();
        public List<ConquestMapAssignment> BattleMaps { get; set; } = new();
        public string BattleMapHash { get; set; } = string.Empty;
        public string BattleGameMode { get; set; } = string.Empty;
        public int PlayerSide { get; set; }
        public int AISide { get; set; }
        public Dictionary<string, int> DropDownOptions { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, bool> CheckBoxOptions { get; set; } = new Dictionary<string, bool>();
    }

    public sealed class ConquestTerritory
    {
        public int Id { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public float MapX { get; set; }
        public float MapY { get; set; }
        public ConquestFaction Owner { get; set; } = ConquestFaction.Neutral;
        public ConquestBonus Bonus { get; set; }
        public int Income { get; set; } = 100;
        public Dictionary<ConquestStat, int> Buffs { get; set; } = new();
        public Dictionary<string, int> Perks { get; set; } = new();
        public string RewardId { get; set; } = string.Empty;
        public List<ConquestFaction> RewardClaimedBy { get; set; } = new();
        public ConquestMapAssignment BattleMap { get; set; } = new();
    }

    public sealed class ConquestArmy
    {
        public long Id { get; set; }
        public ConquestFaction Faction { get; set; }
        public int TerritoryId { get; set; }
        public int Strength { get; set; }
        public bool HasActed { get; set; }
    }

    public sealed class ConquestConnection
    {
        public int Source { get; set; }
        public int Target { get; set; }

        public bool Joins(int a, int b) => (Source == a && Target == b) || (Source == b && Target == a);
    }

    public sealed class ConquestEconomy
    {
        public int Credits { get; set; }
        public int UpgradeLevel { get; set; }
        public Dictionary<ConquestStat, int> Upgrades { get; set; } = new();
        public Dictionary<string, int> Perks { get; set; } = new();
        public int TemporaryBonuses { get; set; }
    }

    public sealed class ConquestBattle
    {
        public long Id { get; set; }
        public long ArmyId { get; set; }
        public ConquestFaction Attacker { get; set; }
        public int SourceTerritoryId { get; set; }
        public int TargetTerritoryId { get; set; }
        public int AttackPower { get; set; }
        public int DefensePower { get; set; }
        public Dictionary<ConquestStat, int> PlayerStats { get; set; } = new();
        public Dictionary<ConquestStat, int> AIStats { get; set; } = new();
        public Dictionary<string, int> PlayerPerks { get; set; } = new();
        public Dictionary<string, int> AIPerks { get; set; } = new();
        public bool SkirmishLaunched { get; set; }
        public int SkirmishGameId { get; set; }
        public string LocalPlayerName { get; set; } = string.Empty;
    }

    /// <summary>
    /// All strategic state, including the AI continuation and unresolved battle.
    /// No UI or engine objects belong in campaign saves.
    /// </summary>
    public sealed class ConquestState
    {
        public int Version { get; set; } = 6;
        public Guid CampaignId { get; set; } = Guid.NewGuid();
        public bool Started { get; set; }
        public ConquestSettings Settings { get; set; } = new ConquestSettings();
        public int TurnNumber { get; set; } = 1;
        public ConquestPhase Phase { get; set; } = ConquestPhase.PlayerTurn;
        public ConquestFaction? Winner { get; set; }
        public uint RandomState { get; set; } = 1;
        public long NextId { get; set; } = 1;
        public List<ConquestTerritory> Territories { get; set; } = new List<ConquestTerritory>();
        public int MapLayoutVersion { get; set; }
        public List<ConquestConnection> Connections { get; set; } = new List<ConquestConnection>();
        public List<ConquestArmy> Armies { get; set; } = new List<ConquestArmy>();
        public ConquestEconomy Player { get; set; } = new ConquestEconomy();
        public ConquestEconomy AI { get; set; } = new ConquestEconomy();
        public ConquestBattle? PendingBattle { get; set; }
        public bool AIRecruited { get; set; }
        public bool AIPurchased { get; set; }
        public int AnimationSpeed { get; set; } = 1;
        public long HistoryBytes { get; set; }
        public int SavedEventCount { get; set; }

        [JsonIgnore]
        public ConquestHistory History { get; } = new ConquestHistory();
    }
}
