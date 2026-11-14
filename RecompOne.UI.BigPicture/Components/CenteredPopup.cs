namespace RecompOne.UI.BigPicture.Components;

public static class CenteredPopup
{
    private const float ViewportShare = 0.8f;
    
    public static float Width => MathF.Min(Px(520f), ImGui.GetMainViewport().Size.X * ViewportShare);
    
    public static float MaxHeight => ImGui.GetMainViewport().Size.Y * ViewportShare;
    
    public static bool Begin(string id, float padding, ImGuiWindowFlags flags, Vector2? size = null)
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos((viewport.Pos + viewport.Size * 0.5f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        if (size is { } fixedSize) ImGui.SetNextWindowSize(fixedSize);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(padding, padding));
        var open = ImGui.BeginPopup(id, flags);
        ImGui.PopStyleVar();
        return open;
    }
}
