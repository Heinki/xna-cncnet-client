#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

using ClientCore.Extensions;

using DTAClient.Domain.Conquest;

using Microsoft.Xna.Framework;

using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Conquest
{
    /// <summary>Node inspection stays independent of the selected army and movement orders.</summary>
    internal sealed class ConquestTerritoryPanel : XNAPanel
    {
        private readonly ConquestCampaign campaign;
        private readonly List<(string Text, Color Color)> lines = new();
        private readonly ConquestTextScrollPanel text;
        private int territoryId = -1;

        public ConquestTerritoryPanel(WindowManager windowManager, ConquestCampaign campaign) : base(windowManager)
        {
            this.campaign = campaign;
            text = new ConquestTextScrollPanel(windowManager, 19) { Name = "spTerritoryEffects" };
            AddChild(text);
            ClientRectangleUpdated += (_, _) => LayoutContent();
        }

        private ConquestBattle? battle;
        public ConquestBattle? Battle
        {
            get => battle;
            set
            {
                if (battle == value)
                    return;
                battle = value;
                LayoutContent();
            }
        }

        private void LayoutContent()
        {
            text.ClientRectangle = new Rectangle(4, 4, Math.Max(1, Width - 8), Math.Max(1, Height - ReservedHeight - 8));
        }
        private int ReservedHeight => Battle == null ? 0 : 112;

        public void Inspect(int id)
        {
            if (id != territoryId)
                text.ScrollToTop();

            territoryId = id;
            lines.Clear();
            var territory = campaign.State.Territories[id];
            Add(ConquestText.Territory(territory));
            Add(ConquestText.Faction(territory.Owner));
            Add(string.Format("Income: {0} credits / turn".L10N("Client:Conquest:NodeIncome"), territory.Income));
            foreach (var improvement in territory.Perks)
            {
                var perk = campaign.State.Settings.Perks.Single(p => p.Id == improvement.Key);
                Add(string.Format("Local defense: {0}, level {1}/{2}".L10N("Client:Conquest:LocalDefense"),
                    ConquestText.PerkName(perk), improvement.Value, perk.MaximumLevel));
            }

            if (territory.Perks.Count > 0)
                Add("Defends this node only. Captured defenses pass to the new owner.".L10N("Client:Conquest:LocalDefenseScope"));

            Add("Hold this node to boost your whole faction:".L10N("Client:Conquest:NodeBuffExplanation"));
            var buffs = territory.Buffs.Where(b => campaign.State.Settings.SupportedStats.Contains(b.Key)).ToList();
            if (buffs.Count == 0)
                Add("No combat buffs".L10N("Client:Conquest:NoCombatBuffs"));

            foreach (var buff in buffs)
                Add(string.Format("{0}: +{1}%".L10N("Client:Conquest:NodeStat"), ConquestText.Stat(buff.Key), buff.Value));

            var reward = campaign.State.Settings.Perks.Find(p => p.Id == territory.RewardId);
            if (reward != null)
            {
                Add("Capture reward — permanent:".L10N("Client:Conquest:NodePerkHeader"));
                Add(ConquestText.PerkName(reward));
                Add(ConquestText.PerkDescription(reward));
                if (campaign.State.Player.Perks.GetValueOrDefault(reward.Id) >= reward.MaximumLevel)
                    Add("Already owned at maximum level".L10N("Client:Conquest:NodePerkMaxed"));
                else if (territory.RewardClaimedBy.Contains(ConquestFaction.Player))
                    Add("Already collected by you".L10N("Client:Conquest:NodePerkCollected"));
            }

            var army = campaign.ArmyAt(id);
            if (Battle != null && (id == Battle.SourceTerritoryId || id == Battle.TargetTerritoryId))
                Add(string.Format("Army power: {0}".L10N("Client:Conquest:BattleArmyPower"), army?.Strength ?? 2));

            if (army != null)
                Add(army.HasActed ? "Action used".L10N("Client:Conquest:ActionUsed") : "Ready".L10N("Client:Conquest:Ready"));

            text.SetLines(lines);
        }

        private void Add(string value)
        {
            lines.Add((value, lines.Count == 0 ? Color.Gold : Color.White));
        }

        public override void Draw(GameTime gameTime)
        {
            FillControlArea(new Color(17, 25, 35));
            if (Battle != null)
            {
                double win = ConquestCampaign.PlayerWinChance(Battle);
                DrawString("BATTLE READY".L10N("Client:Conquest:BattleReady"), 0, new Vector2(8, Height - 106), Color.Gold);
                DrawString(string.Format("Win {0:P0} / Lose {1:P0}".L10N("Client:Conquest:CompactBattleOdds"), win, 1 - win),
                    0, new Vector2(8, Height - 86), win >= 0.5 ? Color.LightGreen : Color.Orange);
            }

            base.Draw(gameTime);
        }
    }
}
