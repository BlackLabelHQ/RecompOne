namespace RecompOne.UI.BigPicture.Components.Widgets;

public class PillWidget(WidgetSet widgets) : Widget(widgets)
{
    public virtual float Height => Widgets.Row.Height;
    
    public virtual float Rounding => Metrics.Rounding;
    
    public virtual bool Draw(string label, float width, bool danger, bool enabled)
    {
        var size = new Vector2(width, Height);
        var min = ImGui.GetCursorScreenPos();
        
        if (!enabled) ImGui.BeginDisabled();
        var clicked = NavCursor.Button($"##pill-{label}", size);
        var glow = RowGlow.Update(ImGui.GetItemID(), Focused, Hovered);
        if (!enabled) ImGui.EndDisabled();
        
        var draw = ImGui.GetWindowDrawList();
        var idle = danger ? Palette.DangerIdle : Palette.Surface;
        var fill = danger ? Palette.Danger : Theme.Accent;
        draw.AddRectFilled(min, min + size, Rgba(Vector4.Lerp(idle, fill, glow)), Rounding);
        var text = ImGui.CalcTextSize(label);
        draw.AddText(Snap(min + (size - text) * 0.5f), Palette.Ink(enabled), label);
        return clicked && enabled;
    }
}
