using System.Numerics;

namespace RecompOne.UI;

public static class MainDock
{
    private const int RedockCooldownFrames = 8;
    
    private static bool _layoutPending = true;
    private static int _redockCooldown;
    
    public static void RequestLayout()
    {
        _layoutPending = true;
    }
    
    public static void CancelLayout()
    {
        _layoutPending = false;
    }
    
    public static void Draw(bool overlay)
    {
        var viewport = ImGui.GetMainViewport();
        var area = overlay ? viewport.Size : viewport.WorkSize;
        ImGui.SetNextWindowPos(overlay ? viewport.Pos : viewport.WorkPos);
        ImGui.SetNextWindowSize(area);
        ImGui.SetNextWindowViewport(viewport.ID);
        
        const ImGuiWindowFlags hostFlags = ImGuiWindowFlags.NoDocking |
                                           ImGuiWindowFlags.NoTitleBar |
                                           ImGuiWindowFlags.NoCollapse |
                                           ImGuiWindowFlags.NoResize |
                                           ImGuiWindowFlags.NoMove |
                                           ImGuiWindowFlags.NoBringToFrontOnFocus |
                                           ImGuiWindowFlags.NoBackground;
        
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.Begin("##DockHost", hostFlags);
        ImGui.PopStyleVar(3);
        var dockId = ImGui.GetID("##MainDock");
        var openCount = PanelManager.Panels.Count(p => p.IsOpen && p is not IFloatingPanel);
        var dockFlags = openCount <= 1 ? (ImGuiDockNodeFlags)4096 : ImGuiDockNodeFlags.None;
        ImGui.DockSpace(dockId, Vector2.Zero, dockFlags);
        
        if (openCount <= 1 && !OutputPanel.IsDocked && _redockCooldown == 0)
            _layoutPending = true;
        
        if (_redockCooldown > 0) _redockCooldown--;
        
        if (_layoutPending)
        {
            _layoutPending = false;
            _redockCooldown = RedockCooldownFrames;
            if (PanelManager.Get<OutputPanel>() is { } output)
                DockBuilder.SetupCenterLayout(dockId, area, output.Title());
        }
        
        ImGui.End();
    }
}
