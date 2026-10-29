#nullable enable

using System.Collections.Generic;
using System.Linq;

using ClientGUI;

using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Conquest
{
    /// <summary>
    /// Lobby-style INI canvas. Campaign-bound controls have code defaults and stable
    /// names; themes override their sections with the standard INI expression parser.
    /// </summary>
    internal abstract class ConquestCanvas : INItializableWindow
    {
        protected ConquestCanvas(WindowManager windowManager) : base(windowManager)
        {
        }

        public override void Initialize()
        {
            var campaignControls = GetDescendants(this).ToArray();

            base.Initialize();
            if (ConfigIni == null)
                return;

            foreach (var control in campaignControls)
            {
                if (!string.IsNullOrEmpty(control.Name))
                    ReadINIForControl(control);
            }
        }

        private static IEnumerable<XNAControl> GetDescendants(XNAControl parent)
        {
            foreach (var child in parent.Children)
            {
                yield return child;

                foreach (var descendant in GetDescendants(child))
                    yield return descendant;
            }
        }
    }
}