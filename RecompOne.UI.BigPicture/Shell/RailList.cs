namespace RecompOne.UI.BigPicture.Shell;

public readonly record struct RailItem(string Icon, string Label, bool Danger = false);

public class RailList(string id, MenuInput input)
{
    private readonly RailHighlight _highlight = new();
    private int _shown = -1;
    
    protected virtual float RowHeight => Px(54f);
    
    protected virtual float Inset => Px(16f);
    
    protected virtual float IconGap => Px(14f);
    
    public virtual int Draw(IReadOnlyList<RailItem> items, int selected, bool focused)
    {
        if (items.Count == 0) return -1;
        selected = Math.Clamp(selected, 0, items.Count - 1);
        
        var height = RowHeight;
        var width = ImGui.GetContentRegionAvail().X;
        var size = new Vector2(width, height);
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        
        var target = origin.Y + selected * height;
        var pill = new Vector2(origin.X, _highlight.Top(target));
        var fill = items[selected].Danger ? Palette.Danger : focused ? Theme.Accent : Palette.Raised;
        draw.AddRectFilled(pill, pill + size, Rgba(_highlight.Fill(fill)), Metrics.Rounding);
        
        var clicked = -1;
        for (var i = 0; i < items.Count; i++)
        {
            var top = MathF.Round(origin.Y + i * height);
            ImGui.SetCursorScreenPos(new Vector2(origin.X, top));
            if (ImGui.InvisibleButton($"##{id}{i}", size)) clicked = i;
            if (i == selected && _shown != selected) ImGui.SetScrollHereY(0.5f);
            
            var hovered = i != selected && input.ItemHovered;
            DrawHover(draw, items[i], new Vector2(origin.X, top), size, hovered);
            DrawItem(draw, items[i], new Vector2(origin.X, top), height, i == selected || hovered);
        }
        
        _shown = selected;
        return clicked;
    }
    
    protected virtual void DrawHover(ImDrawListPtr draw, in RailItem item, Vector2 min, Vector2 size, bool hovered)
    {
        var glow = RowGlow.Update(ImGui.GetItemID(), false, hovered);
        if (glow > 0f) draw.AddRectFilled(min, min + size, Rgba((item.Danger ? Palette.Danger : Palette.Raised) with { W = glow }), Metrics.Rounding);
    }
    
    protected virtual void DrawItem(ImDrawListPtr draw, in RailItem item, Vector2 min, float height, bool selected)
    {
        var color = Palette.Ink(selected);
        var icon = Metrics.IconSize;
        
        ImGuiUtils.AddText(draw, ShellFonts.Icons, icon, Snap(new Vector2(min.X + Inset, min.Y + (height - icon) * 0.5f)), color, item.Icon);
        draw.AddText(Snap(new Vector2(min.X + Inset + icon + IconGap, min.Y + (height - ImGui.GetFontSize()) * 0.5f)), color, item.Label);
    }
}
