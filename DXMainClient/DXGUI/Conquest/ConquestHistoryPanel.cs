#nullable enable

using System;
using System.Collections.Generic;

using ClientCore.Extensions;

using ClientGUI;

using DTAClient.Domain.Conquest;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Conquest
{
    /// <summary>
    /// Persistent history and animation controls. Collapsing hides only the history body;
    /// animation controls remain usable while the map gains the freed space.
    /// </summary>
    internal sealed class ConquestHistoryPanel : XNAPanel
    {
        private readonly ConquestCampaign campaign;
        private readonly ConquestHistoryView history;
        private readonly List<XNAClientButton> filters = new();
        private readonly Dictionary<int, XNAClientButton> speeds = new();
        private readonly XNAClientButton collapse;
        private readonly XNAClientButton latest;
        private int expandedHeight;
        private bool changingLayout;
        private readonly Action<bool> resizeMap;

        public ConquestHistoryPanel(WindowManager windowManager, ConquestCampaign campaign, int width, int height,
            Action<int> focusTerritory, Action<int> changeSpeed, Action<bool> resizeMap) : base(windowManager)
        {
            this.campaign = campaign;
            this.resizeMap = resizeMap;
            expandedHeight = height;
            ClientRectangle = new Rectangle(0, 0, width, height);
            DrawOrder = 2;
            UpdateOrder = 2;
            history = new ConquestHistoryView(windowManager, campaign.State.History, focusTerritory)
            {
                ClientRectangle = new Rectangle(6, 68, width - 12, height - 106)
            };
            AddChild(history);

            string[] filterNames =
            {
                "All Actions".L10N("Client:Conquest:History:AllActions"),
                "Player Actions".L10N("Client:Conquest:History:PlayerActions"),
                "AI Actions".L10N("Client:Conquest:History:AIActions")
            };
            for (int i = 0; i < filterNames.Length; i++)
            {
                int index = i;
                var button = Button("btnFilter" + (ConquestHistoryFilter)index, filterNames[i], 8 + i * 137, 36, 133, () =>
                {
                    history.Filter = (ConquestHistoryFilter)index;
                    for (int j = 0; j < filters.Count; j++)
                        filters[j].Enabled = j != index;
                });
                button.FontIndex = 0;
                filters.Add(button);
            }

            filters[0].Enabled = false;
            collapse = Button("btnToggleHistory", "Hide".L10N("Client:Conquest:History:Hide"), width - 83, 5, 75, ToggleCollapsed);
            collapse.FontIndex = 0;

            string[] speedNames =
            {
                "Normal".L10N("Client:Conquest:History:NormalSpeed"), "2×", "4×",
                "Instant".L10N("Client:Conquest:History:InstantSpeed")
            };
            int[] speedValues = { 1, 2, 4, 0 };
            for (int i = 0; i < speedValues.Length; i++)
            {
                int speed = speedValues[i];
                var button = Button("btnSpeed" + speed, speedNames[i], 8 + i * 80, height - 29, 75, () =>
                {
                    campaign.State.AnimationSpeed = speed;
                    foreach (var entry in speeds)
                        entry.Value.Enabled = entry.Key != speed;

                    changeSpeed(speed);
                });
                button.FontIndex = 0;
                button.ToolTipText = speed == 0
                    ? "Skip AI animations. Battles requiring your input always pause the AI.".L10N("Client:Conquest:History:InstantHelp")
                    : "Set AI animation speed; gameplay stays the same.".L10N("Client:Conquest:History:SpeedHelp");
                button.Enabled = speed != campaign.State.AnimationSpeed;
                speeds.Add(speed, button);
            }

            latest = Button("btnLatest", "Latest".L10N("Client:Conquest:History:Latest"), width - 83, height - 29, 75, history.JumpToLatest);
            latest.FontIndex = 0;
            ClientRectangleUpdated += (_, _) => LayoutContent();
        }

        public bool Collapsed { get; private set; }

        public void ToggleCollapsed()
        {
            if (!Collapsed)
                expandedHeight = Height;
            changingLayout = true;
            Collapsed = !Collapsed;
            Height = Collapsed ? 70 : expandedHeight;
            changingLayout = false;
            history.Visible = history.Enabled = !Collapsed;
            foreach (var filter in filters)
            {
                filter.Visible = !Collapsed;
                filter.Enabled = !Collapsed && (int)history.Filter != filters.IndexOf(filter);
            }
            collapse.Text = Collapsed ? "Show".L10N("Client:Conquest:History:Show") : "Hide".L10N("Client:Conquest:History:Hide");
            LayoutContent();
            resizeMap(Collapsed);
        }

        private void LayoutContent()
        {
            if (changingLayout)
                return;
            changingLayout = true;
            if (Collapsed && Height != 70)
            {
                expandedHeight = Height;
                Height = 70;
            }
            else if (!Collapsed)
            {
                expandedHeight = Height;
            }
            history.ClientRectangle = new Rectangle(6, 68, Math.Max(1, Width - 12), Math.Max(1, expandedHeight - 106));
            collapse.X = Width - collapse.Width - 8;
            int filterWidth = (Width - 24) / 3;
            for (int i = 0; i < filters.Count; i++)
                filters[i].ClientRectangle = new Rectangle(8 + i * (filterWidth + 4), 36, filterWidth, 23);
            int speedWidth = (Width - 32) / 5;
            int index = 0;
            foreach (var button in speeds.Values)
                button.ClientRectangle = new Rectangle(8 + index++ * (speedWidth + 4), Height - 29, speedWidth, 23);
            latest.ClientRectangle = new Rectangle(Width - speedWidth - 8, Height - 29, speedWidth, 23);
            changingLayout = false;
        }

        private XNAClientButton Button(string name, string text, int x, int y, int width, Action action)
        {
            var button = new ConquestButton(WindowManager)
            {
                Name = name,
                IdleTexture = AssetLoader.LoadTexture("133pxbtn.png"),
                HoverTexture = AssetLoader.LoadTexture("133pxbtn_c.png"),
                Text = text,
                ClientRectangle = new Rectangle(x, y, width, 23)
            };
            button.LeftClick += (_, _) => action();
            AddChild(button);

            return button;
        }

        public override void Draw(GameTime gameTime)
        {
            FillControlArea(new Color(17, 25, 35));
            DrawRectangle(new Rectangle(0, 0, Width, Height), Color.SlateGray);
            DrawString(string.Format("HISTORY — ROUND {0}".L10N("Client:Conquest:History:Title"), campaign.State.TurnNumber),
                0, new Vector2(8, 10), Color.Gold);
            base.Draw(gameTime);
        }
    }

    /// <summary>
    /// Virtualized rows: O(visible rows) drawing, O(1) scroll and filter changes.
    /// Scrolling back suspends tail following until Latest is clicked or the bottom is reached.
    /// </summary>
    internal sealed class ConquestHistoryView : ConquestScrollPanel
    {
        private const int RowHeight = 66;
        private readonly ConquestHistory history;
        private readonly Action<int> focusTerritory;
        private readonly HistoryRows rows;
        private readonly Dictionary<long, string> descriptions = new();
        private ConquestHistoryFilter filter;
        private int knownCount = -1;
        private bool followLatest = true;
        private bool updatingExtent;
        private long actualHeight;
        private long actualViewTop;

        public ConquestHistoryView(WindowManager windowManager, ConquestHistory history, Action<int> focusTerritory)
            : base(windowManager)
        {
            this.history = history;
            this.focusTerritory = focusTerritory;
            Name = "spHistory";
            ScrollStep = RowHeight;
            rows = new HistoryRows(windowManager, this) { Name = "HistoryRows" };
            Content.AddChild(rows);
            ViewPositionChanged += (_, _) =>
            {
                if (!updatingExtent)
                {
                    actualViewTop = MaximumTop == 0 ? 0 : (long)Math.Round(CurrentViewPosition.Y * (double)ActualMaximumTop / MaximumTop);
                    followLatest = CurrentViewPosition.Y >= MaximumTop;
                }
            };
            ClientRectangleUpdated += (_, _) => RefreshExtent();
            RefreshExtent();
        }

        public ConquestHistoryFilter Filter
        {
            get => filter;
            set
            {
                filter = value;
                knownCount = -1;
                followLatest = true;
                RefreshExtent();
            }
        }

        private IReadOnlyList<ConquestEvent> Events => history.GetEvents(filter);
        private const int MaximumScrollExtent = 100000;
        private int MaximumTop => Math.Max(0, rows.Height - ViewSize.Y);
        private long ActualMaximumTop => Math.Max(0, actualHeight - ViewSize.Y);
        private long ActualViewTop => actualViewTop;

        public new void ScrollToTop()
        {
            CurrentViewPosition = Point.Zero;
            actualViewTop = 0;
            followLatest = MaximumTop == 0;
        }

        public void JumpToLatest()
        {
            followLatest = true;
            RefreshExtent();
        }

        private void RefreshExtent()
        {
            updatingExtent = true;
            descriptions.Clear();
            actualHeight = (long)Events.Count * RowHeight;
            // Keep the native scroll extent bounded. The row surface maps this
            // viewport onto the full history without huge textures or integer ranges.
            int height = (int)Math.Max(1, Math.Min(MaximumScrollExtent, (long)Events.Count * RowHeight));
            rows.ClientRectangle = new Rectangle(0, 0, ContentWidth, height);
            RefreshContentSize();
            ScrollStep = ActualMaximumTop == 0 ? RowHeight : Math.Max(1, (int)Math.Round(RowHeight * (double)MaximumTop / ActualMaximumTop));
            actualViewTop = followLatest ? ActualMaximumTop : Math.Min(actualViewTop, ActualMaximumTop);
            CurrentViewPosition = new Point(0, ActualMaximumTop == 0 ? 0 : (int)Math.Round(actualViewTop * (double)MaximumTop / ActualMaximumTop));
            updatingExtent = false;
        }

        public override void Update(GameTime gameTime)
        {
            if (knownCount != Events.Count)
            {
                knownCount = Events.Count;
                RefreshExtent();
            }
            if (GetWindowRectangle().Contains(Cursor.Location) && WindowManager.HasFocus)
            {
                if (Keyboard.PressedKeys.Contains(Keys.Home))
                    ScrollToTop();
                if (Keyboard.PressedKeys.Contains(Keys.End))
                    JumpToLatest();
                int page = ActualMaximumTop == 0 ? ViewSize.Y : Math.Max(1, (int)Math.Round(ViewSize.Y * (double)MaximumTop / ActualMaximumTop));
                if (Keyboard.PressedKeys.Contains(Keys.PageUp))
                    CurrentViewPosition = new Point(0, CurrentViewPosition.Y - page);
                if (Keyboard.PressedKeys.Contains(Keys.PageDown))
                    CurrentViewPosition = new Point(0, CurrentViewPosition.Y + page);
            }
            base.Update(gameTime);
        }

        private string Description(ConquestEvent action)
        {
            if (descriptions.TryGetValue(action.Id, out string? description))
                return description;

            if (descriptions.Count > 256)
                descriptions.Clear();
            description = ConquestText.Describe(action);
            int width = Math.Max(1, ContentWidth - 18);
            if (Renderer.GetTextDimensions(description, 0).X > width)
            {
                int end = description.Length;
                while (end > 0 && Renderer.GetTextDimensions(description.Substring(0, end) + "...", 0).X > width)
                    end--;
                if (end > 0 && char.IsHighSurrogate(description[end - 1]))
                    end--;
                description = description.Substring(0, end) + "...";
            }
            descriptions[action.Id] = description;

            return description;
        }

        private sealed class HistoryRows : XNAControl
        {
            private readonly ConquestHistoryView owner;

            public HistoryRows(WindowManager windowManager, ConquestHistoryView owner) : base(windowManager)
            {
                this.owner = owner;
            }

            public override void OnLeftClick(InputEventArgs inputEventArgs)
            {
                var point = GetCursorPoint();
                int localY = point.Y - owner.CurrentViewPosition.Y;
                int index = (int)((owner.ActualViewTop + localY) / RowHeight);
                if (localY >= 0 && localY < owner.ViewSize.Y && index < owner.Events.Count && point.X >= 0 && point.X < Width)
                {
                    var action = owner.Events[index];
                    int? territory = point.X < Width / 2
                        ? action.SourceTerritoryId ?? action.TargetTerritoryId
                        : action.TargetTerritoryId ?? action.SourceTerritoryId;
                    if (territory.HasValue)
                        owner.focusTerritory(territory.Value);
                }
                inputEventArgs.Handled = true;
                base.OnLeftClick(inputEventArgs);
            }

            public override void Draw(GameTime gameTime)
            {
                long top = owner.ActualViewTop;
                int first = (int)(top / RowHeight);
                int end = (int)Math.Min(owner.Events.Count, (top + owner.ViewSize.Y) / RowHeight + 1);
                for (int index = first; index < end; index++)
                {
                    var action = owner.Events[index];
                    int y = owner.CurrentViewPosition.Y + (int)((long)index * RowHeight - top);
                    bool important = ConquestText.Important(action);
                    var factionColor = action.FactionId == ConquestFaction.Player ? Color.LightSkyBlue : Color.Salmon;
                    if (important)
                        FillRectangle(new Rectangle(0, y, Width, RowHeight - 2), new Color(44, 36, 23));
                    FillRectangle(new Rectangle(0, y, 3, RowHeight - 2), factionColor);
                    DrawString(string.Format("ROUND {0} | {1}".L10N("Client:Conquest:History:RowTitle"), action.TurnNumber, ConquestText.Faction(action.FactionId)),
                        0, new Vector2(9, y + 3), factionColor);
                    DrawString(owner.Description(action), 0, new Vector2(9, y + 22), important ? Color.Gold : Color.White);
                    if (action.SourceTerritoryId.HasValue)
                        DrawLink(action.SourceTerritoryId.Value, 9, y + 43, factionColor);
                    if (action.TargetTerritoryId.HasValue)
                        DrawLink(action.TargetTerritoryId.Value, action.SourceTerritoryId.HasValue ? Width / 2 : 9, y + 43, factionColor);
                }
                base.Draw(gameTime);
            }

            private void DrawLink(int id, int x, int y, Color color)
            {
                string name = ConquestText.Territory(id);
                DrawString(name, 0, new Vector2(x, y), color);
                DrawLine(new Vector2(x, y + 15), new Vector2(x + Math.Min(Width / 2 - 12, Renderer.GetTextDimensions(name, 0).X), y + 15), color);
            }
        }
    }
}