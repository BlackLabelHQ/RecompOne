using System.Numerics;

namespace RecompOne.UI;

public sealed class VramViewerPanel : IPanel
{
    public string Name => "VRAM Viewer";
    public string TitleKey => "panel.vram_viewer";
    public bool IsOpen { get; set; }

    private readonly TextureView _view = new();

    public void Draw()
    {
        var flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.MenuBar;
        ImGui.SetNextWindowSize(new Vector2(800, 450), ImGuiCond.FirstUseEver);

        var open = IsOpen;
        if (!ImGui.Begin(this.Title(), ref open, flags))
        {
            IsOpen = open;
            ImGui.End();
            return;
        }

        DrawMenuBar();
        DrawImage();
        IsOpen = open;
        ImGui.End();
    }

    private void DrawMenuBar()
    {
        if (!ImGui.BeginMenuBar()) return;

        if (ImGui.MenuItem("Reset view")) _view.Reset();
        ImGui.TextDisabled($"Zoom: {_view.Zoom:F2}x");

        ImGui.EndMenuBar();
    }

    private void DrawImage()
    {
        var vram = HostOutput.Vram;
        if (!vram.IsValid)
        {
            ImGui.TextDisabled("Waiting for VRAM texture...");
            return;
        }

        _view.Draw(vram.Id, vram.Width, vram.Height, 0.25f, 32f);
    }
}