using System.Numerics;
using ImGuiNET;

namespace RecompOne.Runtime.Host.Window;

internal sealed class VramViewerPanel : IPanel
{
    public string Name => "VRAM Viewer";
    public bool IsOpen { get; set; }

    static uint _texId;
    static int _texW, _texH;

    public static void SetTexture(uint id, int w, int h) => (_texId, _texW, _texH) = (id, w, h);

    readonly TextureView _view = new();

    public void Draw()
    {
        var flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.MenuBar;
        ImGui.SetNextWindowSize(new Vector2(800, 450) * HostWindow.DpiScale, ImGuiCond.FirstUseEver);

        bool open = IsOpen;
        if (!ImGui.Begin(Name, ref open, flags)) { IsOpen = open; ImGui.End(); return; }
        DrawMenuBar();
        DrawImage();
        IsOpen = open;
        ImGui.End();
    }

    void DrawMenuBar()
    {
        if (!ImGui.BeginMenuBar()) return;

        if (ImGui.MenuItem("Reset view")) _view.Reset();
        ImGui.TextDisabled($"Zoom: {_view.Zoom:F2}x");

        ImGui.EndMenuBar();
    }

    void DrawImage()
    {
        if (_texId == 0 || _texW <= 0 || _texH <= 0) { ImGui.TextDisabled("Waiting for VRAM texture..."); return; }
        _view.Draw(_texId, _texW, _texH, 0.25f, 32f);
    }
}
