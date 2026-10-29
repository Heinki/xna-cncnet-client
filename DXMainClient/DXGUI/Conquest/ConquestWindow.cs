#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using ClientCore.Extensions;

using ClientGUI;

using DTAClient.Domain.Conquest;

using Microsoft.Xna.Framework;

using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Conquest
{
    internal sealed class ConquestWindow : ConquestCanvas
    {
        private readonly ConquestSaveStore store;
        private readonly ConquestMapPanel map;
        private readonly ConquestHistoryPanel history;
        private readonly XNALabel title;
        private readonly XNALabel economy;
        private readonly XNALabel selection;
        private readonly ConquestTerritoryPanel details;
        private ConquestBenefitsPanel? benefits;
        private readonly XNALabel battleStatus;
        private readonly XNAClientButton recruit;
        private readonly XNAClientButton upgrade;
        private readonly XNAClientButton bonus;
        private readonly XNAClientButton endTurn;
        private readonly XNAClientButton autoCombat;
        private readonly XNAClientButton skirmish;
        private readonly int buttonWidth;
        private bool saveFailed;
        private double aiDelay;

        public ConquestWindow(WindowManager windowManager, ConquestCampaign campaign, ConquestSaveStore store,
            int width, int height, ConquestArmyAppearance playerAppearance, ConquestArmyAppearance aiAppearance) : base(windowManager)
        {
            Campaign = campaign;
            this.store = store;
            Name = nameof(ConquestWindow);
            buttonWidth = width < 900 ? 92 : 133;
            int step = buttonWidth + 10;
            ClientRectangle = new Rectangle(0, 0, width, height);
            DrawOrder = 1000;
            UpdateOrder = 1000;

            title = Label("lblTitle", 12, 8);
            economy = Label("lblEconomy", 12, 35);
            Label("lblMapHelp", 12, 57).Text = "Select an army, then a highlighted node. Right/middle drag: pan. Wheel: zoom. Empty space: deselect.".L10N("Client:Conquest:ArmyNetworkHelp");
            int detailsWidth = width < 1000 ? 166 : 200;
            details = new ConquestTerritoryPanel(windowManager, campaign)
            {
                Name = "pnlTerritory",
                ClientRectangle = new Rectangle(12, 82, detailsWidth, height - 278)
            };
            AddChild(details);
            map = new ConquestMapPanel(windowManager, campaign, playerAppearance, aiAppearance)
            {
                Name = "ConquestMap",
                ClientRectangle = new Rectangle(detailsWidth + 24, 82, width - detailsWidth - 36, height - 278),
                TerritorySelected = SelectTerritory,
                TerritoryHovered = id => details.Inspect(id >= 0 ? id : map!.SelectedTerritory),
                SelectionCleared = () =>
                {
                    map!.SelectedArmyTerritory = null;
                    Refresh();
                }
            };
            AddChild(map);
            map.FocusTerritory(0);

            selection = Label("lblSelection", 12, height - 186);
            battleStatus = Label("lblBattleStatus", 12, height - 160);
            recruit = Button("btnRecruit", "Recruit 100".L10N("Client:Conquest:Recruit"), 12, height - 126,
                () => Commit(() => Campaign.Recruit(ConquestFaction.Player, map.SelectedTerritory)));
            upgrade = Button("btnResearch", "Research…".L10N("Client:Conquest:OpenResearch"), 12 + step, height - 126,
                () => ShowBenefits(ConquestBenefitsTab.Research));
            bonus = Button("btnSupplies", "Supplies…".L10N("Client:Conquest:OpenSupplies"), 12 + step * 2, height - 126,
                () => ShowBenefits(ConquestBenefitsTab.Supplies));
            recruit.ToolTipText = "Add 5 strength at this owned node for 100 credits. Uses that army's action for this turn.".L10N("Client:Conquest:RecruitTooltip");
            upgrade.ToolTipText = "Open permanent research. Choose a stat in the research screen.\n400 credits per level, maximum 3 levels per stat.\nNode bonuses apply while owned; total stat bonus capped at +100%.".L10N("Client:Conquest:UpgradeTooltip");
            bonus.ToolTipText = "Faction-wide supply: +20% damage in your NEXT battle, attack or defense.\nCosts 150 credits; consumed once when the attack begins.\nStore up to 3. Not tied to the selected node.".L10N("Client:Conquest:SupplyTooltip");
            endTurn = Button("btnEndTurn", "End Turn".L10N("Client:Conquest:EndTurn"), 12 + step * 3, height - 126,
                () => Commit(Campaign.EndPlayerTurn));
            Button("btnSave", "Save".L10N("Client:Conquest:Save"), 12 + step * 4, height - 126, () =>
            {
                Save();
                Refresh();
            });
            Button("btnMainMenu", "Main Menu".L10N("Client:Main:MainMenu"), width - buttonWidth - 12, height - 126, () =>
            {
                if (Save())
                    ExitRequested?.Invoke();
            });

            autoCombat = Button("btnAutobattle", "Autobattle".L10N("Client:Conquest:Autobattle"), 20, 82 + details.Height - 31,
                () => Commit(() => Campaign.ResolveAutoCombat(Campaign.State.PendingBattle?.Id ?? -1)));
            skirmish = Button("btnStartBattle", "Start Battle".L10N("Client:Conquest:StartBattle"), 20, 82 + details.Height - 60,
                () => SkirmishRequested?.Invoke());
            skirmish.Width = autoCombat.Width = detailsWidth - 16;
            skirmish.FontIndex = autoCombat.FontIndex = 0;
            skirmish.ToolTipText = "Fight on this node's assigned map with your faction's researched and territory stats.".L10N("Client:Conquest:SkirmishStatsHelp");
            Button("btnZoomIn", "Zoom +".L10N("Client:Conquest:ZoomIn"), 12 + step * 2, height - 87, () => map.Zoom(1.2f));
            Button("btnZoomOut", "Zoom -".L10N("Client:Conquest:ZoomOut"), 12 + step * 3, height - 87, () => map.Zoom(1 / 1.2f));
            Button("btnCenter", "Center".L10N("Client:Conquest:Center"), 12 + step * 4, height - 87, () => map.FocusTerritory(map.SelectedTerritory));
            Button("btnOverview", "Overview".L10N("Client:Conquest:Overview"), width - buttonWidth - 12, height - 87, map.ShowOverview);
            Button("btnBonuses", "Bonuses…".L10N("Client:Conquest:OpenBonuses"), 12, height - 87,
                () => ShowBenefits(ConquestBenefitsTab.Player));
            Button("btnPerkShop", "Perk shop…".L10N("Client:Conquest:OpenPerkShop"), 12 + step, height - 87,
                () => ShowBenefits(ConquestBenefitsTab.Shop));
            AddChild(new ConquestPurchaseSummary(windowManager, campaign)
            {
                Name = "pnlPurchaseSummary",
                ClientRectangle = new Rectangle(12, height - 61, width - 24, 51)
            });

            history = new ConquestHistoryPanel(windowManager, campaign, 420, map.Height,
                id =>
                {
                    map.FocusTerritory(id);
                    Refresh();
                },
                speed =>
                {
                    map.ChangeAnimationSpeed(speed);
                    aiDelay = 0;
                    Save();
                    Refresh();
                },
                collapsed =>
                {
                    map.Width = collapsed ? Width - map.X - 12 : Math.Max(100, history!.X - map.X - 12);
                    map.ShowOverview();
                })
            {
                Name = "pnlHistory",
                X = width - 432,
                Y = map.Y
            };
            AddChild(history);
            map.Width = Math.Max(100, width - map.X - 444);
            if (width < 1100)
                history.ToggleCollapsed();
            map.ShowOverview();

            Campaign.ActionOccurred += CampaignActionOccurred;
            Refresh();
        }

        public override void Initialize()
        {
            base.Initialize();
            map.Width = history.Collapsed ? Width - map.X - 12 : Math.Max(100, history.X - map.X - 12);
            map.ShowOverview();
            Refresh();
        }

        private void ShowBenefits(ConquestBenefitsTab tab)
        {
            if (benefits == null)
            {
                map.Enabled = false;
                int territoryId = map.SelectedTerritory;

                benefits = new ConquestBenefitsPanel(WindowManager, Campaign, Width, Height,
                    stat => Commit(() => Campaign.BuyUpgrade(ConquestFaction.Player, stat)),
                    () => Commit(() => Campaign.BuyTemporaryBonus(ConquestFaction.Player)),
                    territoryId,
                    id => Commit(() => Campaign.BuyPerk(ConquestFaction.Player, id, territoryId)),
                    () =>
                    {
                        benefits!.Kill();
                        RemoveChild(benefits);
                        benefits = null;
                        map.Enabled = true;
                    });
                AddChild(benefits);
            }
            benefits.ShowTab(tab);
        }

        public ConquestCampaign Campaign { get; }
        public Action? ExitRequested { get; set; }
        public Action? SkirmishRequested { get; set; }

        private XNALabel Label(string name, int x, int y)
        {
            var label = new XNALabel(WindowManager) { Name = name, ClientRectangle = new Rectangle(x, y, 0, 0) };
            AddChild(label);

            return label;
        }

        private XNAClientButton Button(string name, string text, int x, int y, Action action)
        {
            var button = new ConquestButton(WindowManager)
            {
                Name = name,
                Text = text,
                ClientRectangle = new Rectangle(x, y, buttonWidth, 23),
                FontIndex = buttonWidth == 92 ? 0 : 1
            };
            button.LeftClick += (_, _) => action();
            AddChild(button);

            return button;
        }

        private void SelectTerritory(int id)
        {
            if (!saveFailed && map.SelectedArmyTerritory.HasValue &&
                Campaign.Move(ConquestFaction.Player, map.SelectedArmyTerritory.Value, id))
            {
                map.SelectedArmyTerritory = null;
                Save();
            }
            else
            {
                var army = Campaign.ArmyAt(id);
                if (map.CanIssueOrders && army?.Faction == ConquestFaction.Player && !army.HasActed)
                    map.SelectedArmyTerritory = id;
            }

            map.SelectedTerritory = id;
            Refresh();
        }

        private void Commit(Action action)
        {
            if (saveFailed)
                return;

            action();
            Save();
            Refresh();
        }

        private void CampaignActionOccurred(object? sender, ConquestEvent action)
        {
            if (action.FactionId == ConquestFaction.AI)
                map.Animate(action);

            if (action.EventType == ConquestEventType.TerritoryAttacked && action.TargetTerritoryId.HasValue &&
                (action.FactionId == ConquestFaction.Player || Campaign.State.Territories[action.TargetTerritoryId.Value].Owner == ConquestFaction.Player))
                map.FocusBattle(action.TargetTerritoryId.Value);
        }

        public bool Save()
        {
            try
            {
                store.Save(Campaign.State);
                saveFailed = false;

                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                saveFailed = true;
                XNAMessageBox.Show(WindowManager, "Conquest save failed".L10N("Client:Conquest:SaveFailed"),
                    "Campaign paused. Free disk space or restore write access, then click Save to continue.".L10N("Client:Conquest:SaveFailedHelp") + "\n" + ex.Message);

                return false;
            }
        }

        public void Refresh()
        {
            var state = Campaign.State;
            bool playerTurn = !saveFailed && state.Phase == ConquestPhase.PlayerTurn;
            map.CanIssueOrders = playerTurn;
            if (!playerTurn || (map.SelectedArmyTerritory.HasValue && Campaign.ArmyAt(map.SelectedArmyTerritory.Value)?.HasActed != false))
                map.SelectedArmyTerritory = null;

            string phase = state.Phase switch
            {
                ConquestPhase.PlayerTurn => "Player turn".L10N("Client:Conquest:PlayerTurn"),
                ConquestPhase.AITurn => "AI turn".L10N("Client:Conquest:AITurn"),
                ConquestPhase.AwaitingBattle => "Battle awaiting player input".L10N("Client:Conquest:BattlePending"),
                _ => state.Winner == ConquestFaction.Player ? "Victory".L10N("Client:Conquest:Victory") : "Defeat".L10N("Client:Conquest:Defeat")
            };
            title.Text = string.Format("CONQUEST — ROUND {0} | {1}".L10N("Client:Conquest:CampaignTitle"), state.TurnNumber, phase);
            economy.Text = string.Format("Campaign credits: {0} (+{1}/turn). Spend on armies, research and supplies.".L10N("Client:Conquest:CampaignCreditsHelp"),
                state.Player.Credits, Campaign.IncomePerTurn(ConquestFaction.Player));
            var territory = state.Territories[map.SelectedTerritory];
            var army = Campaign.ArmyAt(territory.Id);
            selection.Text = string.Format("{0} | {1} | +{2} credits/turn".L10N("Client:Conquest:SelectedNodeSummary"),
                ConquestText.Territory(territory), ConquestText.Faction(territory.Owner), territory.Income);
            var battle = state.PendingBattle;
            string hint = state.Phase switch
            {
                ConquestPhase.AITurn => "AI armies are moving. An attack on you will pause for your decision.".L10N("Client:Conquest:AIMovementHelp"),
                ConquestPhase.Finished => "Campaign finished. Inspect any node or review the campaign history.".L10N("Client:Conquest:FinishedHelp"),
                _ when map.SelectedArmyTerritory.HasValue => "Green route: move or merge. Orange route: attack. Click a highlighted node.".L10N("Client:Conquest:SelectedArmyRoutes"),
                _ => "Select an army to see where it can move or attack.".L10N("Client:Conquest:SelectArmyHint")
            };
            int playerPower = battle == null ? 0 : battle.Attacker == ConquestFaction.Player ? battle.AttackPower : battle.DefensePower;
            int enemyPower = battle == null ? 0 : battle.Attacker == ConquestFaction.Player ? battle.DefensePower : battle.AttackPower;

            battleStatus.Text = battle == null ? hint : string.Format(
                "Autobattle: {0:P0} win chance. Your power: {1} | Enemy: {2} (bonuses included). Start Battle: fight it yourself.".L10N("Client:Conquest:BattlePowerSummary"),
                ConquestCampaign.PlayerWinChance(battle), playerPower, enemyPower);
            details.Battle = battle;
            details.Inspect(map.SelectedTerritory);
            autoCombat.Visible = skirmish.Visible = battle != null;
            battleStatus.Text = string.Join("\n", Renderer.GetFixedTextLines(battleStatus.Text, 0, Width - 24));

            recruit.Enabled = playerTurn && territory.Owner == ConquestFaction.Player &&
                state.Player.Credits >= ConquestCampaign.RecruitmentCost &&
                (army == null || (!army.HasActed && army.Strength <= ConquestCampaign.MaximumStrength - Campaign.RecruitmentStrength(ConquestFaction.Player)));
            upgrade.Enabled = true;
            bonus.Enabled = state.Settings.SupportedStats.Contains(ConquestStat.Damage);
            endTurn.Enabled = playerTurn;
            autoCombat.Enabled = !saveFailed && battle != null && !battle.SkirmishLaunched;
            skirmish.Enabled = autoCombat.Enabled && !string.IsNullOrEmpty(state.Territories[battle!.TargetTerritoryId].BattleMap.Hash);
            if (autoCombat.Enabled)
            {
                autoCombat.ToolTipText = string.Format("Your win chance: {0:P1}. Loss chance: {1:P1}.".L10N("Client:Conquest:AutoOddsTooltip"), ConquestCampaign.PlayerWinChance(battle!), 1 - ConquestCampaign.PlayerWinChance(battle!));
            }
            recruit.ToolTipText = string.Format("100 campaign credits: add +{0} army power. Uses this army's move. Power improves autobattle odds; skirmish starting units are set separately.".L10N("Client:Conquest:RecruitPowerExplanation"),
                Campaign.RecruitmentStrength(ConquestFaction.Player));
            benefits?.Refresh();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (benefits != null || saveFailed || !WindowManager.HasFocus || map.Animating)
                return;

            var state = Campaign.State;
            aiDelay -= gameTime.ElapsedGameTime.TotalSeconds;
            if (aiDelay > 0 && state.AnimationSpeed != 0)
                return;

            bool changed = false;
            var timer = Stopwatch.StartNew();
            for (int i = 0; i < 32 && timer.ElapsedMilliseconds < 8; i++)
            {
                if (state.Phase == ConquestPhase.AwaitingBattle)
                {
                    var battle = state.PendingBattle!;
                    // AI versus neutral is automatic. Every battle involving the player pauses.
                    if (battle.Attacker != ConquestFaction.AI ||
                        state.Territories[battle.TargetTerritoryId].Owner == ConquestFaction.Player || battle.SkirmishLaunched)
                    {
                        break;
                    }

                    changed |= Campaign.ResolveAutoCombat(battle.Id);
                }
                else if (state.Phase == ConquestPhase.AITurn)
                {
                    changed |= Campaign.StepAI();
                }
                else
                {
                    break;
                }

                if (state.AnimationSpeed != 0)
                    break;
            }

            if (changed)
            {
                aiDelay = state.AnimationSpeed == 0 ? 0 : 0.8 / state.AnimationSpeed;
                Save();
                Refresh();
            }
        }

        public override void Draw(GameTime gameTime)
        {
            FillControlArea(new Color(10, 16, 24));
            base.Draw(gameTime);
        }

        public override void Kill()
        {
            Campaign.ActionOccurred -= CampaignActionOccurred;

            base.Kill();
        }
    }
}
