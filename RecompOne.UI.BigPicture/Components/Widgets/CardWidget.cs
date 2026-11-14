namespace RecompOne.UI.BigPicture.Components.Widgets;

public class CardWidget(WidgetSet widgets) : Widget(widgets)
{
    private const string MetaSeparator = "  -  ";
    private const float DisabledAlpha = 0.55f;
    
    public virtual float IconSize => Px(64f);
    
    public virtual float Padding => Px(14f);
    
    public virtual float ActionSize => Px(44f);
    
    public virtual float ActionGap => Px(6f);
    
    public virtual float IconGap => Px(16f);
    
    public virtual float StatusDot => Px(4f);
    
    public virtual float TitleGap => Px(8f);
    
    public virtual float Rounding => Px(12f);
    
    public virtual CardResult Draw(string id, Card card)
    {
        ImGui.PushID(id);
        var draw = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var pad = Padding;
        var icon = IconSize;
        var button = ActionSize;
        var gap = ActionGap;
        var track = Widgets.Switch.Size;
        
        var actions = card.Actions.Length * (button + gap) + (card.Actions.Length > 0 ? pad - gap : 0f);
        var main = width - actions;
        var left = min.X + pad + icon + IconGap;
        var wrap = min.X + main - left - pad - (card.CanToggle ? track.X + pad : 0f);
        var line = ImGui.GetTextLineHeightWithSpacing();
        var meta = string.Join(MetaSeparator, card.Tags.Prepend(card.Meta).Where(static t => t.Length > 0));
        var text = string.IsNullOrWhiteSpace(card.Description) ? "" : card.Description;
        var body = line * (meta.Length > 0 ? 2f : 1f) + (text.Length > 0 ? ImGui.CalcTextSize(text, false, wrap).Y : 0f);
        var height = MathF.Max(icon, body) + pad * 2f;
        var bounds = new RowBounds(min, min + new Vector2(width, height));
        
        draw.ChannelsSplit(2);
        draw.ChannelsSetCurrent(1);
        
        var pressed = NavCursor.Button("##main", new Vector2(main, height));
        var backId = ImGui.GetItemID();
        var focused = Focused;
        var hovered = Hovered;
        var hint = focused || hovered ? card.ToggleTip ?? card.StatusTip : null;
        
        if (!card.Enabled) ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * DisabledAlpha);
        ImGui.SetCursorScreenPos(Snap(new Vector2(min.X + pad, min.Y + (height - icon) * 0.5f)));
        if (card.Icon != null) card.Icon(icon);
        DrawText(draw, card, left, MathF.Round(min.Y + (height - body) * 0.5f), line, meta, text, wrap);
        if (!card.Enabled) ImGui.PopStyleVar();
        
        if (card.CanToggle)
        {
            var topLeft = Snap(new Vector2(min.X + main - pad - track.X, (min.Y + bounds.Max.Y - track.Y) * 0.5f));
            Widgets.Switch.Draw(draw, topLeft, ImGui.GetID("##knob"), card.Enabled);
        }
        
        var action = !card.CanToggle && pressed && card.Actions.Length > 0 ? 0 : -1;
        for (var i = 0; i < card.Actions.Length; i++)
        {
            var pos = Snap(new Vector2(min.X + main + i * (button + gap), (min.Y + bounds.Max.Y - button) * 0.5f));
            if (DrawAction(draw, card, i, pos, out var lit)) action = i;
            if (!lit) continue;
            focused |= Focused;
            hovered |= Hovered;
            hint = card.Actions[i].Tip;
        }
        
        draw.ChannelsSetCurrent(0);
        draw.AddRectFilled(bounds.Min, bounds.Max, Rgba(Palette.Card), Rounding);
        Widgets.Row.DrawBackground(draw, backId, bounds, focused, hovered);
        draw.ChannelsMerge();
        
        if (hint is { Length: > 0 }) Hint(hint);
        ImGui.SetCursorScreenPos(min);
        ImGui.Dummy(bounds.Size);
        
        if (card.Footer != null)
        {
            ImGui.Indent(pad);
            card.Footer();
            ImGui.Unindent(pad);
        }
        
        ImGui.PopID();
        return new CardResult(card.CanToggle && pressed, action, false, false);
    }
    
    protected virtual void DrawText(ImDrawListPtr draw, Card card, float left, float y, float line, string meta, string text, float wrap)
    {
        var dot = StatusDot;
        draw.AddCircleFilled(new Vector2(left + dot, y + ImGui.GetFontSize() * 0.5f), dot, Rgba(card.Status));
        draw.AddText(Snap(new Vector2(left + dot * 2f + TitleGap, y)), Palette.Text, card.Title);
        y += line;
        if (meta.Length > 0)
        {
            draw.AddText(Snap(new Vector2(left, y)), Palette.Muted, meta);
            y += line;
        }
        
        if (text.Length > 0)
            ImGuiUtils.AddText(draw, ImGui.GetFont(), ImGui.GetFontSize(), Snap(new Vector2(left, y)), Palette.Muted, text, wrap);
    }
    
    protected virtual bool DrawAction(ImDrawListPtr draw, Card card, int index, Vector2 pos, out bool lit)
    {
        var (glyph, _, active) = card.Actions[index];
        var size = new Vector2(ActionSize, ActionSize);
        ImGui.SetCursorScreenPos(pos);
        var clicked = NavCursor.Button($"##act{index}", size);
        
        lit = Focused || Hovered;
        if (lit) draw.AddRectFilled(pos, pos + size, Rgba(Theme.Accent), Metrics.Rounding);
        
        var extent = ImGui.CalcTextSize(glyph);
        draw.AddText(Snap(pos + (size - extent) * 0.5f), active ? Palette.Accent : Palette.Ink(lit), glyph);
        return clicked;
    }
}
