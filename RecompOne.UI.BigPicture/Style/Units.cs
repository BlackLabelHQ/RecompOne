namespace RecompOne.UI.BigPicture.Style;

public static class Units
{
    public static float Px(float value) => MathF.Round(value * Theme.Scale);
    
    public static Vector2 Px(float x, float y) => new(Px(x), Px(y));
    
    public static Vector2 Snap(Vector2 value) => new(MathF.Round(value.X), MathF.Round(value.Y));
    
    public static uint Rgba(Vector4 color) => ImGui.ColorConvertFloat4ToU32(color);
}
