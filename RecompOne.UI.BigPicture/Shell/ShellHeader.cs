namespace RecompOne.UI.BigPicture.Shell;

public static class ShellHeader
{
    public static string ClockFormat { get; set; } = "HH:mm";
    
    public static void Draw()
    {
        var clock = DateTime.Now.ToString(ClockFormat);
        ImGui.TextDisabled(HostWindow.Title);
        ImGui.SameLine(ImGui.GetCursorStartPos().X + ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(clock).X);
        ImGui.TextDisabled(clock);
    }
}
