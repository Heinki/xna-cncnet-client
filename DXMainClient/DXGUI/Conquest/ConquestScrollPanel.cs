#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Conquest
{
    /// <summary>Standard scroll-panel composition, shared with the client's other scroll views.</summary>
    internal class ConquestScrollPanel : XNAScrollPanel
    {
        public ConquestScrollPanel(WindowManager windowManager) : base(windowManager)
        {
            AllowScroll = (false, true);
            DrawBorders = false;
        }

        private Texture2D? ownedCorner;

        public override void Initialize()
        {
            base.Initialize();
            if (!AssetLoader.AssetExists(CORNER_TEXTURE_FILENAME))
                ownedCorner = CornerPanel.BackgroundTexture;
        }

        public XNAPanel Content => ContentPanel;
        public int ContentWidth => Math.Max(1, Width - VerticalScrollBar.ScrollWidth - 4);

        public void RefreshContentSize()
        {
            RecalculateContentSize();
            CurrentViewPosition = CurrentViewPosition;
        }

        public override void Kill()
        {
            RenderTarget?.Dispose();
            if (Initialized)
                base.Kill();
            ownedCorner?.Dispose();
        }
    }

    /// <summary>
    /// Standard scrolling and clipping with one text surface. Only visible lines are
    /// drawn; growing the content never allocates a content-sized render target.
    /// </summary>
    internal sealed class ConquestTextScrollPanel : ConquestScrollPanel
    {
        private readonly TextSurface surface;
        private readonly int lineHeight;
        private List<(string Text, Color Color)> source = new();
        private readonly List<(string Text, Color Color)> lines = new();

        public ConquestTextScrollPanel(WindowManager windowManager, int lineHeight) : base(windowManager)
        {
            this.lineHeight = lineHeight;
            ScrollStep = lineHeight * 3;
            surface = new TextSurface(windowManager, this);
            Content.AddChild(surface);
            NameChanged += (_, _) => surface.Name = Name + "_Text";
            ClientRectangleUpdated += (_, _) => Rebuild();
        }

        public void SetLines(IEnumerable<(string Text, Color Color)> value)
        {
            var updated = value.ToList();
            if (source.SequenceEqual(updated))
                return;

            source = updated;
            Rebuild();
        }

        private void Rebuild()
        {
            lines.Clear();
            foreach (var line in source)
            {
                foreach (string text in Renderer.GetFixedTextLines(line.Text, 0, Math.Max(1, ContentWidth - 12)))
                    lines.Add((text, line.Color));
            }
            surface.ClientRectangle = new Rectangle(0, 0, ContentWidth, Math.Max(1, lines.Count * lineHeight + 8));
            RefreshContentSize();
        }

        private sealed class TextSurface : XNAControl
        {
            private readonly ConquestTextScrollPanel owner;

            public TextSurface(WindowManager windowManager, ConquestTextScrollPanel owner) : base(windowManager)
            {
                this.owner = owner;
            }

            public override void Draw(GameTime gameTime)
            {
                int first = owner.CurrentViewPosition.Y / owner.lineHeight;
                int end = Math.Min(owner.lines.Count, (owner.CurrentViewPosition.Y + owner.ViewSize.Y) / owner.lineHeight + 1);
                for (int i = first; i < end; i++)
                    DrawString(owner.lines[i].Text, 0, new Vector2(6, 4 + i * owner.lineHeight), owner.lines[i].Color);

                base.Draw(gameTime);
            }
        }
    }
}