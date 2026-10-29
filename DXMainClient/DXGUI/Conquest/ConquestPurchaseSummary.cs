#nullable enable

using ClientCore.Extensions;

using DTAClient.Domain.Conquest;

using Microsoft.Xna.Framework;

using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Conquest
{
    internal sealed class ConquestPurchaseSummary : XNAControl
    {
        private readonly ConquestCampaign campaign;
        public ConquestPurchaseSummary(WindowManager windowManager, ConquestCampaign campaign) : base(windowManager)
        {
            this.campaign = campaign;
        }

        private void DrawWrapped(string text, Vector2 location, int width)
        {
            foreach (string line in Renderer.GetFixedTextLines(text, 0, width))
            {
                DrawString(line, 0, location, Color.White);
                location.Y += 14;
            }
        }

        public override void Draw(GameTime gameTime)
        {
            int half = (Width - 12) / 2;
            FillRectangle(new Rectangle(0, 0, half, Height), new Color(37, 31, 17));
            FillRectangle(new Rectangle(half + 12, 0, Width - half - 12, Height), new Color(14, 34, 47));
            FillRectangle(new Rectangle(0, 0, 3, Height), Color.Gold);
            FillRectangle(new Rectangle(half + 12, 0, 3, Height), Color.DeepSkyBlue);
            DrawString("PERMANENT RESEARCH".L10N("Client:Conquest:PurchaseSummary:Research"), 0, new Vector2(10, 4), Color.Gold);
            DrawWrapped(campaign.State.Settings.SupportedStats.Count > 0
                ? "Research… opens stat purchases. +10% / level, 400 credits.".L10N("Client:Conquest:PurchaseSummary:ResearchHelp")
                : "Combat research unavailable in this game.".L10N("Client:Conquest:Benefits:NoResearch"),
                new Vector2(10, 23), half - 20);
            DrawString(string.Format("TEMPORARY SUPPLIES — {0}/3 stored".L10N("Client:Conquest:PurchaseSummary:Supplies"), campaign.State.Player.TemporaryBonuses),
                0, new Vector2(half + 22, 4), Color.LightSkyBlue);
            DrawWrapped(campaign.State.Settings.SupportedStats.Contains(ConquestStat.Damage)
                ? "+20% damage next battle. One charge consumed on attack or defense.".L10N("Client:Conquest:PurchaseSummary:SupplyHelp")
                : "Supplies unavailable in this game.".L10N("Client:Conquest:Benefits:NoSupplies"),
                new Vector2(half + 22, 23), half - 20);
            base.Draw(gameTime);
        }
    }
}
