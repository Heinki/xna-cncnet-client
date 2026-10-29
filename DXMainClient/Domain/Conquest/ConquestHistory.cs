#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace DTAClient.Domain.Conquest
{
    public enum ConquestHistoryFilter
    {
        All,
        Player,
        AI
    }

    /// <summary>
    /// Append-only chronological history with constant-time faction filtering and indexed access.
    /// Display controls need only format the visible rows, regardless of campaign length.
    /// </summary>
    public sealed class ConquestHistory
    {
        private readonly List<ConquestEvent> all = new();
        private readonly List<ConquestEvent> player = new();
        private readonly List<ConquestEvent> ai = new();
        private readonly ReadOnlyCollection<ConquestEvent> allView;
        private readonly ReadOnlyCollection<ConquestEvent> playerView;
        private readonly ReadOnlyCollection<ConquestEvent> aiView;

        public ConquestHistory()
        {
            allView = all.AsReadOnly();
            playerView = player.AsReadOnly();
            aiView = ai.AsReadOnly();
        }

        public int Count => all.Count;
        public long LastId => all.Count == 0 ? 0 : all[all.Count - 1].Id;

        public IReadOnlyList<ConquestEvent> GetEvents(ConquestHistoryFilter filter) => filter switch
        {
            ConquestHistoryFilter.Player => playerView,
            ConquestHistoryFilter.AI => aiView,
            _ => allView
        };

        internal void Append(ConquestEvent action)
        {
            if (action.Id <= LastId)
                throw new InvalidOperationException("Conquest events must have increasing IDs.");

            all.Add(action);

            if (action.FactionId == ConquestFaction.Player)
                player.Add(action);
            else if (action.FactionId == ConquestFaction.AI)
                ai.Add(action);
        }
    }
}