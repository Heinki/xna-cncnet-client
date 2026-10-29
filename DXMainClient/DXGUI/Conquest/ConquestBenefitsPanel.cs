#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

using ClientCore.Extensions;

using ClientGUI;

using DTAClient.Domain.Conquest;

using Microsoft.Xna.Framework;

using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Conquest
{
    internal enum ConquestBenefitsTab
    {
        Research, Supplies, Player, Enemy, Shop
    }

    /// <summary>Full-size input shield with a centered research and faction comparison window.</summary>
    internal sealed class ConquestBenefitsPanel : ConquestCanvas
    {
        private readonly ConquestCampaign campaign;
        private readonly Action<ConquestStat> buyResearch;
        private readonly Action buySupply;
        private readonly List<ConquestButton> tabs = new();
        private readonly Dictionary<ConquestStat, ConquestButton> purchases = new();
        private readonly ConquestButton supplyPurchase;
        private readonly Rectangle body;
        private readonly List<(string Text, Color Color)> lines = new();
        private ConquestBenefitsTab tab;
        private readonly ConquestTextScrollPanel summary;
        private readonly ConquestScrollPanel shop;
        private readonly int territoryId;
        private readonly Dictionary<string, (ConquestButton Button, XNALabel Level)> perkPurchases = new();

        public ConquestBenefitsPanel(WindowManager windowManager, ConquestCampaign campaign, int width, int height,
            Action<ConquestStat> buyResearch, Action buySupply, int territoryId, Action<string> buyPerk, Action close) : base(windowManager)
        {
            this.campaign = campaign;
            this.buyResearch = buyResearch;
            this.buySupply = buySupply;
            this.territoryId = territoryId;
            Name = nameof(ConquestBenefitsPanel);
            ClientRectangle = new Rectangle(0, 0, width, height);
            DrawOrder = 2000;
            UpdateOrder = 2000;
            int panelWidth = Math.Min(730, width - 32);
            int panelHeight = Math.Min(570, height - 32);
            body = new Rectangle((width - panelWidth) / 2, (height - panelHeight) / 2, panelWidth, panelHeight);
            summary = new ConquestTextScrollPanel(windowManager, 22)
            {
                Name = "spBonusSummary",
                ClientRectangle = new Rectangle(body.X + 16, body.Y + 90, body.Width - 32, body.Height - 146)
            };
            AddChild(summary);
            string[] names =
            {
                "Research".L10N("Client:Conquest:Benefits:Research"),
                "Supplies".L10N("Client:Conquest:Benefits:Supplies"),
                "Player bonuses".L10N("Client:Conquest:Benefits:Player"),
                "Enemy bonuses".L10N("Client:Conquest:Benefits:Enemy"),
                "Perk shop".L10N("Client:Conquest:Benefits:Shop")
            };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                var button = Button("btnTab" + (ConquestBenefitsTab)index, names[i], body.X + 16 + i * ((body.Width - 32) / names.Length), body.Y + 48,
                    (body.Width - 56) / names.Length, () => ShowTab((ConquestBenefitsTab)index));
                button.TextColorIdle = index == 3 ? Color.Salmon : index == 2 ? Color.LightSkyBlue : index == 0 ? Color.Gold : Color.LightSkyBlue;
                tabs.Add(button);
            }
            foreach (ConquestStat stat in campaign.State.Settings.SupportedStats)
            {
                var captured = stat;
                int row = purchases.Count;
                purchases[stat] = Button("btnBuy" + stat, "Buy: 400 credits".L10N("Client:Conquest:Benefits:ResearchPrice"), body.Right - 149,
                    body.Y + 136 + row * 61, 133, () => this.buyResearch(captured));
            }
            supplyPurchase = Button("btnBuySupply", "Buy charge: 150 credits".L10N("Client:Conquest:Benefits:SupplyPrice"),
                body.X + 24, body.Y + 239, 170, () => this.buySupply());
            shop = new ConquestScrollPanel(windowManager)
            {
                Name = "spPerkShop",
                ClientRectangle = summary.ClientRectangle
            };
            AddChild(shop);
            int shopY = 4;
            foreach (var perk in campaign.State.Settings.Perks.Where(p => p.PurchaseCost > 0))
            {
                string id = perk.Id;
                var caption = new XNALabel(windowManager)
                {
                    Name = "lblPerk" + id,
                    X = 6,
                    Y = shopY,
                    TextColor = perk.TerritoryOnly ? Color.LightSkyBlue : Color.Plum,
                    Text = string.Join("\n", Renderer.GetFixedTextLines(ConquestText.PerkName(perk), 0, shop.ContentWidth - 16))
                };
                shop.Content.AddChild(caption);
                string scope = perk.TerritoryOnly
                    ? string.Format("Defenses for {0} only; pass to its captor.".L10N("Client:Conquest:Benefits:NodePerkScope"), ConquestText.Territory(campaign.State.Territories[territoryId]))
                    : "Permanent. Applies to your whole faction.".L10N("Client:Conquest:Benefits:PermanentFactionScope");
                string description = string.Join("\n", Renderer.GetFixedTextLines(ConquestText.PerkDescription(perk) + "\n" + scope, 0, shop.ContentWidth - 16));
                var help = new XNALabel(windowManager)
                {
                    Name = "lblPerkHelp" + id,
                    X = 6,
                    Y = caption.Y + caption.Height + 6,
                    TextColor = Color.LightGray,
                    Text = description
                };
                shop.Content.AddChild(help);
                int purchaseY = help.Y + help.Height + 6;
                var level = new XNALabel(windowManager) { Name = "lblPerkLevel" + id, X = 6, Y = purchaseY + 3 };
                shop.Content.AddChild(level);
                var purchase = Button("btnBuyPerk" + id,
                    string.Format("Buy: {0} credits".L10N("Client:Conquest:Benefits:PerkPrice"), perk.PurchaseCost),
                    shop.ContentWidth - 145, purchaseY, 133, () => buyPerk(id), shop.Content);
                perkPurchases[id] = (purchase, level);
                shopY = purchaseY + 42;
            }
            shop.RefreshContentSize();
            Button("btnClose", "Close".L10N("Client:Conquest:Benefits:Close"), body.Right - 149, body.Bottom - 39, 133, close);
            Refresh();
        }

        public void ShowTab(ConquestBenefitsTab selected)
        {
            tab = selected;
            summary.ScrollToTop();
            shop.CurrentViewPosition = Point.Zero;
            Refresh();
        }

        private ConquestButton Button(string name, string text, int x, int y, int width, Action action, XNAControl? parent = null)
        {
            var button = new ConquestButton(WindowManager)
            {
                Name = name,
                IdleTexture = AssetLoader.LoadTexture("133pxbtn.png"),
                HoverTexture = AssetLoader.LoadTexture("133pxbtn_c.png"),
                Text = text,
                FontIndex = 0,
                ClientRectangle = new Rectangle(x, y, width, 23)
            };
            button.LeftClick += (_, _) => action();
            (parent ?? this).AddChild(button);

            return button;
        }

        public void Refresh()
        {
            for (int i = 0; i < tabs.Count; i++)
                tabs[i].Enabled = i != (int)tab;
            bool canBuy = campaign.State.Phase == ConquestPhase.PlayerTurn;
            foreach (var purchase in purchases)
            {
                purchase.Value.Visible = tab == ConquestBenefitsTab.Research;
                purchase.Value.Enabled = canBuy && campaign.State.Player.Credits >= ConquestCampaign.UpgradeCost &&
                    campaign.State.Player.Upgrades.GetValueOrDefault(purchase.Key) < 3;
            }
            supplyPurchase.Visible = tab == ConquestBenefitsTab.Supplies;
            shop.Visible = shop.Enabled = tab == ConquestBenefitsTab.Shop;
            foreach (var perk in campaign.State.Settings.Perks.Where(p => perkPurchases.ContainsKey(p.Id)))
            {
                var purchase = perkPurchases[perk.Id];
                int level = (perk.TerritoryOnly ? campaign.State.Territories[territoryId].Perks : campaign.State.Player.Perks).GetValueOrDefault(perk.Id);
                purchase.Level.Text = string.Format("Level {0}/{1}".L10N("Client:Conquest:Benefits:ShopLevel"), level, perk.MaximumLevel);
                purchase.Button.Enabled = campaign.CanBuyPerk(ConquestFaction.Player, perk.Id, territoryId);
            }
            supplyPurchase.Enabled = canBuy && campaign.State.Player.Credits >= ConquestCampaign.TemporaryBonusCost &&
                campaign.State.Player.TemporaryBonuses < 3 && campaign.State.Settings.SupportedStats.Contains(ConquestStat.Damage);
            lines.Clear();
            summary.Visible = summary.Enabled = tab == ConquestBenefitsTab.Player || tab == ConquestBenefitsTab.Enemy;
            if (summary.Visible)
            {
                BuildSummary(tab == ConquestBenefitsTab.Player ? ConquestFaction.Player : ConquestFaction.AI);
                summary.SetLines(lines);
            }
        }

        private void Add(string text, Color color)
        {
            lines.Add((text, color));
        }

        private void BuildSummary(ConquestFaction faction)
        {
            var summary = campaign.BonusSummary(faction);
            Add("STAT TOTALS — research + owned nodes (cap +100%)".L10N("Client:Conquest:Benefits:TotalsHeader"), Color.Gold);
            foreach (ConquestStat stat in campaign.State.Settings.SupportedStats)
            {
                Add(string.Format("{0}: {1}% research + {2}% nodes = +{3}% total".L10N("Client:Conquest:Benefits:FactionStatBreakdown"),
                    ConquestText.Stat(stat), summary.Research[stat], summary.Territories[stat], summary.Total[stat]), Color.White);
            }
            Add(string.Format("Income: {0} campaign credits/turn | Recruiting adds {1} army power".L10N("Client:Conquest:Benefits:EconomyHelp"),
                summary.Income, summary.RecruitmentStrength), Color.White);
            Add("PERMANENT PERKS — purchased or collected; kept after losing nodes".L10N("Client:Conquest:Benefits:OwnedPerksHeader"), Color.Plum);
            var owned = campaign.Economy(faction).Perks;
            bool any = false;
            foreach (var perk in campaign.State.Settings.Perks.Where(p => owned.GetValueOrDefault(p.Id) > 0))
            {
                any = true;
                Add(string.Format("{0} — level {1}/{2}: {3}".L10N("Client:Conquest:Benefits:PerkLevel"),
                    ConquestText.PerkName(perk), owned[perk.Id], perk.MaximumLevel, ConquestText.PerkDescription(perk)), Color.Plum);
            }
            if (!any)
                Add("No capture rewards collected.".L10N("Client:Conquest:Benefits:NoPerks"), Color.LightGray);
            foreach (var node in campaign.State.Territories.Where(t => t.Owner == faction && t.Perks.Count > 0))
            {
                foreach (var improvement in node.Perks)
                {
                    var perk = campaign.State.Settings.Perks.Single(p => p.Id == improvement.Key);
                    Add(string.Format("{0}: {1}, level {2} (local defense)".L10N("Client:Conquest:Benefits:NodeImprovement"),
                        ConquestText.Territory(node), ConquestText.PerkName(perk), improvement.Value), Color.LightSkyBlue);
                }
            }
            Add("TEMPORARY SUPPLIES — separate from permanent totals".L10N("Client:Conquest:Benefits:SupplyHeader"), Color.LightSkyBlue);
            Add(string.Format("Reserve: {0}/3. One charge gives +20% damage in the next battle.".L10N("Client:Conquest:Benefits:Reserve"), summary.Supplies), Color.LightSkyBlue);
            var pending = campaign.State.PendingBattle;
            if (pending != null)
            {
                var committed = faction == ConquestFaction.Player ? pending.PlayerStats : pending.AIStats;
                Add(string.Format("Current battle: committed damage bonus +{0}%; already consumed supplies stay committed.".L10N("Client:Conquest:Benefits:CommittedSupply"),
                    committed.GetValueOrDefault(ConquestStat.Damage)), Color.LightSkyBlue);
            }
        }

        public override void Draw(GameTime gameTime)
        {
            FillControlArea(Color.Black * 0.75f);
            FillRectangle(body, new Color(16, 26, 37));
            Color accent = tab == ConquestBenefitsTab.Enemy ? Color.Salmon : tab == ConquestBenefitsTab.Research ? Color.Gold : Color.LightSkyBlue;
            FillRectangle(new Rectangle(body.X, body.Y, body.Width, 3), accent);
            string heading = tab == ConquestBenefitsTab.Player ? "PLAYER BONUSES".L10N("Client:Conquest:Benefits:PlayerTitle") :
                tab == ConquestBenefitsTab.Enemy ? "ENEMY BONUSES".L10N("Client:Conquest:Benefits:EnemyTitle") :
                "CAMPAIGN BONUSES".L10N("Client:Conquest:Benefits:Title");
            DrawString(heading, 1, new Vector2(body.X + 16, body.Y + 15), accent);
            string wallet = string.Format("Credits: {0}".L10N("Client:Conquest:Benefits:Wallet"), campaign.State.Player.Credits);
            DrawString(wallet, 0, new Vector2(body.Right - Renderer.GetTextDimensions(wallet, 0).X - 16, body.Y + 15), Color.Gold);
            if (tab == ConquestBenefitsTab.Research)
                DrawResearch();
            else if (tab == ConquestBenefitsTab.Supplies)
                DrawSupplies();
            DrawChildren(gameTime);
        }

        private void DrawResearch()
        {
            DrawString("PERMANENT — choose a stat below; +10% per level; 400 credits".L10N("Client:Conquest:Benefits:ResearchHelp"),
                0, new Vector2(body.X + 20, body.Y + 91), Color.Gold);
            int row = 0;
            foreach (ConquestStat stat in campaign.State.Settings.SupportedStats)
            {
                int level = campaign.State.Player.Upgrades.GetValueOrDefault(stat);
                int y = body.Y + 132 + row++ * 61;
                DrawString(ConquestText.Stat(stat), 1, new Vector2(body.X + 24, y), Color.White);
                DrawString(string.Format("Permanent bonus: +{0}%".L10N("Client:Conquest:Benefits:ResearchAmount"), level * 10),
                    0, new Vector2(body.X + 24, y + 23), Color.Gold);
                for (int i = 0; i < 3; i++)
                    FillRectangle(new Rectangle(body.Right - 260 + i * 28, y + 8, 21, 16), i < level ? Color.Gold : new Color(51, 59, 66));
            }
            if (row == 0)
                DrawString("Combat research unavailable in this game.".L10N("Client:Conquest:Benefits:NoResearch"),
                    0, new Vector2(body.X + 24, body.Y + 136), Color.White);
            DrawString("Research survives node losses. Node buffs are listed separately in the faction tabs.".L10N("Client:Conquest:Benefits:ResearchScope"),
                0, new Vector2(body.X + 20, body.Bottom - 70), Color.LightGray);
        }

        private void DrawSupplies()
        {
            if (!campaign.State.Settings.SupportedStats.Contains(ConquestStat.Damage))
            {
                DrawString("Supplies unavailable in this game.".L10N("Client:Conquest:Benefits:NoSupplies"),
                    0, new Vector2(body.X + 24, body.Y + 100), Color.LightGray);
                return;
            }

            DrawString("TEMPORARY — one charge, one battle".L10N("Client:Conquest:Benefits:SupplyTitle"),
                1, new Vector2(body.X + 24, body.Y + 100), Color.LightSkyBlue);
            DrawString("+20% damage in your next attack or defense. Consumed when the battle begins.".L10N("Client:Conquest:Benefits:SupplyHelp"),
                0, new Vector2(body.X + 24, body.Y + 137), Color.White);
            for (int i = 0; i < 3; i++)
                FillRectangle(new Rectangle(body.X + 24 + i * 57, body.Y + 175, 44, 35),
                    i < campaign.State.Player.TemporaryBonuses ? Color.DeepSkyBlue : new Color(51, 59, 66));
            DrawString(string.Format("Supply charges: {0}/3".L10N("Client:Conquest:Benefits:SupplyCharges"), campaign.State.Player.TemporaryBonuses),
                0, new Vector2(body.X + 215, body.Y + 184), Color.LightSkyBlue);
            DrawString("Stored supplies do not increase your permanent stat totals.".L10N("Client:Conquest:Benefits:SupplySeparation"),
                0, new Vector2(body.X + 24, body.Y + 286), Color.LightGray);
        }
    }
}
