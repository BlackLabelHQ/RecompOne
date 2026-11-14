namespace RecompOne.UI.BigPicture.Glyphs;

public static class KeyCap
{
    private const float FontShare = 0.58f;
    private const float PaddingShare = 0.6f;
    private const float StrokeShare = 0.08f;
    private const float InsetShare = 0.12f;
    private const float RoundingShare = 0.3f;
    
    public static float Width(float size, string text)
    {
        var extent = ImGuiUtils.TextSize(ImGui.GetFont(), size * FontShare, text);
        return MathF.Max(size, extent.X + size * PaddingShare);
    }
    
    public static float Draw(ImDrawListPtr draw, Vector2 pos, float size, string text, uint color) //for key
    {
        var font = ImGui.GetFont();
        var fontSize = size * FontShare;
        var extent = ImGuiUtils.TextSize(font, fontSize, text);
        var width = Width(size, text);
        var thickness = MathF.Max(1f, size * StrokeShare);
        var half = thickness * 0.5f;
        var inset = size * InsetShare;
        draw.AddRect(pos + new Vector2(half, inset), pos + new Vector2(width, size - inset) - new Vector2(half), color, size * RoundingShare, ImDrawFlags.None, thickness);
        ImGuiUtils.AddText(draw, font, fontSize, Snap(pos + (new Vector2(width, size) - extent) * 0.5f), color, text);
        return width;
    }
}
