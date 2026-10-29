#nullable enable

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DTAClient.Domain.Conquest
{
    /// <summary>
    /// Atomic snapshots plus an append-only UTF-8 event journal. Snapshot byte length commits
    /// the journal prefix, so a crash cannot replay an uncommitted action or duplicate its events.
    /// </summary>
    public sealed class ConquestSaveStore
    {
        public ConquestSaveStore(string path)
        {
            Path = path;
        }

        public string Path { get; }
        public bool Exists => File.Exists(Path);

        public void Save(ConquestState state)
        {
            Validate(state);
            string? directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            long previousLength = state.HistoryBytes;
            int previousCount = state.SavedEventCount;
            try
            {
                AppendHistory(state);
                string temporaryPath = Path + ".tmp";
                using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, state);
                    stream.Flush(true);
                }

                if (File.Exists(Path))
                    File.Replace(temporaryPath, Path, Path + ".bak");
                else
                    File.Move(temporaryPath, Path);
            }
            catch
            {
                state.HistoryBytes = previousLength;
                state.SavedEventCount = previousCount;
                throw;
            }
        }

        public ConquestState Load()
        {
            using var stream = File.OpenRead(Path);
            var state = JsonSerializer.Deserialize<ConquestState>(stream)
                ?? throw new InvalidDataException("Empty Conquest save.");
            Validate(state);
            LoadHistory(state);

            return state;
        }

        private string HistoryPath(ConquestState state) =>
            System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path) ?? string.Empty, state.CampaignId.ToString("N") + ".history");

        private void AppendHistory(ConquestState state)
        {
            using var stream = new FileStream(HistoryPath(state), FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            if (stream.Length < state.HistoryBytes || state.SavedEventCount > state.History.Count)
                throw new InvalidDataException("Conquest history is incomplete.");

            // Remove any tail left by an interrupted snapshot write, retaining the committed prefix.
            stream.SetLength(state.HistoryBytes);
            stream.Position = state.HistoryBytes;
            var length = new byte[4];
            var events = state.History.GetEvents(ConquestHistoryFilter.All);
            for (int i = state.SavedEventCount; i < events.Count; i++)
            {
                byte[] payload = JsonSerializer.SerializeToUtf8Bytes(events[i]);
                BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
                stream.Write(length, 0, length.Length);
                stream.Write(payload, 0, payload.Length);
            }

            stream.Flush(true);
            state.HistoryBytes = stream.Position;
            state.SavedEventCount = events.Count;
        }

        private void LoadHistory(ConquestState state)
        {
            if (state.HistoryBytes == 0 && state.SavedEventCount == 0)
                return;

            using var stream = File.OpenRead(HistoryPath(state));
            if (stream.Length < state.HistoryBytes)
                throw new InvalidDataException("Conquest history is truncated.");

            var length = new byte[4];
            while (stream.Position < state.HistoryBytes)
            {
                if (state.HistoryBytes - stream.Position < length.Length)
                    throw new InvalidDataException("Invalid Conquest event length.");

                ReadBytes(stream, length);
                int count = BinaryPrimitives.ReadInt32LittleEndian(length);
                if (count < 1 || count > 4096 || count > state.HistoryBytes - stream.Position)
                    throw new InvalidDataException("Invalid Conquest event length.");

                var payload = new byte[count];
                ReadBytes(stream, payload);
                var action = JsonSerializer.Deserialize<ConquestEvent>(payload)
                    ?? throw new InvalidDataException("Empty Conquest event.");
                if (action.Id <= state.History.LastId || action.Id >= state.NextId || action.TurnNumber < 1 ||
                    action.TurnNumber > state.TurnNumber || (action.FactionId != ConquestFaction.Player && action.FactionId != ConquestFaction.AI) ||
                    !Enum.IsDefined(typeof(ConquestEventType), action.EventType) ||
                    (action.Stat.HasValue && !Enum.IsDefined(typeof(ConquestStat), action.Stat.Value)) ||
                    (action.SourceTerritoryId.HasValue && (action.SourceTerritoryId < 0 || action.SourceTerritoryId >= state.Territories.Count)) ||
                    (action.TargetTerritoryId.HasValue && (action.TargetTerritoryId < 0 || action.TargetTerritoryId >= state.Territories.Count)))
                {
                    throw new InvalidDataException("Invalid Conquest history event.");
                }

                state.History.Append(action);
            }

            if (state.History.Count != state.SavedEventCount)
                throw new InvalidDataException("Conquest history count does not match the snapshot.");
        }

        private static void ReadBytes(Stream stream, byte[] buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read == 0)
                    throw new EndOfStreamException("Truncated Conquest event.");

                offset += read;
            }
        }

        private static bool ValidStats(Dictionary<ConquestStat, int>? stats, int maximum) =>
            stats != null && stats.All(p => Enum.IsDefined(typeof(ConquestStat), p.Key) && p.Value >= 0 && p.Value <= maximum);

        private static void Validate(ConquestState state)
        {
            if ((state.Version < 1 || state.Version > 4) || state.CampaignId == Guid.Empty || state.HistoryBytes < 0 || state.SavedEventCount < 0 ||
                state.Settings == null || state.Player == null || state.AI == null ||
                state.Territories == null || state.Connections == null || state.Armies == null || state.TurnNumber < 1 || state.NextId < 1 ||
                state.RandomState == 0 || !Enum.IsDefined(typeof(ConquestPhase), state.Phase) ||
                (state.AnimationSpeed != 0 && state.AnimationSpeed != 1 && state.AnimationSpeed != 2 && state.AnimationSpeed != 4))
            {
                throw new InvalidDataException("Unsupported or invalid Conquest save.");
            }

            int size = state.Settings.MapSize;
            if (size < 3 || size > 10 || state.Territories.Count != size * size ||
                state.Settings.Difficulty < 0 || state.Settings.Difficulty > 2 || state.Settings.BuffDensity < 0 || state.Settings.BuffDensity > 2 ||
                state.Settings.BattleMaps == null || state.Settings.SupportedStats == null ||
                state.Settings.SupportedStats.Any(s => !Enum.IsDefined(typeof(ConquestStat), s)) ||
                state.Settings.SupportedStats.Distinct().Count() != state.Settings.SupportedStats.Count ||
                state.Settings.BattleMapHash == null || state.Settings.BattleGameMode == null ||
                state.Settings.DropDownOptions == null || state.Settings.CheckBoxOptions == null)
            {
                throw new InvalidDataException("Invalid Conquest map settings.");
            }

            for (int i = 0; i < state.Territories.Count; i++)
            {
                var territory = state.Territories[i];
                if (territory == null || territory.Id != i || territory.X != i % size || territory.Y != i / size ||
                    territory.Buffs == null || !Enum.IsDefined(typeof(ConquestFaction), territory.Owner) || !Enum.IsDefined(typeof(ConquestBonus), territory.Bonus))
                {
                    throw new InvalidDataException("Invalid Conquest territory.");
                }
            }

            ConquestMapLayout.Ensure(state);
            float extent = ConquestMapLayout.Extent(state);
            if (state.MapLayoutVersion != 1 || state.Territories.Any(t => float.IsNaN(t.MapX) || float.IsInfinity(t.MapX) ||
                float.IsNaN(t.MapY) || float.IsInfinity(t.MapY) || t.MapX < 0 || t.MapY < 0 || t.MapX > extent || t.MapY > extent) ||
                state.Connections.Count < size * size - 1 || state.Connections.Count > size * size * 6 ||
                state.Connections.Any(e => e == null || e.Source < 0 || e.Target >= size * size || e.Source >= e.Target) ||
                state.Connections.Select(e => (e.Source, e.Target)).Distinct().Count() != state.Connections.Count)
            {
                throw new InvalidDataException("Invalid Conquest map network.");
            }

            var visited = new HashSet<int> { 0 };
            var queue = new Queue<int>();
            queue.Enqueue(0);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (var edge in state.Connections.Where(e => e.Source == current || e.Target == current))
                {
                    int neighbor = edge.Source == current ? edge.Target : edge.Source;
                    if (visited.Add(neighbor))
                        queue.Enqueue(neighbor);
                }
            }

            if (visited.Count != state.Territories.Count)
                throw new InvalidDataException("Disconnected Conquest map network.");

            if (state.Player.Upgrades == null || state.AI.Upgrades == null || state.Settings.BattleMaps.Any(m => m == null || m.Hash == null || m.GameMode == null || m.Name == null))
                throw new InvalidDataException("Invalid Conquest research or map assignments.");

            if (state.Version < 3)
            {
                foreach (var territory in state.Territories)
                {
                    territory.Income = territory.Bonus == ConquestBonus.Income ? 175 : 100;
                    if (territory.Bonus == ConquestBonus.Attack)
                        territory.Buffs[ConquestStat.Damage] = 10;
                    if (territory.Bonus == ConquestBonus.Defense)
                        territory.Buffs[ConquestStat.Armor] = 10;
                    territory.BattleMap = new ConquestMapAssignment
                    {
                        Hash = state.Settings.BattleMapHash, GameMode = state.Settings.BattleGameMode
                    };
                }
                foreach (var economy in new[] { state.Player, state.AI })
                {
                    if (economy.UpgradeLevel > 0)
                        economy.Upgrades[ConquestStat.Damage] = Math.Min(3, economy.UpgradeLevel);
                }
            }
            state.Version = 4;
            var perks = state.Settings.Perks;
            if (perks == null || perks.Count > 32 || perks.Any(p => p == null || string.IsNullOrEmpty(p.Id) || p.Id.Length > 80 ||
                p.Name == null || p.Description == null || p.MapCodeFile == null || p.SpawnCodeFile == null ||
                !Enum.IsDefined(typeof(ConquestPerkKind), p.Kind) || p.MaximumLevel < 1 || p.MaximumLevel > 3 ||
                p.Amount < 0 || p.Amount > 100 || p.AutoCombatStrength < 0 || p.AutoCombatStrength > 10) ||
                perks.Select(p => p.Id).Distinct().Count() != perks.Count)
                throw new InvalidDataException("Invalid Conquest capture reward definitions.");

            foreach (var territory in state.Territories)
            {
                if (territory.RewardId == null || territory.RewardClaimedBy == null ||
                    (territory.RewardId.Length > 0 && !perks.Any(p => p.Id == territory.RewardId)) ||
                    territory.RewardClaimedBy.Any(f => f != ConquestFaction.Player && f != ConquestFaction.AI) ||
                    territory.RewardClaimedBy.Distinct().Count() != territory.RewardClaimedBy.Count)
                    throw new InvalidDataException("Invalid Conquest capture reward.");
            }
            bool validPerks(Dictionary<string, int>? owned) => owned != null && owned.All(e =>
                perks.Any(p => p.Id == e.Key && e.Value >= 1 && e.Value <= p.MaximumLevel));
            if (!validPerks(state.Player.Perks) || !validPerks(state.AI.Perks) ||
                (state.PendingBattle != null && (!validPerks(state.PendingBattle.PlayerPerks) || !validPerks(state.PendingBattle.AIPerks))))
                throw new InvalidDataException("Invalid Conquest permanent perks.");
            foreach (var territory in state.Territories)
            {
                if (territory.Income < 50 || territory.Income > 1000 || !ValidStats(territory.Buffs, 100) ||
                    territory.BattleMap == null || territory.BattleMap.Hash == null || territory.BattleMap.GameMode == null)
                    throw new InvalidDataException("Invalid Conquest territory effects.");
            }

            if (state.Armies.Any(a => a == null || a.Id < 1 || a.Id >= state.NextId || a.Strength < 1 ||
                a.Strength > ConquestCampaign.MaximumStrength || a.TerritoryId < 0 || a.TerritoryId >= size * size ||
                a.Faction == ConquestFaction.Neutral || state.Territories[a.TerritoryId].Owner != a.Faction) ||
                state.Armies.Select(a => a.Id).Distinct().Count() != state.Armies.Count ||
                state.Armies.Select(a => a.TerritoryId).Distinct().Count() != state.Armies.Count)
            {
                throw new InvalidDataException("Invalid Conquest army.");
            }

            foreach (var economy in new[] { state.Player, state.AI })
            {
                if (economy.Credits < 0 || economy.Credits > 100000000 || economy.UpgradeLevel < 0 ||
                    economy.UpgradeLevel > 15 || !ValidStats(economy.Upgrades, 3) || economy.TemporaryBonuses < 0 || economy.TemporaryBonuses > 3)
                {
                    throw new InvalidDataException("Invalid Conquest economy.");
                }
            }

            var battle = state.PendingBattle;
            if ((state.Phase == ConquestPhase.AwaitingBattle) != (battle != null))
                throw new InvalidDataException("Invalid Conquest battle phase.");

            if (battle != null)
            {
                var army = state.Armies.Find(a => a.Id == battle.ArmyId);
                if (battle.Id < 1 || battle.Id >= state.NextId || battle.SkirmishGameId < 0 || battle.LocalPlayerName == null || !ValidStats(battle.PlayerStats, 120) || !ValidStats(battle.AIStats, 120) ||
                    army == null || !army.HasActed ||
                    army.Faction != battle.Attacker || army.TerritoryId != battle.SourceTerritoryId ||
                    battle.TargetTerritoryId < 0 || battle.TargetTerritoryId >= size * size ||
                    state.Territories[battle.TargetTerritoryId].Owner == battle.Attacker ||
                    !state.Connections.Any(e => e.Joins(battle.SourceTerritoryId, battle.TargetTerritoryId)) ||
                    battle.AttackPower < 1 || battle.AttackPower > 20000 || battle.DefensePower < 1 || battle.DefensePower > 20000)
                {
                    throw new InvalidDataException("Invalid Conquest pending battle.");
                }
            }

            if ((state.Phase == ConquestPhase.Finished) != state.Winner.HasValue ||
                (state.Winner.HasValue && ((state.Winner != ConquestFaction.Player && state.Winner != ConquestFaction.AI) ||
                !state.Territories.All(t => t.Owner == state.Winner || t.Owner == ConquestFaction.Neutral))))
            {
                throw new InvalidDataException("Invalid Conquest outcome.");
            }
        }
    }
}
