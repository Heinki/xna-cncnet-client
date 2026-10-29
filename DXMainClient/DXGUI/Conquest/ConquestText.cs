#nullable enable

using ClientCore.Extensions;

using DTAClient.Domain.Conquest;

namespace DTAClient.DXGUI.Conquest
{
    internal static class ConquestText
    {
        public static string Territory(ConquestTerritory territory) => string.IsNullOrEmpty(territory.BattleMap.Name)
            ? "Territory".L10N("Client:Conquest:UnnamedTerritory") : territory.BattleMap.Name;

        public static string Faction(ConquestFaction faction) => faction switch
        {
            ConquestFaction.Player => "PLAYER".L10N("Client:Conquest:Player"),
            ConquestFaction.AI => "AI".L10N("Client:Conquest:AI"),
            _ => "NEUTRAL".L10N("Client:Conquest:Neutral")
        };

        public static string Stat(ConquestStat stat) => stat switch
        {
            ConquestStat.Health => "HP".L10N("Client:Conquest:Health"),
            ConquestStat.Armor => "Armor".L10N("Client:Conquest:Armor"),
            ConquestStat.Speed => "Speed".L10N("Client:Conquest:Speed"),
            ConquestStat.AttackSpeed => "Attack speed".L10N("Client:Conquest:AttackSpeed"),
            _ => "Damage".L10N("Client:Conquest:Damage")
        };

        public static string PerkName(ConquestPerkDefinition perk) => PerkName(perk.Id, perk.Kind, perk.Name);

        private static string PerkName(string id, ConquestPerkKind kind, string custom) => !string.IsNullOrEmpty(custom) ? custom : id switch
        {
            "starting-infantry" => "Infantry escort".L10N("Client:Conquest:Perk:InfantryEscort"),
            "starting-vehicles" => "Vehicle escort".L10N("Client:Conquest:Perk:VehicleEscort"),
            "starting-power" => "Field power plant".L10N("Client:Conquest:Perk:PowerPlant"),
            "starting-refinery" => "Field refinery".L10N("Client:Conquest:Perk:Refinery"),
            "starting-barracks" => "Field barracks".L10N("Client:Conquest:Perk:Barracks"),
            "veteran-escorts" => "Veteran escorts".L10N("Client:Conquest:Perk:VeteranEscorts"),
            "node-defenses" => "Node garrison".L10N("Client:Conquest:Perk:NodeGarrison"),
            _ => PerkName(kind, custom)
        };

        private static string PerkName(ConquestPerkKind kind, string custom) => !string.IsNullOrEmpty(custom) ? custom : kind switch
        {
            ConquestPerkKind.Recruitment => "Recruitment training".L10N("Client:Conquest:Perk:Recruitment"),
            ConquestPerkKind.Income => "War treasury".L10N("Client:Conquest:Perk:Income"),
            ConquestPerkKind.StartingTroops => "Starting troops".L10N("Client:Conquest:Perk:StartingTroops"),
            ConquestPerkKind.StartingStructures => "Starting base structures".L10N("Client:Conquest:Perk:StartingStructures"),
            ConquestPerkKind.StartingDefenses => "Starting defenses".L10N("Client:Conquest:Perk:StartingDefenses"),
            _ => "Veteran production".L10N("Client:Conquest:Perk:VeteranProduction")
        };

        public static string PerkDescription(ConquestPerkDefinition perk) => !string.IsNullOrEmpty(perk.Description) ? perk.Description : perk.Id == "veteran-escorts"
            ? "Purchased infantry and vehicle escorts start at veteran rank. Requires an escort perk.".L10N("Client:Conquest:Perk:VeteranEscortsHelp") : perk.StartingObjects.Count > 0
            ? string.Format(perk.TerritoryOnly
                ? "Start with {0} faction defense per level when defending this node.".L10N("Client:Conquest:Perk:NativeDefenseHelp")
                : perk.Kind == ConquestPerkKind.StartingStructures
                    ? "Start every battle with {0} faction building already placed near your base.".L10N("Client:Conquest:Perk:NativeBuildingHelp")
                    : "Start every battle with {0} extra faction troops per level near your base.".L10N("Client:Conquest:Perk:NativeTroopsHelp"), perk.StartingObjects[0].Count)
            : perk.Kind switch
            {
                ConquestPerkKind.Recruitment => string.Format("Each purchase adds +{0} army power when recruiting. More power improves autobattle odds.".L10N("Client:Conquest:Perk:TrainingExplanation"), perk.Amount),
                ConquestPerkKind.Income => string.Format("Each purchase earns +{0} campaign credits at the start of every turn.".L10N("Client:Conquest:Perk:TreasuryExplanation"), perk.Amount),
                ConquestPerkKind.StartingTroops => "Extra troops at battle start, provided by this game pack.".L10N("Client:Conquest:Perk:TroopsHelp"),
                ConquestPerkKind.StartingDefenses => "Extra defenses at battle start, provided by this game pack.".L10N("Client:Conquest:Perk:DefensesHelp"),
                _ => "Configured combat units are constructed at veteran rank.".L10N("Client:Conquest:Perk:VeteranHelp")
            };

        private static string EventBonus(ConquestEvent action) => action.Stat.HasValue
            ? string.Format("{0} +{1}%".L10N("Client:Conquest:StatBonus"), Stat(action.Stat.Value), action.Amount)
            : action.Bonus == ConquestBonus.Income && action.Amount > 0
                ? string.Format("Income: {0} credits / turn".L10N("Client:Conquest:NodeIncome"), action.Amount) : Bonus(action.Bonus);

        public static string Bonus(ConquestBonus bonus) => bonus switch
        {
            ConquestBonus.Income => "Mine (+75 income)".L10N("Client:Conquest:IncomeBonus"),
            ConquestBonus.Attack => "Armory (+2 attack)".L10N("Client:Conquest:AttackBonus"),
            ConquestBonus.Defense => "Fortress (+2 defense)".L10N("Client:Conquest:DefenseBonus"),
            _ => "No bonus".L10N("Client:Conquest:NoBonus")
        };

        public static bool Important(ConquestEvent action) => action.EventType is
            ConquestEventType.TerritoryAttacked or ConquestEventType.TerritoryCaptured or
            ConquestEventType.TerritoryLost or ConquestEventType.ArmyDestroyed or
            ConquestEventType.AutoCombatResult or ConquestEventType.SkirmishResult or
            ConquestEventType.CampaignVictory or ConquestEventType.CampaignDefeat or ConquestEventType.PermanentPerkGained;

        public static string Describe(ConquestEvent action) => action.EventType switch
        {
            ConquestEventType.PerkPurchased => string.Format("Perk purchased: {0}, level {1}".L10N("Client:Conquest:History:PerkPurchased"),
                PerkName(action.PerkId, action.PerkKind ?? ConquestPerkKind.Income, action.PerkName), action.Amount),
            ConquestEventType.TerritoryImproved => string.Format("Node defenses built: {0}, level {1}".L10N("Client:Conquest:History:TerritoryImproved"),
                PerkName(action.PerkId, action.PerkKind ?? ConquestPerkKind.StartingDefenses, action.PerkName), action.Amount),
            ConquestEventType.PermanentPerkGained => string.Format("Permanent perk: {0}, level {1}".L10N("Client:Conquest:History:PermanentPerk"),
                PerkName(action.PerkId, action.PerkKind ?? ConquestPerkKind.Income, action.PerkName), action.Amount),
            ConquestEventType.TurnStarted => "Turn started".L10N("Client:Conquest:History:TurnStarted"),
            ConquestEventType.TurnEnded => "Turn ended".L10N("Client:Conquest:History:TurnEnded"),
            ConquestEventType.ArmyMoved => "Army moved".L10N("Client:Conquest:History:ArmyMoved"),
            ConquestEventType.ArmyRecruited => "Troops recruited".L10N("Client:Conquest:History:TroopsRecruited"),
            ConquestEventType.TerritoryAttacked => "Territory attacked".L10N("Client:Conquest:History:TerritoryAttacked"),
            ConquestEventType.TerritoryCaptured => "Territory captured".L10N("Client:Conquest:History:TerritoryCaptured"),
            ConquestEventType.TerritoryLost => "Territory lost".L10N("Client:Conquest:History:TerritoryLost"),
            ConquestEventType.ArmyDestroyed => "Army destroyed".L10N("Client:Conquest:History:ArmyDestroyed"),
            ConquestEventType.CreditsGenerated => string.Format("+{0} credits generated".L10N("Client:Conquest:History:CreditsGenerated"), action.Amount),
            ConquestEventType.CreditsSpent => string.Format("{0} credits spent".L10N("Client:Conquest:History:CreditsSpent"), action.Amount),
            ConquestEventType.TerritoryBonusGained => string.Format("Bonus gained: {0}".L10N("Client:Conquest:History:BonusGained"), EventBonus(action)),
            ConquestEventType.TerritoryBonusLost => string.Format("Bonus lost: {0}".L10N("Client:Conquest:History:BonusLost"), EventBonus(action)),
            ConquestEventType.UpgradePurchased => string.Format("Permanent upgrade: {0}, level {1}".L10N("Client:Conquest:History:StatUpgradePurchased"), action.Stat.HasValue ? Stat(action.Stat.Value) : Stat(ConquestStat.Damage), action.Amount),
            ConquestEventType.TemporaryBonusPurchased => "Temporary supply bonus purchased".L10N("Client:Conquest:History:SupplyPurchased"),
            ConquestEventType.TemporaryBonusConsumed => "Temporary supply bonus consumed".L10N("Client:Conquest:History:SupplyConsumed"),
            ConquestEventType.AutoCombatResult => action.Success
                ? "Auto Combat: victory".L10N("Client:Conquest:History:AutoCombatVictory")
                : "Auto Combat: defeat".L10N("Client:Conquest:History:AutoCombatDefeat"),
            ConquestEventType.SkirmishResult => action.Success
                ? "Skirmish battle: victory".L10N("Client:Conquest:History:SkirmishVictory")
                : "Skirmish battle: defeat".L10N("Client:Conquest:History:SkirmishDefeat"),
            ConquestEventType.CampaignVictory => "Campaign victory".L10N("Client:Conquest:History:CampaignVictory"),
            ConquestEventType.CampaignDefeat => "Campaign defeat".L10N("Client:Conquest:History:CampaignDefeat"),
            _ => string.Empty
        };
    }
}