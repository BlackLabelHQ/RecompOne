namespace RecompOne.UI.BigPicture.Session;

public class MenuButtonOverlay(ShellSession session)
{
    private const ImGuiWindowFlags OverlayFlags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoScrollWithMouse;
    
    public virtual bool Available => !OperatingSystem.IsLinux() && !OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS();
    
    protected virtual float Size => Px(40f);
    
    public virtual void Draw() //is this right?
    {
        if (!Available || Autohide.CursorHidden) return;
        
        var viewport = ImGui.GetMainViewport();
        var size = new Vector2(Size, Size);
        ImGui.SetNextWindowPos((viewport.Pos + viewport.Size * 0.5f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(size);
        ImGui.SetNextWindowViewport(viewport.ID);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, size.X * 0.5f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.Button, Palette.Scrim);
        ImGui.Begin("##bp-overlay", OverlayFlags);
        if (ImGui.Button($"{FontSet.Ellipsis}##bp-open", size)) session.Open();
        ImGui.End();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(3);
    }
}
