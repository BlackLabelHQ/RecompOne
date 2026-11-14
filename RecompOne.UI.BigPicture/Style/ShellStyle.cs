namespace RecompOne.UI.BigPicture.Style;

public static class ShellStyle
{
    //runs every frame because theme.apply overwrites these
    public static void Apply()
    {
        var style = ImGui.GetStyle();
        style.FrameRounding = Metrics.FrameRounding;
        style.GrabRounding = Metrics.GrabRounding;
        style.PopupRounding = Metrics.PopupRounding;
        style.PopupBorderSize = 0f;
        style.Colors[(int)ImGuiCol.NavCursor] = Theme.AccentText;
        style.Colors[(int)ImGuiCol.PopupBg] = Palette.Popup;
        style.Colors[(int)ImGuiCol.ModalWindowDimBg] = Palette.Dim;
    }
}
