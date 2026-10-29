#nullable enable

using Microsoft.Xna.Framework.Graphics;

using Rampastring.XNAUI;

namespace DTAClient.DXGUI.Conquest
{
    /// <summary>Pack-owned transparent unit art with silhouettes extracted from native game assets.</summary>
    internal sealed class ConquestArmyAppearance
    {
        public Texture2D? Unit { get; private set; }
        public string Style { get; private set; } = string.Empty;

        public static ConquestArmyAppearance Load(string faction, string unitImage = "", string style = "")
        {
            string factionStyle = NormalizeStyle(string.IsNullOrEmpty(style) ? faction : style);

            return new ConquestArmyAppearance
            {
                Unit = LoadTexture(unitImage) ?? LoadTexture("ConquestArmy_" + faction + ".png") ?? LoadTexture(NativeUnit(factionStyle)) ?? LoadTexture("ConquestArmy.png"),
                Style = factionStyle
            };
        }

        private static string NativeUnit(string style) => style switch
        {
            "gdi" => "Conquest/Units/Titan.png",
            "nod" => "Conquest/Units/TickTank.png",
            "allies" => "Conquest/Units/Grizzly.png",
            "soviet" => "Conquest/Units/Rhino.png",
            "yuri" => "Conquest/Units/Lasher.png",
            _ => string.Empty
        };

        private static string NormalizeStyle(string name) => name.Trim().ToLowerInvariant() switch
        {
            "allied" or "allies" or "united states" or "usa" or "americans" or "america" or
            "korea" or "south korea" or "alliance" or "france" or "french" or "germany" or "germans" or "great britain" or "british" or "britain" => "allies",
            "soviet" or "soviets" or "russia" or "russians" or "soviet union" or "cuba" or "confederation" or "iraq" or "arabs" or "libya" or "africans" => "soviet",
            "yuri" or "yuri's army" or "yuricountry" => "yuri",
            "brotherhood of nod" => "nod",
            "global defense initiative" => "gdi",
            _ => name.Trim().ToLowerInvariant()
        };

        private static Texture2D? LoadTexture(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            if (AssetLoader.AssetExists(name))
                return AssetLoader.LoadTexture(name);

            string lower = name.ToLowerInvariant();

            return AssetLoader.AssetExists(lower) ? AssetLoader.LoadTexture(lower) : null;
        }
    }
}