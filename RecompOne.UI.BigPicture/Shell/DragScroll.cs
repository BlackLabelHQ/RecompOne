namespace RecompOne.UI.BigPicture.Shell;

public static class DragScroll
{
    public static bool Begin()
    {
        var dragging = ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem) &&
                       ImGui.IsMouseDragging(ImGuiMouseButton.Left) && !ImGui.IsAnyItemActive();
        if (!dragging) return false;
        
        ImGui.SetScrollY(ImGui.GetScrollY() - ImGui.GetIO().MouseDelta.Y);
        ImGui.BeginDisabled();
        return true;
    }
    
    public static void End(bool dragging)
    {
        if (dragging) ImGui.EndDisabled();
    }
}
