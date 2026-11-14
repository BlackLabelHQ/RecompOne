namespace RecompOne.UI.BigPicture.Input;

public static class NavCursor
{
    public static void Hide() //looks better like this
    {
        ImGui.PushStyleColor(ImGuiCol.NavCursor, Vector4.Zero);
    }
    
    public static void Restore()
    {
        ImGui.PopStyleColor();
    }
    
    public static bool Button(string id, Vector2 size)
    {
        Hide();
        var clicked = ImGui.InvisibleButton(id, size, ImGuiButtonFlags.EnableNav);
        Restore();
        return clicked;
    }
}
