namespace RecompOne.UI.BigPicture.Components.Widgets;

public readonly record struct RowBounds(Vector2 Min, Vector2 Max)
{
    public Vector2 Size => Max - Min;
    
    public float Width => Max.X - Min.X;
    
    public float Height => Max.Y - Min.Y;
}

public class RowWidget(WidgetSet widgets) : Widget(widgets)
{
    public virtual float Height => Px(46f);
    
    public virtual float Inset => Metrics.Inset;
    
    public virtual float LabelShare => 0.52f;
    
    public virtual float Rounding => Metrics.Rounding;
    
    public float Pitch => Height + ImGui.GetStyle().ItemSpacing.Y;
    
    public virtual bool Press(string label, string? hint, out RowBounds bounds, bool enabled = true)
    {
        var size = new Vector2(ImGui.GetContentRegionAvail().X, Height);
        var min = ImGui.GetCursorScreenPos();
        bounds = new RowBounds(min, min + size);
        
        if (!enabled) ImGui.BeginDisabled();
        var clicked = NavCursor.Button($"##row-{label}", size);
        var id = ImGui.GetItemID();
        var focused = Focused;
        var hovered = Hovered;
        if (!enabled) ImGui.EndDisabled();
        
        var draw = ImGui.GetWindowDrawList();
        DrawBackground(draw, id, bounds, focused, hovered);
        DrawLabel(draw, label, bounds, enabled);
        if (focused || hovered) Hint(hint);
        return clicked;
    }
    
    public virtual RowBounds Begin(string label)
    {
        var draw = ImGui.GetWindowDrawList();
        draw.ChannelsSplit(2);
        draw.ChannelsSetCurrent(1);
        
        var min = ImGui.GetCursorScreenPos();
        var bounds = new RowBounds(min, min + new Vector2(ImGui.GetContentRegionAvail().X, Height));
        DrawLabel(draw, label, bounds, true);
        
        var column = Column(label, bounds.Width);
        ImGui.SetCursorScreenPos(Snap(new Vector2(min.X + column, min.Y + (bounds.Height - ImGui.GetFrameHeight()) * 0.5f)));
        ImGui.SetNextItemWidth(bounds.Width - column - Inset);
        NavCursor.Hide();
        return bounds;
    }
    
    public virtual void End(RowBounds bounds, string? hint)
    {
        NavCursor.Restore();
        var focused = Focused || ImGui.IsItemActive();
        var hovered = Hovered || (Input.UsesMouse && ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(bounds.Min, bounds.Max));
        
        var draw = ImGui.GetWindowDrawList();
        draw.ChannelsSetCurrent(0);
        DrawBackground(draw, ImGui.GetItemID(), bounds, focused, hovered);
        draw.ChannelsMerge();
        
        if (focused || hovered) Hint(hint);
        ImGui.SetCursorScreenPos(bounds.Min);
        ImGui.Dummy(bounds.Size);
    }
    
    public virtual void DrawBackground(ImDrawListPtr draw, uint id, RowBounds bounds, bool focused, bool hovered)
    {
        var glow = RowGlow.Update(id, focused, hovered);
        if (glow > 0f) draw.AddRectFilled(bounds.Min, bounds.Max, Rgba(Palette.Glow with { W = Palette.Glow.W * glow }), Rounding);
    }
    
    public virtual void DrawLabel(ImDrawListPtr draw, string label, RowBounds bounds, bool enabled)
    {
        var text = Labels.Visible(label);
        if (text.Length == 0) return;
        var y = bounds.Min.Y + (bounds.Height - ImGui.GetFontSize()) * 0.5f;
        draw.AddText(Snap(new Vector2(bounds.Min.X + Inset, y)), Palette.Ink(enabled), text);
    }
    
    public virtual void DrawCheck(RowBounds bounds)
    {
        var size = ImGui.CalcTextSize(FontSet.Check);
        ImGui.GetWindowDrawList().AddText(Trailing(bounds, size), Palette.Accent, FontSet.Check);
    }
    
    public virtual float Column(string label, float width)
    {
        return Labels.Visible(label).Length == 0 ? Inset : MathF.Round(width * LabelShare);
    }
    
    public Vector2 Trailing(RowBounds bounds, Vector2 size)
    {
        return Snap(new Vector2(bounds.Max.X - Inset - size.X, (bounds.Min.Y + bounds.Max.Y - size.Y) * 0.5f));
    }
}
