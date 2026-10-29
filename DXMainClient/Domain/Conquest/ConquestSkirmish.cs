#nullable enable

using System.Linq;

using ClientCore.Statistics;

namespace DTAClient.Domain.Conquest
{
    public static class ConquestSkirmish
    {
        /// <summary>
        /// Only an explicit local result from this launched match can resolve the pending battle.
        /// Missing, incomplete, contradictory or repeated results leave strategic state untouched.
        /// </summary>
        public static bool TryResolve(ConquestCampaign campaign, MatchStatistics statistics)
        {
            var battle = campaign.State.PendingBattle;
            if (battle == null || !battle.SkirmishLaunched || battle.SkirmishGameId != statistics.GameID ||
                !statistics.SawCompletion || statistics.Players.Count != 2)
            {
                return false;
            }

            var locals = statistics.Players.Where(p => p.IsLocalPlayer && !p.WasSpectator).ToList();
            var opponents = statistics.Players.Where(p => p.IsAI && !p.WasSpectator).ToList();
            if (locals.Count != 1 || opponents.Count != 1 || !locals[0].SawEnd ||
                locals[0].Name != battle.LocalPlayerName || (locals[0].Won && opponents[0].Won))
            {
                return false;
            }

            bool attackerWon = battle.Attacker == ConquestFaction.Player ? locals[0].Won : !locals[0].Won;

            return campaign.ResolveBattle(battle.Id, attackerWon, true);
        }
    }
}
