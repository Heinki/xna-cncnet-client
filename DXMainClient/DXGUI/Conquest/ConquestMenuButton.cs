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

        public ConquestMenuButton(WindowManager windowManager) : base(windowManager)
        {
            string? fontPath = AssetLoader.AssetSearchPaths.Select(p => Path.Combine(p, "Fonts", "texgyreadventor-bold.otf")).FirstOrDefault(File.Exists);
            using var fontStream = fontPath != null ? File.OpenRead(fontPath) :
                typeof(ConquestMenuButton).Assembly.GetManifestResourceStream("DTAClient.ConquestMenu.otf");
            if (fontStream != null)
            {
                using var bytes = new MemoryStream();
                fontStream.CopyTo(bytes);
                menuFont = new FontSystem(new FontSystemSettings { UseEmToPixelsScale = true });
                menuFont.AddFont(bytes.ToArray());
                menuFontIndex = Renderer.GetFontList().Count;
                captionFont = new TTFFontWrapper(menuFont.GetFont(30));
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
            }

            float scale = (menuFont == null ? 2.1f : 1) * Math.Min(1, Height / 72f);
            var font = fonts[menuFontIndex];
            scale = Math.Min(scale, (Width - 24) / Math.Max(1, font.MeasureString(Text).X));
            var size = font.MeasureString(Text) * scale;
            var location = new Vector2((Width - size.X) / 2, (Height - (72 - font.GetTextYPadding(72, Text) * 2) * scale) / 2);
            float hover = MathHelper.Clamp(HoverTextureAlpha, 0, 1);
            var color = Color.Lerp(new Color(45, 228, 255), Color.White, hover);
            // Broad bloom remains behind the black lettering shadow in both states.
            for (int x = -16; x <= 16; x += 2)
            {
                for (int y = -16; y <= 16; y += 2)
                {
                    float distance = x * x + y * y;
                    float alpha = (float)Math.Exp(-distance / (50 + hover * 35)) * (0.008f + hover * 0.055f);
                    DrawString(Text, menuFontIndex, location + new Vector2(x, y), color * alpha, scale);
                }
            }

            // Match the source captions' soft black shadow, avoiding a hard outline.
            for (int x = -4; x <= 4; x += 2)
            {
                for (int y = -4; y <= 4; y += 2)
                    DrawString(Text, menuFontIndex, location + new Vector2(x, y + 1), Color.Black * (float)(0.14 * Math.Exp(-(x * x + y * y) / 12.0)), scale);
            }

            DrawString(Text, menuFontIndex, location + new Vector2(1, 2), Color.Black * 0.85f, scale);
            DrawString(Text, menuFontIndex, location, color, scale);
            DrawChildren(gameTime);
        }

        public override void Kill()
        {
            if (IdleTexture == transparent)
                IdleTexture = null;

            if (HoverTexture == transparent)
                HoverTexture = null;

            // Neither theme textures nor the shared font list belong to this control.
            IdleTexture = null;
            HoverTexture = null;
            transparent.Dispose();
            base.Kill();
        }
    }
}
