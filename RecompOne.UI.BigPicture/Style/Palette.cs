namespace RecompOne.UI.BigPicture.Style;

public static class Palette
{
    private const float LightThreshold = 0.6f;
    
    public const uint White = 0xFFFFFFFFu;
    public const uint Black = 0xFF000000u;
    public const uint FaintWhite = 0x80FFFFFFu;
    
    public static Vector4 Danger { get; set; } = new(0.82f, 0.22f, 0.24f, 1f);
    
    public static Vector4 Surface => ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg];
    
    public static Vector4 Raised => ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBgActive];
    
    public static Vector4 Backdrop => Theme.Background with { W = 0.94f };
    
    public static Vector4 Panel => Surface with { W = 0.94f };
    
    public static Vector4 Card => Surface with { W = 0.45f };
    
    public static Vector4 Glow => Theme.Accent with { W = 0.42f };
    
    public static Vector4 DangerIdle => Danger with { W = 0.55f };
    
    public static Vector4 Popup => Theme.Background with { W = 0.98f };
    
    public static Vector4 Dim { get; set; } = new(0f, 0f, 0f, 0.62f);
    
    public static Vector4 Scrim { get; set; } = new(0f, 0f, 0f, 0.55f);
    
    public static uint Text => ImGui.GetColorU32(ImGuiCol.Text);
    
    public static uint Muted => ImGui.GetColorU32(ImGuiCol.TextDisabled);
    
    public static uint Accent => Rgba(Theme.AccentText);
    
    public static uint Ink(bool strong) => strong ? Text : Muted;
    
    public static uint Contrast(Vector4 color)
    {
        return color.X * 0.3f + color.Y * 0.59f + color.Z * 0.11f > LightThreshold ? Black : White;
    }
}
