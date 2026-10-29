#nullable enable

using System.Collections.Generic;

namespace DTAClient.Domain.Conquest
{
    public enum ConquestPerkKind
    {
        Recruitment, Income, StartingTroops, StartingDefenses, VeteranProduction, StartingStructures
    }

    /// <summary>
    /// Permanent capture rewards and purchases. Starting objects come from effective game rules;
    /// game-pack templates remain available for specialized grants.
    /// </summary>
    public sealed class ConquestPerkDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ConquestPerkKind Kind { get; set; }
        public int MaximumLevel { get; set; } = 3;
        public int Amount { get; set; }
        public int AutoCombatStrength { get; set; }
        public int PurchaseCost { get; set; } = 500;
        public bool TerritoryOnly { get; set; }
        public List<ConquestStartingObject> StartingObjects { get; set; } = new();
        public string MapCodeFile { get; set; } = string.Empty;
        public string SpawnCodeFile { get; set; } = string.Empty;

        public static List<ConquestPerkDefinition> StrategicDefaults() => new()
        {
            new ConquestPerkDefinition { Id = "recruitment", Kind = ConquestPerkKind.Recruitment, Amount = 1 },
            new ConquestPerkDefinition { Id = "income", Kind = ConquestPerkKind.Income, Amount = 25 }
        };
    }

    public sealed class ConquestBonusSummary
    {
        public Dictionary<ConquestStat, int> Research { get; } = new();
        public Dictionary<ConquestStat, int> Territories { get; } = new();
        public Dictionary<ConquestStat, int> Total { get; } = new();
        public int Income { get; set; }
        public int RecruitmentStrength { get; set; }
        public int Supplies { get; set; }
        public int StatIndex { get; set; }
        public int ArmyStrength { get; set; }
    }
}