#nullable enable

using ClientGUI;

using Rampastring.XNAUI;

namespace DTAClient.DXGUI.Conquest
{
    /// <summary>
    /// Campaign panels are recreated on navigation. Their buttons share the theme's cached
    /// textures with the main menu and lobby, so closing a panel must not dispose those assets.
    /// </summary>
    internal sealed class ConquestButton : XNAClientButton
    {
        public ConquestButton(WindowManager windowManager) : base(windowManager)
        {
        }

        public override void Kill()
        {
            IdleTexture = null;
            HoverTexture = null;
            base.Kill();
        }
    }
}
