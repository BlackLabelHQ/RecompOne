using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Config;

namespace RecompOne.Runtime.Host.Window;

//may people be happy! - flaffy
internal static class Autohide
{
    private const double CursorIdleMs = 5000.0;
    private const float RevealPixels = 2f;

    private static double _lastActivity;
    private static bool _barShown;

    public static bool CursorHidden { get; private set; }

    public static bool BarOverlay => InterfaceOptions.AutoHideMenuBar && !InterfaceOptions.HideMenuBar;

    public static bool BarVisible => !InterfaceOptions.HideMenuBar && (!InterfaceOptions.AutoHideMenuBar || _barShown);

    public static void Update()
    {
        var io = ImGui.GetIO();
        var now = FrameClock.Now;
        if (io.MouseDelta != Vector2.Zero || io.MouseWheel != 0f || io.MouseWheelH != 0f ||
            io.MouseDown[0] || io.MouseDown[1] || io.MouseDown[2])
            _lastActivity = now;

        var menuOpen = ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel);
        CursorHidden = now - _lastActivity > CursorIdleMs && !menuOpen && !ImGui.IsAnyItemActive();

        if (!BarOverlay)
        {
            _barShown = false;
            return;
        }

        var valid = ImGui.IsMousePosValid();
        var y = io.MousePos.Y - ImGui.GetMainViewport().Pos.Y;
        if (CursorHidden) _barShown = false;
        else if (valid && y <= RevealPixels * Theme.Scale) _barShown = true;
        else if (_barShown && !menuOpen && (!valid || y > ImGui.GetFrameHeight())) _barShown = false;
    }
}
