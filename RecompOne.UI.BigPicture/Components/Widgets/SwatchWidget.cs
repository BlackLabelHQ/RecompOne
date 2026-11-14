namespace RecompOne.UI.BigPicture.Components.Widgets;

public class SwatchWidget(WidgetSet widgets) : Widget(widgets)
{
    public virtual float Size => Px(44f);
    
    public virtual float Ring => Px(3f);
    
    public virtual float RingStroke => Px(2f);
    
    public virtual float Rounding => Metrics.Rounding;
    
    public virtual bool Draw(string id, Vector4 color, bool selected, string? hint)
    {
        if (ImGui.GetCursorPosX() <= ImGui.GetStyle().WindowPadding.X + 1f)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Widgets.Row.Inset);
        
        var size = new Vector2(Size, Size);
        var min = ImGui.GetCursorScreenPos();
        var clicked = NavCursor.Button(id, size);
        var focused = Focused;
        var hovered = Hovered;
        
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, min + size, Rgba(color), Rounding);
        if (selected)
        {
            var check = ImGui.CalcTextSize(FontSet.Check);
            draw.AddText(Snap(min + (size - check) * 0.5f), Palette.Contrast(color), FontSet.Check);
        }
        
        if (focused || hovered)
        {
            var ring = new Vector2(Ring, Ring);
            draw.AddRect(min - ring, min + size + ring, focused ? Palette.White : Palette.FaintWhite, Rounding + Ring, ImDrawFlags.None, RingStroke);
            Hint(hint);
        }
        
        return clicked;
    }
}
