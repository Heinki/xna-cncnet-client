#nullable enable

using System;

using ClientCore.Extensions;

using DTAClient.Domain.Conquest;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Conquest
{
    /// <summary>
    /// Strategic route network with explicit army selection and legal destination previews.
    /// Rendering and hit testing use the same saved node coordinates and gameplay routes.
    /// </summary>
    internal sealed class ConquestMapPanel : XNAControl
    {
        private readonly Texture2D disc;
        private readonly ConquestArmy?[] armiesByTerritory;
        private readonly bool[] destinations;
        private readonly ConquestArmyAppearance playerAppearance;
        private readonly ConquestArmyAppearance aiAppearance;
        private ConquestFaction animationFaction;
        private Vector2 camera;
        private float zoom = 1.0f;
        private Point previousCursor;
        private bool dragging;
        private int hovered = -1;
        private int animationSource = -1;
        private int animationTarget = -1;
        private double animationTime;
        private double animationDuration;

        public ConquestMapPanel(WindowManager windowManager, ConquestCampaign campaign,
            ConquestArmyAppearance playerAppearance, ConquestArmyAppearance aiAppearance) : base(windowManager)
        {
            Campaign = campaign;
            armiesByTerritory = new ConquestArmy?[campaign.State.Territories.Count];
            destinations = new bool[campaign.State.Territories.Count];
            this.playerAppearance = playerAppearance;
            this.aiAppearance = aiAppearance;
            DrawMode = ControlDrawMode.UNIQUE_RENDER_TARGET;
            HandlesDragging = true;
            disc = new Texture2D(windowManager.GraphicsDevice, 64, 64);
            var pixels = new Color[64 * 64];
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                    pixels[y * 64 + x] = (x - 31.5f) * (x - 31.5f) + (y - 31.5f) * (y - 31.5f) <= 31 * 31 ? Color.White : Color.Transparent;
            }

            disc.SetData(pixels);
        }

        public ConquestCampaign Campaign { get; }
        public int SelectedTerritory { get; set; }
        public int? SelectedArmyTerritory { get; set; }
        public bool CanIssueOrders { get; set; } = true;
        public Action<int>? TerritorySelected { get; set; }
        public Action<int>? TerritoryHovered { get; set; }
        public Action? SelectionCleared { get; set; }
        public bool Animating => animationTime < animationDuration;

        public void ShowOverview()
        {
            float extent = ConquestMapLayout.Extent(Campaign.State);
            zoom = MathHelper.Clamp(Math.Min((Width - 60) / extent, (Height - 60) / extent), 0.05f, 1.5f);
            camera = new Vector2(extent / 2, extent / 2);
        }

        public void FocusTerritory(int id)
        {
            if (id < 0 || id >= Campaign.State.Territories.Count)
                return;

            var territory = Campaign.State.Territories[id];
            camera = new Vector2(territory.MapX, territory.MapY);
            SelectedTerritory = id;
            ClampCamera();
        }

        public void FocusBattle(int id)
        {
            zoom = Math.Max(zoom, 0.8f);
            FocusTerritory(id);
        }

        public void Zoom(float multiplier)
        {
            zoom = MathHelper.Clamp(zoom * multiplier, 0.05f, 2.0f);
            ClampCamera();
        }

        public void Pan(int x, int y)
        {
            camera += new Vector2(x, y) / zoom;
            ClampCamera();
        }

        public void Animate(ConquestEvent action)
        {
            if (action.EventType != ConquestEventType.ArmyMoved && action.EventType != ConquestEventType.TerritoryAttacked)
                return;

            animationSource = action.SourceTerritoryId ?? -1;
            animationFaction = action.FactionId;
            animationTarget = action.TargetTerritoryId ?? -1;
            animationTime = 0;
            animationDuration = Campaign.State.AnimationSpeed == 0 ? 0 : 0.8 / Campaign.State.AnimationSpeed;
            if (action.TargetTerritoryId.HasValue)
                FocusTerritory(action.TargetTerritoryId.Value);
        }

        public void ChangeAnimationSpeed(int speed)
        {
            double progress = animationDuration > 0 ? animationTime / animationDuration : 1;
            animationDuration = speed == 0 ? 0 : 0.8 / speed;
            animationTime = progress * animationDuration;
        }

        public Vector2 NodeScreenPosition(int id)
        {
            var territory = Campaign.State.Territories[id];

            return (new Vector2(territory.MapX, territory.MapY) - camera) * zoom + new Vector2(Width / 2f, Height / 2f);
        }

        private void ClampCamera()
        {
            float extent = ConquestMapLayout.Extent(Campaign.State);
            camera.X = MathHelper.Clamp(camera.X, 0, extent);
            camera.Y = MathHelper.Clamp(camera.Y, 0, extent);
        }

        private int HitTest(Point cursor)
        {
            if (!new Rectangle(0, 0, Width, Height).Contains(cursor))
                return -1;

            float nearest = 22 * 22;
            int result = -1;
            foreach (var territory in Campaign.State.Territories)
            {
                float distance = Vector2.DistanceSquared(NodeScreenPosition(territory.Id), new Vector2(cursor.X, cursor.Y));
                if (distance < nearest)
                {
                    nearest = distance;
                    result = territory.Id;
                }
            }

            return result;
        }

        public override void OnLeftClick(InputEventArgs inputEventArgs)
        {
            int id = HitTest(GetCursorPoint());
            if (id >= 0)
                TerritorySelected?.Invoke(id);
            else
                SelectionCleared?.Invoke();

            inputEventArgs.Handled = true;
            base.OnLeftClick(inputEventArgs);
        }

        public override void OnMouseScrolled(InputEventArgs inputEventArgs)
        {
            var point = GetCursorPoint();
            var offset = new Vector2(point.X - Width / 2f, point.Y - Height / 2f);
            var world = camera + offset / zoom;
            Zoom((float)Math.Pow(1.08, Cursor.ScrollWheelValue));
            camera = world - offset / zoom;
            ClampCamera();
            inputEventArgs.Handled = true;
            base.OnMouseScrolled(inputEventArgs);
        }

        public override void Update(GameTime gameTime)
        {
            animationTime += gameTime.ElapsedGameTime.TotalSeconds;
            var point = GetCursorPoint();
            bool inside = new Rectangle(0, 0, Width, Height).Contains(point);
            int nextHovered = HitTest(point);
            if (nextHovered != hovered)
            {
                hovered = nextHovered;
                TerritoryHovered?.Invoke(hovered);
            }
            bool held = Cursor.MiddleDown || Cursor.RightDown;
            if (held && (dragging || inside))
            {
                if (dragging)
                    camera -= new Vector2(point.X - previousCursor.X, point.Y - previousCursor.Y) / zoom;

                dragging = true;
            }
            else
            {
                dragging = false;
            }

            previousCursor = point;
            if (inside && WindowManager.HasFocus)
            {
                float distance = (float)gameTime.ElapsedGameTime.TotalSeconds * 400 / zoom;
                if (Keyboard.IsKeyHeldDown(Keys.Left))
                    camera.X -= distance;

                if (Keyboard.IsKeyHeldDown(Keys.Right))
                    camera.X += distance;

                if (Keyboard.IsKeyHeldDown(Keys.Up))
                    camera.Y -= distance;

                if (Keyboard.IsKeyHeldDown(Keys.Down))
                    camera.Y += distance;

                if (Keyboard.PressedKeys.Contains(Keys.Escape))
                    SelectionCleared?.Invoke();
            }

            ClampCamera();
            base.Update(gameTime);
        }

        private bool IsDestination(int id) => destinations[id];

        private void RefreshDrawState()
        {
            Array.Clear(armiesByTerritory, 0, armiesByTerritory.Length);
            Array.Clear(destinations, 0, destinations.Length);
            foreach (var army in Campaign.State.Armies)
                armiesByTerritory[army.TerritoryId] = army;

            if (!CanIssueOrders || !SelectedArmyTerritory.HasValue)
                return;

            int source = SelectedArmyTerritory.Value;
            foreach (var edge in Campaign.State.Connections)
            {
                if (edge.Source != source && edge.Target != source)
                    continue;
                int target = edge.Source == source ? edge.Target : edge.Source;
                destinations[target] = Campaign.CanMove(ConquestFaction.Player, source, target);
            }
        }

        private static Color FactionColor(ConquestFaction faction) => faction switch
        {
            ConquestFaction.Player => new Color(65, 192, 255),
            ConquestFaction.AI => new Color(255, 94, 83),
            _ => new Color(135, 152, 166)
        };

        private void Circle(Vector2 point, int radius, Color color) =>
            DrawTexture(disc, new Rectangle((int)point.X - radius, (int)point.Y - radius, radius * 2, radius * 2), color);

        private void ArmyMarker(Vector2 point, Color color, float scale, bool used, ConquestFaction faction)
        {
            var appearance = faction == ConquestFaction.Player ? playerAppearance : aiAppearance;
            int x = (int)point.X;
            int y = (int)point.Y;
            int unit = Math.Max(1, (int)(3 * scale));
            if (appearance.Unit != null)
            {
                float imageScale = unit * 12f / Math.Max(appearance.Unit.Width, appearance.Unit.Height);
                int width = Math.Max(1, (int)(appearance.Unit.Width * imageScale));
                int height = Math.Max(1, (int)(appearance.Unit.Height * imageScale));
                DrawTexture(appearance.Unit, new Rectangle(x - width / 2, y - height / 2, width, height),
                    Color.Lerp(Color.White, color, 0.25f) * (used ? 0.6f : 1));
            }
            else if (appearance.Style == "yuri")
            {
                DrawTexture(disc, new Rectangle(x - unit * 5, y - unit * 2, unit * 10, unit * 5), color);
                DrawTexture(disc, new Rectangle(x - unit * 2, y - unit * 3, unit * 4, unit * 4), Color.Lerp(color, Color.White, 0.5f));
            }
            else if (appearance.Style == "nod")
            {
                // A wheeled scout distinguishes Nod from the tracked tank silhouettes.
                for (int wheel = -2; wheel <= 2; wheel += 4)
                {
                    FillRectangle(new Rectangle(x - unit * 4, y + wheel * unit, unit * 2, unit * 2), color * 0.7f);
                    FillRectangle(new Rectangle(x + unit * 2, y + wheel * unit, unit * 2, unit * 2), color * 0.7f);
                }

                FillRectangle(new Rectangle(x - unit * 2, y - unit * 3, unit * 4, unit * 7), color);
                FillRectangle(new Rectangle(x - unit, y - unit * 2, unit * 2, unit * 2), Color.Lerp(color, Color.White, 0.5f));
                FillRectangle(new Rectangle(x, y - unit * 5, unit, unit * 4), color);
            }
            else
            {
                bool heavy = appearance.Style == "gdi" || appearance.Style == "soviet" || appearance.Style == "soviets";
                FillRectangle(new Rectangle(x - unit * 4, y - unit * 2, unit * 2, unit * 6), color * 0.65f);
                FillRectangle(new Rectangle(x + unit * 2, y - unit * 2, unit * 2, unit * 6), color * 0.65f);
                FillRectangle(new Rectangle(x - unit * 2, y - unit * 3, unit * 4, unit * (heavy ? 8 : 6)), color);
                FillRectangle(new Rectangle(x - unit, y - unit * 2, unit * 2, unit * 3), Color.Lerp(color, Color.White, 0.5f));
                FillRectangle(new Rectangle(x, y - unit * 6, unit, unit * 5), Color.Lerp(color, Color.White, 0.65f));
                if (appearance.Style == "gdi")
                    FillRectangle(new Rectangle(x - unit * 2, y - unit * 6, unit, unit * 5), Color.Lerp(color, Color.White, 0.65f));
            }

            if (used)
                DrawLine(new Vector2(x - unit * 4, y + unit * 4), new Vector2(x + unit * 4, y - unit * 4), Color.Gray, 2);
        }

        public override void Draw(GameTime gameTime)
        {
            RefreshDrawState();
            FillControlArea(new Color(8, 18, 25));
            foreach (var edge in Campaign.State.Connections)
            {
                int target = SelectedArmyTerritory == edge.Source ? edge.Target : edge.Source;
                bool route = (SelectedArmyTerritory == edge.Source || SelectedArmyTerritory == edge.Target) && IsDestination(target);
                var color = route ? (Campaign.State.Territories[target].Owner == ConquestFaction.Player ? Color.LightGreen : Color.Orange) : new Color(48, 77, 91);
                var sourcePoint = NodeScreenPosition(edge.Source);
                var targetPoint = NodeScreenPosition(edge.Target);
                DrawLine(sourcePoint, targetPoint, color, route ? 3 : 1);
                if (route)
                {
                    var destination = NodeScreenPosition(target);
                    var origin = NodeScreenPosition(SelectedArmyTerritory!.Value);
                    var direction = Vector2.Normalize(destination - origin);
                    var end = destination - direction * 22;
                    var side = new Vector2(-direction.Y, direction.X);
                    DrawLine(end, end - direction * 10 + side * 5, color, 2);
                    DrawLine(end, end - direction * 10 - side * 5, color, 2);
                }
            }

            foreach (var territory in Campaign.State.Territories)
            {
                var point = NodeScreenPosition(territory.Id);
                if (point.X < -50 || point.X > Width + 50 || point.Y < -50 || point.Y > Height + 50)
                    continue;

                bool selectedArmy = SelectedArmyTerritory == territory.Id;
                bool destination = IsDestination(territory.Id);
                var color = FactionColor(territory.Owner);
                var army = armiesByTerritory[territory.Id];
                int radius = (int)MathHelper.Clamp(26 * zoom, army == null ? 5 : 11, 26);
                Color border = selectedArmy ? Color.Gold : destination
                    ? (territory.Owner == ConquestFaction.Player ? Color.LightGreen : Color.Orange)
                    : territory.Id == SelectedTerritory || territory.Id == hovered ? Color.White : color;
                Circle(point, radius + (selectedArmy || destination ? 5 : 2), border);
                Circle(point, radius, new Color(12, 32, 43));
                Circle(point, Math.Max(3, radius - 4), color * 0.24f);
                if (army != null)
                {
                    ArmyMarker(point, army.HasActed ? color * 0.65f : color, MathHelper.Clamp(zoom, 0.75f, 1), army.HasActed, army.Faction);
                }

                if (territory.Id == hovered && destination)
                {
                    string action = territory.Owner == ConquestFaction.Player
                        ? "MOVE".L10N("Client:Conquest:MovePreview") : "ATTACK".L10N("Client:Conquest:AttackPreview");
                    DrawString(action, 0, new Vector2(point.X + radius + 8, point.Y - 6), border);
                }
            }

            if (Animating && animationSource >= 0 && animationTarget >= 0)
            {
                var source = NodeScreenPosition(animationSource);
                var target = NodeScreenPosition(animationTarget);
                ArmyMarker(Vector2.Lerp(source, target, (float)(animationTime / animationDuration)), Color.Gold, 1, false, animationFaction);
            }

            DrawString("Blue: you | Red: enemy\nGold ring: selected army | Slash: already moved".L10N("Client:Conquest:ArmyMapLegend"),
                0, new Vector2(10, Height - 36), Color.LightGray, 0.85f);
            base.Draw(gameTime);
        }

        public override void Kill()
        {
            disc.Dispose();
            RenderTarget?.Dispose();
            base.Kill();
        }
    }
}