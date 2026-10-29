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
            FillRectangle(new Rectangle(0, 0, Width, Height), new Color(14, 34, 47));
            FillRectangle(new Rectangle(0, 0, 3, Height), Color.DeepSkyBlue);
            DrawString(string.Format("SUPPLIES — {0}/3 charges".L10N("Client:Conquest:PurchaseSummary:SupplyCharges"), campaign.State.Player.TemporaryBonuses),
                0, new Vector2(10, 4), Color.LightSkyBlue);
            DrawWrapped(campaign.State.Settings.SupportedStats.Contains(ConquestStat.Damage)
                ? "1 charge = +20% damage for one battle. Buy charges under Supplies for 150 credits each.".L10N("Client:Conquest:PurchaseSummary:ChargeHelp")
                : "Supplies unavailable in this game.".L10N("Client:Conquest:Benefits:NoSupplies"),
                new Vector2(10, 23), Width - 20);
            base.Draw(gameTime);
        }
    }
}