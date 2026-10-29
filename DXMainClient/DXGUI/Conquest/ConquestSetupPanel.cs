#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using ClientCore.Extensions;

using ClientGUI;

using DTAClient.Domain.Conquest;

using Microsoft.Xna.Framework;

using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Conquest
{
    internal sealed class ConquestSetupChoices
    {
        public IReadOnlyList<ConquestStat> SupportedStats { get; set; } = new[] { ConquestStat.Health, ConquestStat.Armor, ConquestStat.Speed, ConquestStat.AttackSpeed, ConquestStat.Damage };
        public IReadOnlyList<string> Sides { get; set; } = Array.Empty<string>();
        public int PlayerSide { get; set; }
        public int AISide { get; set; }
    }

    /// <summary>
    /// Campaign options inside the installed campaign window theme.
    /// </summary>
    internal sealed class ConquestSetupPanel : ConquestCanvas
    {
        public ConquestSetupPanel(WindowManager windowManager, int parentWidth, int parentHeight, int defaultSeed,
            Action<ConquestSettings> start, Action resume, Action back, ConquestSetupChoices? choices = null) : base(windowManager)
        {
            choices ??= new ConquestSetupChoices();
            Name = nameof(ConquestSetupPanel);
            int width = Math.Min(760, parentWidth - 64);
            int height = Math.Min(600, parentHeight - 36);
            ClientRectangle = new Rectangle((parentWidth - width) / 2, (parentHeight - height) / 2, width, height);
            DrawOrder = 1000;
            UpdateOrder = 1000;

            Label("lblSettingsTitle", 24, 20, "CONQUEST SETTINGS".L10N("Client:Conquest:SettingsTitle"));
            Label("lblIntroduction", 24, 52, "Capture nodes to earn campaign credits. Spend them on armies and upgrades; battle starting money is separate.".L10N("Client:Conquest:SetupCreditsExplanation"));
            int formX = (width - 460) / 2;
            int fieldX = formX + 200;
            Label("lblCampaignSize", formX, 94, "Campaign size".L10N("Client:Conquest:CampaignSize"));
            var size = DropDown("ddCampaignSize", fieldX, 90, new[]
            {
                "Short: 9 territories".L10N("Client:Conquest:SizeShort"), "Standard: 25 territories".L10N("Client:Conquest:SizeStandard"),
                "Long: 64 territories".L10N("Client:Conquest:SizeLong"), "Epic: 100 territories".L10N("Client:Conquest:SizeEpic")
            }, 1);
            size.ToolTipText = "Total territories on the campaign map. More territories mean a longer campaign.".L10N("Client:Conquest:CampaignSizeHelp");
            Label("lblStartingCredits", formX, 130, "Campaign credits".L10N("Client:Conquest:SetupCampaignCredits"));
            var credits = DropDown("ddStartingCredits", fieldX, 126, new[] { "500", "1000", "2000" }, 0);
            Label("lblDifficulty", formX, 166, "AI difficulty".L10N("Client:Conquest:AIDifficulty"));
            var difficulty = DropDown("ddDifficulty", fieldX, 162, new[]
            {
                "Easy".L10N("Client:Conquest:Easy"), "Normal".L10N("Client:Conquest:NormalDifficulty"),
                "Hard".L10N("Client:Conquest:Hard")
            }, 1);
            Label("lblSeed", formX, 202, "Map seed".L10N("Client:Conquest:MapSeed"));
            var seed = new XNATextBox(windowManager)
            {
                Name = "tbSeed",
                ClientRectangle = new Rectangle(fieldX, 198, 240, 23),
                MaximumTextLength = 11,
                Text = defaultSeed.ToString(CultureInfo.InvariantCulture)
            };
            AddChild(seed);

            Label("lblBonusDensity", formX, 238, "Node bonuses".L10N("Client:Conquest:BonusDensity"));
            var density = DropDown("ddBonusDensity", fieldX, 234, new[]
            {
                "Few".L10N("Client:Conquest:FewBuffs"), "Normal".L10N("Client:Conquest:NormalBuffs"),
                "Many".L10N("Client:Conquest:ManyBuffs")
            }, 1);
            var sideNames = choices.Sides.Count == 0
                ? new[] { "Random".L10N("Client:Conquest:RandomSide") } : choices.Sides;
            Label("lblPlayerFaction", formX, 274, "Your faction".L10N("Client:Conquest:YourFaction"));
            var playerSide = DropDown("ddPlayerFaction", fieldX, 270, sideNames, choices.PlayerSide);
            Label("lblAIFaction", formX, 310, "AI faction".L10N("Client:Conquest:AIFaction"));
            var aiSide = DropDown("ddAIFaction", fieldX, 306, sideNames, choices.AISide);

            Label("lblMovementHelp", 24, 358, "Select an army, then a connected node. Capture every enemy node to win.".L10N("Client:Conquest:SetupArmyHelp"));
            Label("lblResearchHelp", 24, 384, choices.SupportedStats.Count == 0 ? "Combat bonuses unavailable in this game.".L10N("Client:Conquest:CombatBonusesUnavailable") : string.Format("Research: {0}. +10% per level.".L10N("Client:Conquest:SetupStatResearch"), string.Join(", ", choices.SupportedStats.Select(ConquestText.Stat))));
            Label("lblSupplyHelp", 24, 410, choices.SupportedStats.Contains(ConquestStat.Damage) ? "Supply: 150 credits, +20% damage in your next battle. Store up to 3.".L10N("Client:Conquest:SetupSupplyHelp") : "Every node generates income. Recruit armies and capture connected nodes.".L10N("Client:Conquest:IncomeOnlyHelp"));

            Label("lblPerkHelp", 24, 442, "Some nodes grant permanent capture rewards. Compare both sides under Bonuses.".L10N("Client:Conquest:SetupPerkHelp"));

            Button("btnNewCampaign", "New Campaign".L10N("Client:Conquest:NewCampaign"), 24, height - 48, () =>
            {
                if (!int.TryParse(seed.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mapSeed))
                {
                    XNAMessageBox.Show(windowManager, "Map seed".L10N("Client:Conquest:MapSeed"),
                        "Enter a whole number between -2147483648 and 2147483647.".L10N("Client:Conquest:InvalidSeed"));
                    return;
                }

                start(new ConquestSettings
                {
                    MapSize = new[] { 3, 5, 8, 10 }[size.SelectedIndex],
                    StartingCredits = new[] { 500, 1000, 2000 }[credits.SelectedIndex],
                    Difficulty = difficulty.SelectedIndex,
                    Seed = mapSeed,
                    BuffDensity = density.SelectedIndex,
                    PlayerSide = playerSide.SelectedIndex,
                    AISide = aiSide.SelectedIndex
                });
            });
            Button("btnContinue", "Continue".L10N("Client:Conquest:Continue"), (width - 133) / 2, height - 48, resume);
            Button("btnMainMenu", "Main Menu".L10N("Client:Main:MainMenu"), width - 157, height - 48, back);
        }

        private void Label(string name, int x, int y, string text) => AddChild(new XNALabel(WindowManager)
        {
            Name = name,
            Text = string.Join("\n", Renderer.GetFixedTextLines(text, 0, Width - x - 24)),
            ClientRectangle = new Rectangle(x, y, 0, 0)
        });

        private XNAClientDropDown DropDown(string name, int x, int y, IReadOnlyList<string> items, int selected)
        {
            var dropDown = new XNAClientDropDown(WindowManager) { Name = name, ClientRectangle = new Rectangle(x, y, 240, 23) };
            foreach (string item in items)
                dropDown.AddItem(item);

            dropDown.SelectedIndex = Math.Max(0, Math.Min(selected, items.Count - 1));
            AddChild(dropDown);

            return dropDown;
        }

        private void Button(string name, string text, int x, int y, Action action)
        {
            var button = new ConquestButton(WindowManager) { Name = name, Text = text, ClientRectangle = new Rectangle(x, y, 133, 23) };
            button.LeftClick += (_, _) => action();
            AddChild(button);
        }
    }
}