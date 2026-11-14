namespace RecompOne.UI.BigPicture.Shell;

public class KeyLegend(MenuInput input)
{
    private const float GlyphScale = 1.25f;
    
    protected virtual float LabelGap => Px(8f);
    
    protected virtual float Spacing => Px(18f);
    
    public virtual void Draw(Vector2 pos, string accept, string back)
    {
        var x = MathF.Round(pos.X);
        var y = MathF.Round(pos.Y);
        if (input.UsesGamepad)
        {
            x = Entry(x, y, accept, (draw, at, size, ink) => ButtonGlyphs.Pad(draw, at, size, PadButton.South, ink));
            Entry(x, y, back, (draw, at, size, ink) => ButtonGlyphs.Pad(draw, at, size, PadButton.East, ink));
            return;
        }
        
        x = Entry(x, y, accept, (draw, at, size, ink) => ButtonGlyphs.Key(draw, at, size, KeyButton.Enter, ink));
        Entry(x, y, back, (draw, at, size, ink) => ButtonGlyphs.Key(draw, at, size, KeyButton.Escape, ink));
    }
    
    protected delegate float GlyphDrawer(ImDrawListPtr draw, Vector2 pos, float size, uint color);
    
    protected virtual float Entry(float x, float y, string label, GlyphDrawer glyph)
    {
        var draw = ImGui.GetWindowDrawList();
        var size = MathF.Round(ImGui.GetFontSize() * GlyphScale);
        var top = MathF.Round(y + (ImGui.GetFontSize() - size) * 0.5f);
        var width = glyph(draw, new Vector2(x, top), size, Palette.Text);
        
        var labelX = MathF.Round(x + width + LabelGap);
        draw.AddText(new Vector2(labelX, y), Palette.Muted, label);
        return MathF.Round(labelX + ImGui.CalcTextSize(label).X + Spacing);
    }
}
