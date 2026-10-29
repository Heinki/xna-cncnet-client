#nullable enable

using System;
using System.IO;
using System.Linq;

using ClientGUI;

using FontStashSharp;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Rampastring.XNAUI;
using Rampastring.XNAUI.FontManagement;

namespace DTAClient.DXGUI.Conquest
{
    /// <summary>
    /// A text menu entry with the theme's cyan glow, rather than an in-dialog metal button.
    /// Themes may still supply their own idle/hover textures through MainMenu.ini.
    /// </summary>
    internal sealed class ConquestMenuButton : XNAClientButton
    {
        private readonly Texture2D transparent;
        private readonly FontSystem? menuFont;
        private readonly IFont? captionFont;
        private int menuFontIndex;
        private RenderTarget2D? idleCaption;
        private RenderTarget2D? hoverCaption;
        private string cachedText = string.Empty;
        private Point cachedSize;

        public ConquestMenuButton(WindowManager windowManager) : base(windowManager)
        {
            string? fontPath = AssetLoader.AssetSearchPaths.Select(p => Path.Combine(p, "Fonts", "texgyreadventor-bold.otf")).FirstOrDefault(File.Exists);
            using var fontStream = fontPath != null ? File.OpenRead(fontPath) :
                typeof(ConquestMenuButton).Assembly.GetManifestResourceStream("DTAClient.ConquestMenu.otf");
            if (fontStream != null)
            {
                using var bytes = new MemoryStream();
                fontStream.CopyTo(bytes);
                menuFont = new FontSystem(new FontSystemSettings { UseEmToPixelsScale = true, TextureWidth = 256, TextureHeight = 128 });
                menuFont.AddFont(bytes.ToArray());
                menuFontIndex = Renderer.GetFontList().Count;
                captionFont = new TTFFontWrapper(menuFont.GetFont(28));
                Renderer.GetFontList().Add(captionFont);
            }

            transparent = AssetLoader.CreateTexture(Color.Transparent, 300, 72);
            IdleTexture = transparent;
            HoverTexture = transparent;
            ClientRectangle = new Rectangle(0, 0, 300, 72);
            HoverSoundEffect = new EnhancedSoundEffect("MainMenu/button.wav");
        }

        public override void Draw(GameTime gameTime)
        {
            if (IdleTexture != transparent)
            {
                base.Draw(gameTime);
                return;
            }

            var fonts = Renderer.GetFontList();
            if (captionFont != null && (menuFontIndex >= fonts.Count || !ReferenceEquals(fonts[menuFontIndex], captionFont)))
            {
                // Display-scale changes reload the shared font list.
                menuFontIndex = fonts.Count;
                fonts.Add(captionFont);
                ClearCaptionCache();
            }

            if (Width < 1 || Height < 1)
                return;

            var size = new Point(Math.Min(512, Width), Math.Min(128, Height));
            if (idleCaption == null || hoverCaption == null || cachedText != Text || cachedSize != size)
            {
                ClearCaptionCache();
                idleCaption = BakeCaption(0, size);
                hoverCaption = BakeCaption(1, size);
                cachedText = Text;
                cachedSize = size;
            }
            var destination = new Rectangle(0, 0, Width, Height);
            float hover = MathHelper.Clamp(HoverTextureAlpha, 0, 1);
            DrawTexture(idleCaption, destination, Color.White * Alpha);
            if (hover > 0)
                DrawTexture(hoverCaption, destination, Color.White * hover * Alpha);
            DrawChildren(gameTime);
        }

        private RenderTarget2D BakeCaption(float hover, Point size)
        {
            var target = new RenderTarget2D(WindowManager.GraphicsDevice, size.X, size.Y,
                false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            try
            {
                Renderer.PushRenderTarget(target);
                try
                {
                    WindowManager.GraphicsDevice.Clear(Color.Transparent);
                    DrawCaption(hover, size.X, size.Y);
                }
                finally
                {
                    Renderer.PopRenderTarget();
                }
            }
            catch
            {
                target.Dispose();
                throw;
            }

            return target;
        }

        private void DrawCaption(float hover, int width, int height)
        {
            float scale = (menuFont == null ? 2.1f : 1) * Math.Min(1, height / 72f);
            var font = Renderer.GetFontList()[menuFontIndex];
            scale = Math.Min(scale, Math.Max(1, width - 24) / Math.Max(1, font.MeasureString(Text).X));
            var size = font.MeasureString(Text) * scale;
            var location = new Vector2((width - size.X) / 2, (height - (72 - font.GetTextYPadding(72, Text) * 2) * scale) / 2 - 3 * scale);
            var color = Color.Lerp(new Color(45, 228, 255), Color.White, hover);
            // Broad bloom remains behind the black lettering shadow in both states.
            for (int x = -16; x <= 16; x += 2)
            {
                for (int y = -16; y <= 16; y += 2)
                {
                    float distance = x * x + y * y;
                    float alpha = (float)Math.Exp(-distance / (65 + hover * 35)) * (0.04f + hover * 0.055f);
                    Renderer.DrawString(Text, menuFontIndex, location + new Vector2(x, y), color * alpha, scale);
                }
            }

            // Match the neighboring captions' broad dark halo as well as their
            // sharp outline. This work runs only when rebuilding the caption cache.
            for (int x = -12; x <= 12; x += 2)
            {
                for (int y = -10; y <= 10; y += 2)
                {
                    float alpha = 0.32f * (float)Math.Exp(-(x * x + y * y) / 46.0);
                    Renderer.DrawString(Text, menuFontIndex, location + new Vector2(x, y + 3), Color.Black * alpha, scale);
                }
            }

            Renderer.DrawString(Text, menuFontIndex, location + new Vector2(1, 2), Color.Black * 0.85f, scale);
            for (int x = -2; x <= 2; x++)
            {
                for (int y = -2; y <= 2; y++)
                {
                    if (x * x + y * y <= 5)
                        Renderer.DrawString(Text, menuFontIndex, location + new Vector2(x, y) * scale, Color.Black, scale);
                }
            }

            Renderer.DrawString(Text, menuFontIndex, location, color, scale);
        }

        private void ClearCaptionCache()
        {
            idleCaption?.Dispose();
            hoverCaption?.Dispose();
            idleCaption = null;
            hoverCaption = null;
        }

        public override void Kill()
        {
            ClearCaptionCache();
            if (IdleTexture == transparent)
                IdleTexture = null;

            if (HoverTexture == transparent)
                HoverTexture = null;

            // Neither theme textures nor the shared font list belong to this control.
            IdleTexture = null;
            HoverTexture = null;
            transparent.Dispose();
            if (captionFont != null)
            {
                var fonts = Renderer.GetFontList();
                int index = fonts.IndexOf(captionFont);
                if (index == fonts.Count - 1 && index >= 0)
                    fonts.RemoveAt(index);
                else if (index > 0)
                    fonts[index] = fonts[0];
            }
            menuFont?.Dispose();
            base.Kill();
        }
    }
}