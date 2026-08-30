using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Host.Window;

//inspired on pcsxredux's one
internal sealed class RamMapPanel : IPanel
{
    public string Name => "RAM Map";
    public string TitleKey => "panel.ram_map";
    public bool IsOpen { get; set; }

    private static uint _texId;

    public static void SetTexture(uint id)
    {
        _texId = id;
    }

    private readonly TextureView _view = new();

    public void Draw()
    {
        var flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.MenuBar;
        ImGui.SetNextWindowSize(new Vector2(820, 300), ImGuiCond.FirstUseEver);

        var open = IsOpen;
        if (!ImGui.Begin(this.Title(), ref open, flags))
        {
            IsOpen = open;
            ImGui.End();
            return;
        }

        DrawMenuBar();
        DrawMap();

        IsOpen = open;
        ImGui.End();
    }

    private void DrawMenuBar()
    {
        if (!ImGui.BeginMenuBar()) return;

        if (ImGui.BeginMenu("View"))
        {
            if (ImGui.MenuItem("Reset view")) _view.Reset();
            ImGui.Separator();
            ImGui.MenuItem("Show greyscale", null, ref Runtime.RamLog.ShowGreyscale);
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Configuration"))
        {
            //half life 3 is real
            ImGui.SliderFloat("Decay half-life (frames)", ref Runtime.RamLog.DecayFrames, 10f, 600f);
            ImGui.Separator();
            ImGui.ColorEdit4("Backdrop color", ref Runtime.RamLog.BackdropColor);
            ImGui.ColorEdit4("Write color", ref Runtime.RamLog.WriteColor);
            ImGui.ColorEdit4("Read color", ref Runtime.RamLog.ReadColor);
            ImGui.EndMenu();
        }

        ImGui.TextDisabled($"Zoom: {_view.Zoom:F2}x");

        if (_hoveredAddr.HasValue && _hoveredAddr.Value < 0x200000u)
        {
            ImGui.SameLine();
            ImGui.Spacing();
            ImGui.SameLine();
            ImGui.TextDisabled($"0x{0x80000000u + _hoveredAddr.Value:X8}");
        }

        ImGui.EndMenuBar();
    }

    private uint? _hoveredAddr;

    private void DrawMap()
    {
        if (_texId == 0)
        {
            ImGui.TextDisabled("Waiting for RAM texture...");
            return;
        }

        var r = _view.Draw(_texId, RamLogger.Width, RamLogger.Height);
        _hoveredAddr = null;
        if (!r.Hovered) return;

        var texel = (ImGui.GetIO().MousePos - r.ImgPos) / r.Scale;
        int byteX = (int)texel.X, byteY = (int)texel.Y;
        if (byteX >= 0 && byteY >= 0 && byteX < RamLogger.Width && byteY < RamLogger.Height)
        {
            var addr = (uint)byteY * RamLogger.Width + (uint)byteX;
            if (addr < 0x200000u) _hoveredAddr = addr;
        }

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && _hoveredAddr.HasValue)
        {
            var editor = PanelManager.Get<MemoryEditorPanel>();
            if (editor != null)
            {
                editor.JumpTo(_hoveredAddr.Value);
                editor.IsOpen = true;
            }
        }
    }
}