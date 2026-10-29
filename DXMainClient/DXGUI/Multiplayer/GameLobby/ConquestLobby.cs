#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

using ClientCore;
using ClientCore.Extensions;
using ClientCore.INIProcessing;
using ClientCore.Statistics;

using ClientGUI;

using DTAClient.Domain;
using DTAClient.Domain.Conquest;
using DTAClient.Domain.Multiplayer;
using DTAClient.DXGUI.Conquest;
using DTAClient.DXGUI.Generic;

using Microsoft.Xna.Framework;

using Rampastring.Tools;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Multiplayer.GameLobby
{
    /// <summary>
    /// Offline strategic campaign using the installed mod's skirmish pipeline for battles.
    /// </summary>
    public class ConquestLobby : SkirmishLobby
    {
        private readonly Random campaignRandom;
        private readonly DirectDrawWrapperManager rendererManager;
        private readonly Dictionary<XNAControl, (bool Visible, bool Enabled)> lobbyControls = new();
        private ConquestSaveStore? store;
        private ConquestWindow? campaignWindow;
        private ConquestSetupPanel? setup;
        private long? launchedBattleId;
        private ConquestBattleRules? battleRules;
        private ConquestBattleConfiguration? battleConfiguration;

        public ConquestLobby(WindowManager windowManager, TopBar topBar, MapLoader mapLoader,
            DiscordHandler discordHandler, Random random, DirectDrawWrapperManager rendererManager)
            : base(windowManager, topBar, mapLoader, discordHandler, random)
        {
            campaignRandom = random;
            this.rendererManager = rendererManager;
        }

        protected override string SettingsPath => "Client/ConquestSettings.ini";
        protected override bool RecordMatchStatistics => false;
        protected override bool ForceClientStartingLocations => true;

        public override void Open()
        {
            base.Open();
            if (setup == null && campaignWindow == null)
                OpenCampaignSettings();
        }

        public override void Draw(GameTime gameTime)
        {
            if (setup != null)
                DrawChildren(gameTime);
            else
                base.Draw(gameTime);
        }

        public override void Initialize()
        {
            // Keep the mod's existing side selectors, maps and game options.
            base.Initialize();
            store = new ConquestSaveStore(SafePath.CombineFilePath(ProgramConstants.GamePath, "Client", "Conquest", "Campaign.json"));
            btnLaunchGame.Text = "Conquest Settings".L10N("Client:Conquest:OpenSettings");
            btnLaunchGame.ToolTipText = "Start or continue an offline strategic campaign.".L10N("Client:Conquest:CampaignDescription");
            btnLaunchGame.Enabled = true;
        }

        protected override bool UpdateLaunchGameButtonStatus()
        {
            // Auto combat campaigns remain available without an installed skirmish map.
            btnLaunchGame.Enabled = true;

            return true;
        }

        protected override void BtnLaunchGame_LeftClick(object sender, EventArgs e)
        {
            OpenCampaignSettings();
        }

        private void OpenCampaignSettings()
        {
            if (store == null)
                return;

            SaveSettings();
            battleConfiguration = ConquestBattleConfiguration.Read(ClientConfiguration.Instance.ClientGameType, LoadBattleConfiguration());
            HideLobbyControls();
            var choices = new ConquestSetupChoices
            {
                SupportedStats = battleConfiguration.SupportedStats,
                Sides = ddPlayerSides[0].Items.Take(SideCount + RandomSelectorCount).Select(i => i.Text).ToList(),
                PlayerSide = Players.FirstOrDefault()?.SideId ?? 0,
                AISide = AIPlayers.FirstOrDefault()?.SideId ?? 0
            };
            setup = new ConquestSetupPanel(WindowManager, Width, Height, campaignRandom.Next(),
                RequestNewCampaign, ResumeCampaign, () =>
                {
                    CloseSetup();
                    base.BtnLeaveGame_LeftClick(this, EventArgs.Empty);
                }, choices);
            AddChild(setup);
        }

        private void HideLobbyControls()
        {
            if (lobbyControls.Count != 0)
                return;

            foreach (var control in Children.ToArray())
            {
                lobbyControls.Add(control, (control.Visible, control.Enabled));
                control.Disable();
            }
        }

        private void CloseSetup()
        {
            if (setup != null)
            {
                setup.Kill();
                RemoveChild(setup);
                setup = null;
            }

            foreach (var entry in lobbyControls)
            {
                entry.Key.Visible = entry.Value.Visible;
                entry.Key.Enabled = entry.Value.Enabled;
            }

            lobbyControls.Clear();
        }

        private void RequestNewCampaign(ConquestSettings settings)
        {
            if (store!.Exists)
            {
                var prompt = XNAMessageBox.ShowYesNoDialog(WindowManager,
                    "New Campaign".L10N("Client:Conquest:NewCampaign"),
                    "Replace the saved campaign?".L10N("Client:Conquest:ReplaceCampaign"));
                prompt.YesClickedAction += _ => StartCampaign(settings);
            }
            else
            {
                StartCampaign(settings);
            }
        }

        private void StartCampaign(ConquestSettings settings)
        {
            settings.SupportedStats = battleConfiguration!.SupportedStats.ToList();
            settings.BattleMaps = GameModeMaps.Where(m => !m.MultiplayerOnly && !m.IsCoop && !m.HumanPlayersOnly &&
                m.MaxPlayers >= 2 && (!m.EnforceMinPlayers || m.MinPlayers <= 2))
                .Select(m => new ConquestMapAssignment { Hash = m.Map.SHA1, GameMode = m.GameMode.Name, Name = m.ToUIString() })
                .OrderBy(m => m.GameMode, StringComparer.Ordinal).ThenBy(m => m.Hash, StringComparer.Ordinal).ToList();
            var factionRandom = new Random(settings.Seed);
            int? playerSide = ResolveSide(settings.PlayerSide, true, factionRandom);
            int? aiSide = ResolveSide(settings.AISide, false, factionRandom);
            if (!playerSide.HasValue || !aiSide.HasValue)
            {
                XNAMessageBox.Show(WindowManager, "Cannot start campaign".L10N("Client:Conquest:CannotStart"),
                    "Choose available playable factions in Conquest Settings.".L10N("Client:Conquest:NoPlayableSides"));
                return;
            }

            settings.PlayerSide = playerSide.Value;
            settings.AISide = aiSide.Value;
            try
            {
                int humanCountry = new PlayerHouseInfo { SideIndex = settings.PlayerSide - RandomSelectorCount }.InternalSideIndex;
                int enemyCountry = new PlayerHouseInfo { SideIndex = settings.AISide - RandomSelectorCount }.InternalSideIndex;
                settings.Perks = battleConfiguration.LoadPerks(ProgramConstants.GamePath, humanCountry, enemyCountry);
            }
            catch (IOException ex)
            {
                XNAMessageBox.Show(WindowManager, "Cannot start campaign".L10N("Client:Conquest:CannotStart"), ex.Message);
                return;
            }
            settings.DropDownOptions = DropDowns.ToDictionary(d => d.Name, d => d.SelectedIndex);
            settings.CheckBoxOptions = CheckBoxes.ToDictionary(c => c.Name, c => c.Checked);
            var campaign = ConquestCampaign.Create(settings);
            ShowCampaign(campaign);
            campaign.Start();
            campaignWindow!.Save();
            campaignWindow.Refresh();
        }

        private void ResumeCampaign()
        {
            try
            {
                var state = store!.Load();
                state.Settings.SupportedStats = state.Settings.SupportedStats.Intersect(battleConfiguration!.SupportedStats).ToList();
                int humanCountry = new PlayerHouseInfo { SideIndex = state.Settings.PlayerSide - RandomSelectorCount }.InternalSideIndex;
                int enemyCountry = new PlayerHouseInfo { SideIndex = state.Settings.AISide - RandomSelectorCount }.InternalSideIndex;
                foreach (var perk in battleConfiguration.LoadPerks(ProgramConstants.GamePath, humanCountry, enemyCountry))
                {
                    if (!state.Settings.Perks.Any(p => p.Id == perk.Id) && state.Settings.Perks.Count < 32)
                        state.Settings.Perks.Add(perk);
                }
                // A process cannot resume across restarts. Keep the pending attack and its
                // consumed bonuses; allow retry or auto combat without replaying the attack.
                if (state.PendingBattle != null)
                    state.PendingBattle.SkirmishLaunched = false;

                ShowCampaign(new ConquestCampaign(state));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                XNAMessageBox.Show(WindowManager, "Cannot load Conquest".L10N("Client:Conquest:LoadFailed"), ex.Message);
            }
        }

        private void ShowCampaign(ConquestCampaign campaign)
        {
            if (setup != null)
            {
                setup.Kill();
                RemoveChild(setup);
                setup = null;
            }

            campaignWindow = new ConquestWindow(WindowManager, campaign, store!, Width, Height,
                LoadArmyAppearance(campaign.State.Settings.PlayerSide), LoadArmyAppearance(campaign.State.Settings.AISide));
            campaignWindow.ExitRequested = () =>
            {
                campaignWindow.Kill();
                RemoveChild(campaignWindow);
                campaignWindow = null;
                CloseSetup();
                base.BtnLeaveGame_LeftClick(this, EventArgs.Empty);
            };
            campaignWindow.SkirmishRequested = LaunchCampaignBattle;
            AddChild(campaignWindow);
        }

        private ConquestArmyAppearance LoadArmyAppearance(int side)
        {
            string[] names = ClientConfiguration.Instance.Sides.Split(',');
            int index = side - RandomSelectorCount;
            string faction = index >= 0 && index < names.Length ? names[index].Trim() : string.Empty;

            return ConquestArmyAppearance.Load(faction, GameOptionsIni.GetStringValue("ConquestArmyIcons", faction, string.Empty),
                GameOptionsIni.GetStringValue("ConquestArmyStyles", faction, string.Empty));
        }

        private void LaunchCampaignBattle()
        {
            var campaign = campaignWindow!.Campaign;
            var battle = campaign.State.PendingBattle;
            if (battle == null || battle.SkirmishLaunched || GameProcessLogic.IsGameProcessRunning)
                return;

            if (PreprocessorBackgroundTask.Instance.IsRunning)
            {
                XNAMessageBox.Show(WindowManager, "Cannot launch game".L10N("Client:Main:LaunchGameErrorTitle"),
                    "Game rules are still loading. Try again shortly.".L10N("Client:Conquest:RulesLoading"));
                return;
            }

            var settings = campaign.State.Settings;
            var assignment = campaign.State.Territories[battle.TargetTerritoryId].BattleMap;
            var battleMap = GameModeMaps.FirstOrDefault(m => m.Map.SHA1 == assignment.Hash && m.GameMode.Name == assignment.GameMode);
            if (battleMap == null || battleMap.MultiplayerOnly || battleMap.IsCoop || battleMap.HumanPlayersOnly ||
                battleMap.MaxPlayers < 2 || (battleMap.EnforceMinPlayers && battleMap.MinPlayers > 2))
            {
                XNAMessageBox.Show(WindowManager, "Cannot launch game".L10N("Client:Main:LaunchGameErrorTitle"),
                    "Campaign battles require the original offline map supporting one human and one AI. Use Auto Combat if unavailable.".L10N("Client:Conquest:BattleMapUnavailable"));
                return;
            }

            foreach (var dropDown in DropDowns)
            {
                if (settings.DropDownOptions.TryGetValue(dropDown.Name, out int value) && value >= 0 && value < dropDown.Items.Count)
                {
                    dropDown.UserSelectedIndex = value;
                    dropDown.SelectedIndex = value;
                }
            }

            foreach (var checkBox in CheckBoxes)
            {
                if (settings.CheckBoxOptions.TryGetValue(checkBox.Name, out bool value))
                {
                    checkBox.UserChecked = value;
                    checkBox.Checked = value;
                }
            }

            ChangeMap(battleMap);
            var factionRandom = new Random(settings.Seed);
            int? playerSide = ResolveSide(settings.PlayerSide, true, factionRandom);
            int? aiSide = ResolveSide(settings.AISide, false, factionRandom);
            if (!playerSide.HasValue || !aiSide.HasValue)
            {
                XNAMessageBox.Show(WindowManager, "Cannot launch game".L10N("Client:Main:LaunchGameErrorTitle"),
                    "Campaign sides are unavailable on this map. Use Auto Combat.".L10N("Client:Conquest:BattleSidesUnavailable"));
                return;
            }

            settings.PlayerSide = playerSide.Value;
            settings.AISide = aiSide.Value;
            Players.Clear();
            AIPlayers.Clear();
            Players.Add(new PlayerInfo(ProgramConstants.PLAYERNAME, settings.PlayerSide, 0, 0, 0));
            AIPlayers.Add(new PlayerInfo(ProgramConstants.AI_PLAYER_NAMES[0], settings.AISide, 0, 0, 0)
            {
                IsAI = true,
                AILevel = settings.Difficulty
            });
            SetPlayerExtraOptions(new PlayerExtraOptions());
            CopyPlayerDataToUI();
            string? error = CheckGameValidity();
            if (error != null)
            {
                XNAMessageBox.Show(WindowManager, "Cannot launch game".L10N("Client:Main:LaunchGameErrorTitle"), error);
                return;
            }

            try
            {
                // Clear before committing launch metadata, so even a crash at the save boundary
                // cannot associate the previous match's log with the new campaign battle.
                SafePath.DeleteFileIfExists(ProgramConstants.GamePath, ClientConfiguration.Instance.StatisticsLogFileName);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                XNAMessageBox.Show(WindowManager, "Cannot launch game".L10N("Client:Main:LaunchGameErrorTitle"), ex.Message);
                return;
            }

            RandomSeed = campaignRandom.Next();
            UniqueGameID = campaignRandom.Next(1, int.MaxValue);
            battle.SkirmishGameId = UniqueGameID;
            battle.LocalPlayerName = ProgramConstants.PLAYERNAME;
            battle.SkirmishLaunched = true;
            launchedBattleId = battle.Id;
            if (!campaignWindow.Save())
            {
                battle.SkirmishLaunched = false;
                launchedBattleId = null;
                campaignWindow.Refresh();
                return;
            }

            if (!ConquestSkirmish.TryLaunch(battle, () =>
            {
                EnableWindowedBattle();
                base.StartGame();
            }, out var launchError))
            {
                launchedBattleId = null;
                campaignWindow.Save();
                Logger.Log("Conquest launch failed: " + launchError);
                XNAMessageBox.Show(WindowManager, "Cannot launch game".L10N("Client:Main:LaunchGameErrorTitle"), launchError!.Message);
            }

            campaignWindow.Refresh();
        }

        private void EnableWindowedBattle()
        {
            UserINISettings.Instance.WindowedMode.Value = true;
            UserINISettings.Instance.BorderlessWindowedMode.Value = false;
            var renderer = rendererManager.SelectedRenderer;
            if (renderer.UsesCustomWindowedOption() && !string.IsNullOrEmpty(renderer.ConfigFileName))
            {
                rendererManager.Save(renderer);
                string path = SafePath.CombineFilePath(ProgramConstants.GamePath, renderer.ConfigFileName);
                var config = new IniFile(path);
                config.SetBooleanValue(renderer.WindowedModeSection, renderer.WindowedModeKey, true);
                if (!string.IsNullOrEmpty(renderer.BorderlessWindowedModeKey))
                    config.SetBooleanValue(renderer.WindowedModeSection, renderer.BorderlessWindowedModeKey, renderer.IsBorderlessWindowedModeKeyReversed);
                config.WriteIniFile();
            }
            UserINISettings.Instance.SaveSettings();
        }

        private int? ResolveSide(int selected, bool human, Random random)
        {
            bool[] disallowed = GetDisallowedSidesForGroup(human);
            IEnumerable<int> candidates;
            if (selected >= RandomSelectorCount && selected < SideCount + RandomSelectorCount)
                candidates = new[] { selected - RandomSelectorCount };
            else if (selected > 0 && selected < RandomSelectorCount)
                candidates = RandomSelectors[selected - 1];
            else
                candidates = Enumerable.Range(0, SideCount);

            int[] available = candidates.Where(side => side >= 0 && side < SideCount && !disallowed[side]).ToArray();

            return available.Length == 0 ? null : available[random.Next(available.Length)] + RandomSelectorCount;
        }

        protected override void OnGameLaunchCancelled()
        {
            base.OnGameLaunchCancelled();
            var battle = campaignWindow?.Campaign.State.PendingBattle;
            if (battle == null)
                return;

            battle.SkirmishLaunched = false;
            launchedBattleId = null;
            campaignWindow!.Save();
            campaignWindow.Refresh();
        }

        protected override void OnMatchCompleted(MatchStatistics statistics)
        {
            base.OnMatchCompleted(statistics);
            var campaign = campaignWindow?.Campaign;
            var battle = campaign?.State.PendingBattle;
            if (battle == null || battle.Id != launchedBattleId)
                return;

            launchedBattleId = null;
            if (statistics == null || !ConquestSkirmish.TryResolve(campaign!, statistics))
            {
                battle.SkirmishLaunched = false;
                string logPath = SafePath.CombineFilePath(ProgramConstants.GamePath, ClientConfiguration.Instance.StatisticsLogFileName);
                string help = File.Exists(logPath)
                    ? "No reliable battle result was found. Retry the battle or choose Auto Combat; campaign progress is preserved.".L10N("Client:Conquest:UnresolvedBattleHelp")
                    : "The game did not write its result log. Use a game build with result logging, then retry, or choose Auto Combat. Campaign progress is preserved.".L10N("Client:Conquest:MissingResultLogHelp");
                XNAMessageBox.Show(WindowManager, "Battle unresolved".L10N("Client:Conquest:UnresolvedBattle"), help);
            }

            campaignWindow!.Save();
            campaignWindow.Refresh();
        }

        protected override void ApplyAdditionalMapCode(IniFile mapIni, PlayerHouseInfo[] houseInfos)
        {
            base.ApplyAdditionalMapCode(mapIni, houseInfos);
            var battle = campaignWindow?.Campaign.State.PendingBattle;
            if (battle == null || !battle.SkirmishLaunched)
                return;

            battleRules?.ApplyMap(mapIni, battle);
            if (houseInfos.Length >= 2)
            {
                ConquestStartingForces.Apply(mapIni, campaignWindow!.Campaign.State.Settings, battle,
                    houseInfos[0].StartingWaypoint, houseInfos[1].StartingWaypoint,
                    houseInfos[0].InternalSideIndex, houseInfos[1].InternalSideIndex,
                    id => battleRules?.ResolvePlayerType(id) ?? id,
                    battleConfiguration?.LoadTerrain(ProgramConstants.GamePath, mapIni.GetStringValue("Map", "Theater", string.Empty)),
                    ground => battleRules?.CanBuildOn(ground) ?? true);
            }
            ApplyPerkCode(mapIni, battle, houseInfos, false);

            // Mod-owned rules avoid assuming an engine-specific unit, country, or house tag.
            var rules = LoadBattleConfiguration();
            int playerPower = battle.Attacker == ConquestFaction.Player ? battle.AttackPower : battle.DefensePower;
            int aiPower = battle.Attacker == ConquestFaction.Player ? battle.DefensePower : battle.AttackPower;
            foreach (string sectionName in rules.GetSections())
            {
                if (sectionName == "Conquest" || sectionName.StartsWith("ConquestPerk:", StringComparison.Ordinal))
                    continue;

                var section = rules.GetSection(sectionName);
                foreach (var entry in section.Keys)
                {
                    string value = entry.Value.Replace("{PlayerPower}", playerPower.ToString(CultureInfo.InvariantCulture))
                        .Replace("{AIPower}", aiPower.ToString(CultureInfo.InvariantCulture))
                        .Replace("{PlayerMultiplier}", ConquestBattleRules.PlayerMultiplier(battle).ToString("0.00", CultureInfo.InvariantCulture))
                        .Replace("{AIMultiplier}", ConquestBattleRules.AIMultiplier(battle).ToString("0.00", CultureInfo.InvariantCulture));
                    foreach (ConquestStat stat in Enum.GetValues(typeof(ConquestStat)))
                    {
                        value = value.Replace("{Player" + stat + "}", battle.PlayerStats.GetValueOrDefault(stat).ToString(CultureInfo.InvariantCulture))
                            .Replace("{AI" + stat + "}", battle.AIStats.GetValueOrDefault(stat).ToString(CultureInfo.InvariantCulture));
                    }
                    mapIni.SetStringValue(section.SectionName, entry.Key, value);
                }
            }
        }

        private void ApplyPerkCode(IniFile destination, ConquestBattle battle, PlayerHouseInfo[]? houses, bool spawn)
        {
            var settings = campaignWindow!.Campaign.State.Settings;
            int playerSide = houses?.Length >= 2 ? houses[0].InternalSideIndex :
                new PlayerHouseInfo { SideIndex = settings.PlayerSide - RandomSelectorCount }.InternalSideIndex;
            int aiSide = houses?.Length >= 2 ? houses[1].InternalSideIndex :
                new PlayerHouseInfo { SideIndex = settings.AISide - RandomSelectorCount }.InternalSideIndex;

            ConquestPerkRules.Apply(destination, settings, battle, ProgramConstants.GamePath,
                spawn, houses?.Length >= 2 ? houses[0].StartingWaypoint : -1, houses?.Length >= 2 ? houses[1].StartingWaypoint : -1,
                id => battleRules?.ResolvePlayerType(id) ?? id, playerSide, aiSide);
        }

        private static IniFile LoadBattleConfiguration()
        {
            string path = SafePath.CombineFilePath(ProgramConstants.GetResourcePath(), "ConquestBattle.ini");
            if (!File.Exists(path))
                path = SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), "ConquestBattle.ini");

            return File.Exists(path) ? new IniFile(path) : new IniFile();
        }

        protected override void ApplyAdditionalSpawnCode(IniFile iniFile)
        {
            base.ApplyAdditionalSpawnCode(iniFile);
            var battle = campaignWindow?.Campaign.State.PendingBattle;
            if (battle != null && battle.SkirmishLaunched)
            {
                var configuration = battleConfiguration ?? ConquestBattleConfiguration.Read(ClientConfiguration.Instance.ClientGameType, LoadBattleConfiguration());
                battleRules = new ConquestBattleRules(configuration.LoadRules(ProgramConstants.GamePath),
                    ClientConfiguration.Instance.ClientGameType, campaignWindow!.Campaign.State.Settings.Difficulty,
                    iniFile.GetIntValue("Settings", "Side", 0), iniFile.GetIntValue("HouseCountries", "Multi2", 0), configuration);
                iniFile.SetIntValue("Settings", "GameID", battle.SkirmishGameId);
                battleRules.ApplySpawn(iniFile);
                ApplyPerkCode(iniFile, battle, null, true);
            }
        }

        public override string GetSwitchName() => "Conquest".L10N("Client:Conquest:LobbyTitle");
    }
}
