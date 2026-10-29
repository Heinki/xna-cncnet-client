#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace DTAClient.Domain.Conquest
{
    public enum ConquestEventType
    {
        TurnStarted, TurnEnded, ArmyMoved, ArmyRecruited, TerritoryAttacked,
        TerritoryCaptured, TerritoryLost, ArmyDestroyed, CreditsGenerated, CreditsSpent,
        TerritoryBonusGained, TerritoryBonusLost, UpgradePurchased, TemporaryBonusPurchased,
        TemporaryBonusConsumed, AutoCombatResult, SkirmishResult, CampaignVictory, CampaignDefeat, PermanentPerkGained,
        PerkPurchased, TerritoryImproved
    }

    /// <summary>
    /// A gameplay notification. Numeric payloads keep descriptions localizable at display time.
    /// </summary>
    public sealed class ConquestEvent : EventArgs
    {
        public long Id { get; set; }
        public int TurnNumber { get; set; }
        public ConquestFaction FactionId { get; set; }
        public ConquestEventType EventType { get; set; }
        public int? SourceTerritoryId { get; set; }
        public int? TargetTerritoryId { get; set; }
        public int Amount { get; set; }
        public ConquestBonus Bonus { get; set; }
        public ConquestStat? Stat { get; set; }
        public string PerkId { get; set; } = string.Empty;
        public ConquestPerkKind? PerkKind { get; set; }
        public string PerkName { get; set; } = string.Empty;
        public bool Success { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Turn-based campaign rules. Every AI step commits its continuation before returning,
    /// so an interrupted attack cannot replay an action on resume.
    /// </summary>
    public sealed class ConquestCampaign
    {
        public const int RecruitmentCost = 100;
        public const int UpgradeCost = 400;
        public const int TemporaryBonusCost = 150;
        public const int MaximumStrength = 1000;

        public ConquestCampaign(ConquestState state)
        {
            State = state;
            ConquestMapLayout.Ensure(state);
        }

        public ConquestState State { get; }
        public event EventHandler<ConquestEvent>? ActionOccurred;

        public static ConquestCampaign Create(ConquestSettings settings)
        {
            if (settings.MapSize < 3 || settings.MapSize > 10 || settings.StartingCredits < 100 ||
                settings.StartingCredits > 10000 || settings.Difficulty < 0 || settings.Difficulty > 2 || settings.BuffDensity < 0 || settings.BuffDensity > 2)
            {
                throw new ArgumentOutOfRangeException(nameof(settings));
            }

            var state = new ConquestState
            {
                Settings = settings,
                RandomState = settings.Seed == 0 ? 0x9E3779B9 : unchecked((uint)settings.Seed),
                Player = new ConquestEconomy { Credits = settings.StartingCredits },
                AI = new ConquestEconomy { Credits = settings.StartingCredits }
            };
            var campaign = new ConquestCampaign(state);

            for (int y = 0; y < settings.MapSize; y++)
            {
                for (int x = 0; x < settings.MapSize; x++)
                {
                    int id = y * settings.MapSize + x;
                    state.Territories.Add(new ConquestTerritory
                    {
                        Id = id,
                        X = x,
                        Y = y,
                        Income = 100
                    });
                    var territory = state.Territories[id];
                    int buffCount = campaign.NextRandom(3) + settings.BuffDensity - 1;
                    buffCount = Math.Max(0, buffCount);
                    foreach (ConquestStat stat in settings.SupportedStats
                        .OrderBy(_ => campaign.NextRandom(10000)).Take(buffCount))
                        territory.Buffs[stat] = 5 + campaign.NextRandom(2) * 5;

                    // Income-rich nodes and research-rich nodes offer different priorities.
                    territory.Income = Math.Max(50, 250 - territory.Buffs.Count * 60 + campaign.NextRandom(5) * 25);
                    if (settings.BattleMaps.Count > 0)
                        territory.BattleMap = settings.BattleMaps[campaign.NextRandom(settings.BattleMaps.Count)];
                    if (settings.Perks.Count > 0 && campaign.NextRandom(100) < 15 + settings.BuffDensity * 10)
                    {
                        var perk = settings.Perks[campaign.NextRandom(settings.Perks.Count)];
                        if (perk.TerritoryOnly)
                            territory.Perks[perk.Id] = 1;
                        else
                            territory.RewardId = perk.Id;
                    }
                }
            }

            ConquestMapLayout.Ensure(state);
            state.Territories[0].Owner = ConquestFaction.Player;
            state.Territories[state.Territories.Count - 1].Owner = ConquestFaction.AI;
            state.Armies.Add(new ConquestArmy
            {
                Id = state.NextId++,
                Faction = ConquestFaction.Player,
                TerritoryId = 0,
                Strength = 8
            });
            state.Armies.Add(new ConquestArmy
            {
                Id = state.NextId++,
                Faction = ConquestFaction.AI,
                TerritoryId = state.Territories.Count - 1,
                Strength = 8
            });

            return campaign;
        }

        public void Start()
        {
            if (State.Started)
                return;

            State.Started = true;
            Emit(ConquestFaction.Player, ConquestEventType.TurnStarted);
            Emit(ConquestFaction.Player, ConquestEventType.CreditsGenerated, amount: State.Player.Credits);
            Emit(ConquestFaction.AI, ConquestEventType.CreditsGenerated, amount: State.AI.Credits);
            AnnounceBonus(State.Territories[0], ConquestEventType.TerritoryBonusGained);
            AnnounceBonus(State.Territories[State.Territories.Count - 1], ConquestEventType.TerritoryBonusGained);
            ClaimReward(State.Territories[0]);
            ClaimReward(State.Territories[State.Territories.Count - 1]);
        }

        public ConquestEconomy Economy(ConquestFaction faction) =>
            faction == ConquestFaction.Player ? State.Player : State.AI;

        public ConquestArmy? ArmyAt(int territoryId)
        {
            foreach (var army in State.Armies)
            {
                if (army.TerritoryId == territoryId)
                    return army;
            }

            return null;
        }

        public IEnumerable<ConquestTerritory> Neighbors(int territoryId)
        {
            return State.Connections.Where(e => e.Source == territoryId || e.Target == territoryId)
                .Select(e => State.Territories[e.Source == territoryId ? e.Target : e.Source]);
        }

        private bool CanAct(ConquestFaction faction) =>
            (faction == ConquestFaction.Player && State.Phase == ConquestPhase.PlayerTurn) ||
            (faction == ConquestFaction.AI && State.Phase == ConquestPhase.AITurn);

        public bool Recruit(ConquestFaction faction, int territoryId)
        {
            if (!CanAct(faction) || territoryId < 0 || territoryId >= State.Territories.Count ||
                State.Territories[territoryId].Owner != faction || Economy(faction).Credits < RecruitmentCost)
            {
                return false;
            }

            var army = ArmyAt(territoryId);
            if (army != null && (army.HasActed || army.Strength > MaximumStrength - RecruitmentStrength(faction)))
                return false;

            Spend(faction, RecruitmentCost, territoryId);

            if (army == null)
            {
                army = new ConquestArmy { Id = State.NextId++, Faction = faction, TerritoryId = territoryId };
                State.Armies.Add(army);
            }

            int strength = RecruitmentStrength(faction);
            army.Strength += strength;
            // Recruitment uses this army's action, preventing infinite reinforcement in one turn.
            army.HasActed = true;
            Emit(faction, ConquestEventType.ArmyRecruited, target: territoryId, amount: strength);

            return true;
        }

        public bool BuyUpgrade(ConquestFaction faction, ConquestStat stat = ConquestStat.Health)
        {
            var economy = Economy(faction);
            if (!CanAct(faction) || economy.Credits < UpgradeCost || !State.Settings.SupportedStats.Contains(stat) || economy.Upgrades.GetValueOrDefault(stat) >= 3)
                return false;

            Spend(faction, UpgradeCost);
            economy.Upgrades[stat] = economy.Upgrades.GetValueOrDefault(stat) + 1;
            economy.UpgradeLevel = economy.Upgrades.Values.Sum();
            Emit(faction, ConquestEventType.UpgradePurchased, amount: economy.Upgrades[stat], stat: stat);

            return true;
        }

        public bool BuyTemporaryBonus(ConquestFaction faction)
        {
            var economy = Economy(faction);
            if (!CanAct(faction) || economy.Credits < TemporaryBonusCost || economy.TemporaryBonuses >= 3 || !State.Settings.SupportedStats.Contains(ConquestStat.Damage))
                return false;

            Spend(faction, TemporaryBonusCost);
            economy.TemporaryBonuses++;
            Emit(faction, ConquestEventType.TemporaryBonusPurchased);

            return true;
        }

        public bool CanBuyPerk(ConquestFaction faction, string id, int territoryId)
        {
            var perk = State.Settings.Perks.Find(p => p.Id == id);
            if (perk == null || !CanAct(faction) || perk.PurchaseCost == 0 || Economy(faction).Credits < perk.PurchaseCost)
                return false;

            if (id == "veteran-escorts" && !State.Settings.Perks.Any(p => p.Kind == ConquestPerkKind.StartingTroops &&
                p.StartingObjects.Count > 0 && Economy(faction).Perks.GetValueOrDefault(p.Id) > 0))
                return false;

            if (perk.TerritoryOnly && (territoryId < 0 || territoryId >= State.Territories.Count || State.Territories[territoryId].Owner != faction))
                return false;

            var inventory = perk.TerritoryOnly ? State.Territories[territoryId].Perks : Economy(faction).Perks;

            return inventory.GetValueOrDefault(id) < perk.MaximumLevel;
        }

        public bool BuyPerk(ConquestFaction faction, string id, int territoryId)
        {
            if (!CanBuyPerk(faction, id, territoryId))
                return false;

            var perk = State.Settings.Perks.Single(p => p.Id == id);
            var inventory = perk.TerritoryOnly ? State.Territories[territoryId].Perks : Economy(faction).Perks;
            Spend(faction, perk.PurchaseCost, perk.TerritoryOnly ? territoryId : null);
            inventory[id] = inventory.GetValueOrDefault(id) + 1;
            Emit(faction, perk.TerritoryOnly ? ConquestEventType.TerritoryImproved : ConquestEventType.PerkPurchased,
                target: perk.TerritoryOnly ? territoryId : null, amount: inventory[id], perk: perk);

            return true;
        }

        public bool CanMove(ConquestFaction faction, int sourceId, int targetId)
        {
            if (!CanAct(faction) || sourceId < 0 || targetId < 0 ||
                sourceId >= State.Territories.Count || targetId >= State.Territories.Count)
            {
                return false;
            }

            var army = ArmyAt(sourceId);
            if (army == null || army.Faction != faction || army.HasActed)
                return false;

            bool adjacent = false;
            foreach (var edge in State.Connections)
            {
                if (edge.Joins(sourceId, targetId))
                {
                    adjacent = true;
                    break;
                }
            }
            if (!adjacent)
                return false;

            var target = State.Territories[targetId];
            var friendly = ArmyAt(targetId);
            if (target.Owner == faction && friendly != null && friendly.Strength > MaximumStrength - army.Strength)
                return false;

            return true;
        }

        public bool Move(ConquestFaction faction, int sourceId, int targetId)
        {
            if (!CanMove(faction, sourceId, targetId))
                return false;

            var army = ArmyAt(sourceId)!;
            var target = State.Territories[targetId];
            var friendly = ArmyAt(targetId);
            army.HasActed = true;

            if (target.Owner == faction)
            {
                army.TerritoryId = targetId;

                if (friendly != null)
                {
                    friendly.Strength += army.Strength;
                    friendly.HasActed = true;
                    State.Armies.Remove(army);
                }

                Emit(faction, ConquestEventType.ArmyMoved, sourceId, targetId);
            }
            else
            {
                var defender = ArmyAt(targetId);
                var attackerStats = Stats(faction);
                var defenderStats = Stats(target.Owner);
                ConsumeBonus(faction, targetId, attackerStats);
                ConsumeBonus(target.Owner, targetId, defenderStats);
                var attackerPerks = BattlePerks(faction, false, target);
                var defenderPerks = BattlePerks(target.Owner, true, target);
                State.PendingBattle = new ConquestBattle
                {
                    Id = State.NextId++,
                    ArmyId = army.Id,
                    Attacker = faction,
                    SourceTerritoryId = sourceId,
                    TargetTerritoryId = targetId,
                    AttackPower = EffectivePower(army.Strength + PerkCombatStrength(attackerPerks), attackerStats),
                    DefensePower = EffectivePower((defender?.Strength ?? 2) + PerkCombatStrength(defenderPerks), defenderStats),
                    PlayerStats = faction == ConquestFaction.Player ? attackerStats : defenderStats,
                    AIStats = faction == ConquestFaction.AI ? attackerStats : defenderStats,
                    PlayerPerks = faction == ConquestFaction.Player ? attackerPerks : defenderPerks,
                    AIPerks = faction == ConquestFaction.AI ? attackerPerks : defenderPerks
                };
                State.Phase = ConquestPhase.AwaitingBattle;
                Emit(faction, ConquestEventType.TerritoryAttacked, sourceId, targetId);
            }

            return true;
        }

        public void EndPlayerTurn()
        {
            if (State.Phase != ConquestPhase.PlayerTurn)
                return;

            Emit(ConquestFaction.Player, ConquestEventType.TurnEnded);
            State.AIRecruited = false;
            State.AIPurchased = false;
            BeginTurn(ConquestFaction.AI);
        }

        /// <summary>
        /// Performs one gameplay action. Animation speed controls only calls to this method.
        /// AwaitingBattle always blocks progress, including at instant speed.
        /// </summary>
        public bool StepAI()
        {
            if (State.Phase != ConquestPhase.AITurn)
                return false;

            if (!State.AIPurchased)
            {
                State.AIPurchased = true;

                var improvements = State.Settings.Perks.Where(p => p.PurchaseCost > 0).ToList();
                if (improvements.Count > 0)
                {
                    var perk = improvements[State.TurnNumber % improvements.Count];
                    var node = State.Territories.Where(t => t.Owner == ConquestFaction.AI)
                        .OrderBy(DistanceToEnemy).FirstOrDefault(t => CanBuyPerk(ConquestFaction.AI, perk.Id, t.Id));
                    if (node != null && State.AI.Credits >= perk.PurchaseCost + RecruitmentCost && BuyPerk(ConquestFaction.AI, perk.Id, node.Id))
                        return true;
                }

                if (State.AI.Credits >= UpgradeCost + RecruitmentCost && BuyUpgrade(ConquestFaction.AI, State.Settings.SupportedStats.Count == 0 ? ConquestStat.Health : State.Settings.SupportedStats[State.TurnNumber % State.Settings.SupportedStats.Count]))
                    return true;

                if (State.AI.Credits >= TemporaryBonusCost + RecruitmentCost && BuyTemporaryBonus(ConquestFaction.AI))
                    return true;
            }

            var army = State.Armies.FirstOrDefault(a => a.Faction == ConquestFaction.AI && !a.HasActed);
            if (army != null)
            {
                var target = Neighbors(army.TerritoryId)
                    .OrderBy(t => t.Owner == ConquestFaction.AI ? 1 : 0)
                    .ThenBy(t => t.Owner == ConquestFaction.AI ? DistanceToEnemy(t) : (ArmyAt(t.Id)?.Strength ?? 2))
                    .ThenBy(t => t.Id)
                    .First();
                bool moved = Move(ConquestFaction.AI, army.TerritoryId, target.Id);
                if (!moved)
                    army.HasActed = true;

                return true;
            }

            if (!State.AIRecruited)
            {
                State.AIRecruited = true;
                var location = State.Territories.Where(t => t.Owner == ConquestFaction.AI)
                    .OrderBy(t => ArmyAt(t.Id) == null ? 0 : 1)
                    .ThenBy(DistanceToEnemy).FirstOrDefault();
                if (location != null && Recruit(ConquestFaction.AI, location.Id))
                    return true;
            }

            Emit(ConquestFaction.AI, ConquestEventType.TurnEnded);
            State.TurnNumber++;
            BeginTurn(ConquestFaction.Player);

            return true;
        }

        private int DistanceToEnemy(ConquestTerritory source)
        {
            var visited = new HashSet<int> { source.Id };
            var queue = new Queue<(int Id, int Distance)>();
            queue.Enqueue((source.Id, 0));
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (State.Territories[current.Id].Owner != ConquestFaction.AI)
                    return current.Distance;

                foreach (var neighbor in Neighbors(current.Id))
                {
                    if (visited.Add(neighbor.Id))
                        queue.Enqueue((neighbor.Id, current.Distance + 1));
                }
            }

            return 0;
        }

        public bool ResolveAutoCombat(long battleId)
        {
            var battle = State.PendingBattle;
            if (battle == null || battle.Id != battleId || battle.SkirmishLaunched)
                return false;

            bool attackerWon = NextRandom(battle.AttackPower + battle.DefensePower) < battle.AttackPower;

            return ResolveBattle(battleId, attackerWon, false);
        }

        public bool ResolveBattle(long battleId, bool attackerWon, bool skirmish)
        {
            var battle = State.PendingBattle;
            if (State.Phase != ConquestPhase.AwaitingBattle || battle == null || battle.Id != battleId)
                return false;

            // Clear before emitting notifications, making repeated result callbacks harmless.
            State.PendingBattle = null;
            var army = State.Armies.Single(a => a.Id == battle.ArmyId);
            var target = State.Territories[battle.TargetTerritoryId];
            var defender = ArmyAt(target.Id);
            Emit(battle.Attacker, skirmish ? ConquestEventType.SkirmishResult : ConquestEventType.AutoCombatResult,
                battle.SourceTerritoryId, target.Id, success: attackerWon);

            if (target.Owner != ConquestFaction.Neutral)
            {
                Emit(target.Owner, skirmish ? ConquestEventType.SkirmishResult : ConquestEventType.AutoCombatResult,
                    battle.SourceTerritoryId, target.Id, success: !attackerWon);
            }

            if (attackerWon)
            {
                if (defender != null)
                    DestroyArmy(defender);

                if (target.Owner != ConquestFaction.Neutral)
                {
                    Emit(target.Owner, ConquestEventType.TerritoryLost, target: target.Id);
                    AnnounceBonus(target, ConquestEventType.TerritoryBonusLost);
                }

                target.Owner = battle.Attacker;
                army.TerritoryId = target.Id;
                army.Strength = Math.Max(1, army.Strength - Math.Max(1, battle.DefensePower / 3));
                Emit(battle.Attacker, ConquestEventType.ArmyMoved, battle.SourceTerritoryId, target.Id);
                Emit(battle.Attacker, ConquestEventType.TerritoryCaptured, target: target.Id);
                AnnounceBonus(target, ConquestEventType.TerritoryBonusGained);
                ClaimReward(target);
            }
            else
            {
                DestroyArmy(army);

                if (defender != null)
                    defender.Strength = Math.Max(1, defender.Strength - Math.Max(1, battle.AttackPower / 3));
            }

            State.Phase = battle.Attacker == ConquestFaction.Player ? ConquestPhase.PlayerTurn : ConquestPhase.AITurn;
            CheckVictory();

            return true;
        }

        private void BeginTurn(ConquestFaction faction)
        {
            State.Phase = faction == ConquestFaction.Player ? ConquestPhase.PlayerTurn : ConquestPhase.AITurn;

            foreach (var army in State.Armies.Where(a => a.Faction == faction))
                army.HasActed = false;

            Emit(faction, ConquestEventType.TurnStarted);
            int income = IncomePerTurn(faction);

            var economy = Economy(faction);
            income = Math.Min(income, 100000000 - economy.Credits);
            economy.Credits += income;
            Emit(faction, ConquestEventType.CreditsGenerated, amount: income);
        }

        public int RecruitmentStrength(ConquestFaction faction) => Math.Min(20, 5 + PerkAmount(faction, ConquestPerkKind.Recruitment));

        public int IncomePerTurn(ConquestFaction faction)
        {
            int income = State.Territories.Where(t => t.Owner == faction).Sum(t => t.Income) + PerkAmount(faction, ConquestPerkKind.Income);

            return faction == ConquestFaction.AI ? income * (75 + State.Settings.Difficulty * 25) / 100 : income;
        }

        private int PerkAmount(ConquestFaction faction, ConquestPerkKind kind) => faction == ConquestFaction.Neutral ? 0 :
            State.Settings.Perks.Where(p => p.Kind == kind).Sum(p => p.Amount * Economy(faction).Perks.GetValueOrDefault(p.Id));

        private Dictionary<string, int> BattlePerks(ConquestFaction faction, bool defending, ConquestTerritory target)
        {
            var result = new Dictionary<string, int>();
            foreach (var perk in State.Settings.Perks)
            {
                if (perk.Kind == ConquestPerkKind.StartingDefenses && !defending)
                    continue;

                int level = perk.TerritoryOnly ? (defending ? target.Perks.GetValueOrDefault(perk.Id) : 0) :
                    faction == ConquestFaction.Neutral ? 0 : Economy(faction).Perks.GetValueOrDefault(perk.Id);
                if (level > 0)
                    result[perk.Id] = level;
            }

            return result;
        }

        private int PerkCombatStrength(Dictionary<string, int> perks) =>
            State.Settings.Perks.Sum(p => p.AutoCombatStrength * perks.GetValueOrDefault(p.Id));

        public ConquestBonusSummary BonusSummary(ConquestFaction faction)
        {
            var summary = new ConquestBonusSummary
            {
                Income = IncomePerTurn(faction),
                RecruitmentStrength = RecruitmentStrength(faction),
                Supplies = Economy(faction).TemporaryBonuses,
                ArmyStrength = State.Armies.Where(a => a.Faction == faction).Sum(a => a.Strength)
            };
            foreach (ConquestStat stat in State.Settings.SupportedStats)
            {
                summary.Research[stat] = Economy(faction).Upgrades.GetValueOrDefault(stat) * 10;
                summary.Territories[stat] = State.Territories.Where(t => t.Owner == faction).Sum(t => t.Buffs.GetValueOrDefault(stat));
                summary.Total[stat] = Math.Min(100, summary.Research[stat] + summary.Territories[stat]);
            }
            summary.StatIndex = EffectivePower(100, summary.Total);

            return summary;
        }

        private void ClaimReward(ConquestTerritory territory)
        {
            var perk = State.Settings.Perks.Find(p => p.Id == territory.RewardId);
            if (perk == null || territory.Owner == ConquestFaction.Neutral || territory.RewardClaimedBy.Contains(territory.Owner))
                return;

            territory.RewardClaimedBy.Add(territory.Owner);
            var owned = Economy(territory.Owner).Perks;
            int level = owned.GetValueOrDefault(perk.Id);
            if (level >= perk.MaximumLevel)
                return;

            owned[perk.Id] = level + 1;
            Emit(territory.Owner, ConquestEventType.PermanentPerkGained, target: territory.Id, amount: level + 1, perk: perk);
        }

        public Dictionary<ConquestStat, int> Stats(ConquestFaction faction)
        {
            var result = new Dictionary<ConquestStat, int>();
            if (faction == ConquestFaction.Neutral)
                return result;

            foreach (ConquestStat stat in State.Settings.SupportedStats)
            {
                int value = Economy(faction).Upgrades.GetValueOrDefault(stat) * 10 +
                    State.Territories.Where(t => t.Owner == faction).Sum(t => t.Buffs.GetValueOrDefault(stat));
                result[stat] = Math.Min(100, value);
            }

            return result;
        }

        public static int EffectivePower(int strength, Dictionary<ConquestStat, int> stats)
        {
            double value = strength;
            foreach (ConquestStat stat in Enum.GetValues(typeof(ConquestStat)))
                value *= Math.Pow(1 + stats.GetValueOrDefault(stat) / 100.0, stat == ConquestStat.Speed ? 0.25 : 0.5);

            return Math.Max(1, (int)Math.Round(value));
        }

        public static double PlayerWinChance(ConquestBattle battle)
        {
            double attackerChance = battle.AttackPower / (double)(battle.AttackPower + battle.DefensePower);

            return battle.Attacker == ConquestFaction.Player ? attackerChance : 1 - attackerChance;
        }

        private void ConsumeBonus(ConquestFaction faction, int targetId, Dictionary<ConquestStat, int> stats)
        {
            if (faction == ConquestFaction.Neutral || Economy(faction).TemporaryBonuses == 0 ||
                !State.Settings.SupportedStats.Contains(ConquestStat.Damage))
                return;

            Economy(faction).TemporaryBonuses--;
            stats[ConquestStat.Damage] = stats.GetValueOrDefault(ConquestStat.Damage) + 20;
            Emit(faction, ConquestEventType.TemporaryBonusConsumed, target: targetId);
        }

        private void CheckVictory()
        {
            foreach (var faction in new[] { ConquestFaction.Player, ConquestFaction.AI })
            {
                if (State.Territories.Any(t => t.Owner == faction))
                    continue;

                var winner = faction == ConquestFaction.Player ? ConquestFaction.AI : ConquestFaction.Player;
                Emit(State.Phase == ConquestPhase.PlayerTurn ? ConquestFaction.Player : ConquestFaction.AI,
                    ConquestEventType.TurnEnded);
                State.Winner = winner;
                State.Phase = ConquestPhase.Finished;
                Emit(winner, ConquestEventType.CampaignVictory);
                Emit(faction, ConquestEventType.CampaignDefeat);
                break;
            }
        }

        private void DestroyArmy(ConquestArmy army)
        {
            State.Armies.Remove(army);
            Emit(army.Faction, ConquestEventType.ArmyDestroyed, target: army.TerritoryId);
        }

        private void Spend(ConquestFaction faction, int amount, int? territoryId = null)
        {
            Economy(faction).Credits -= amount;
            Emit(faction, ConquestEventType.CreditsSpent, target: territoryId, amount: amount);
        }

        private void AnnounceBonus(ConquestTerritory territory, ConquestEventType type)
        {
            Emit(territory.Owner, type, target: territory.Id, amount: territory.Income, bonus: ConquestBonus.Income);
            foreach (var buff in territory.Buffs.Where(b => State.Settings.SupportedStats.Contains(b.Key)))
                Emit(territory.Owner, type, target: territory.Id, amount: buff.Value, stat: buff.Key);
        }

        private int NextRandom(int maximum)
        {
            uint value = State.RandomState;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            State.RandomState = value;

            return (int)(value % (uint)maximum);
        }

        private void Emit(ConquestFaction faction, ConquestEventType type, int? source = null,
            int? target = null, int amount = 0, ConquestBonus bonus = ConquestBonus.None, bool success = false, ConquestStat? stat = null, ConquestPerkDefinition? perk = null)
        {
            var action = new ConquestEvent
            {
                Id = State.NextId++,
                TurnNumber = State.TurnNumber,
                FactionId = faction,
                EventType = type,
                SourceTerritoryId = source,
                TargetTerritoryId = target,
                Amount = amount,
                Bonus = bonus,
                Stat = stat,
                PerkId = perk?.Id ?? string.Empty,
                PerkKind = perk?.Kind,
                PerkName = perk?.Name ?? string.Empty,
                Success = success,
                Timestamp = DateTime.UtcNow
            };
            State.History.Append(action);
            ActionOccurred?.Invoke(this, action);
        }
    }
}