namespace RecompOne.UI.BigPicture.Components;

public class BigPictureComponents(WidgetSet widgets) : UiComponents
{
    private bool _inContextMenu;
    
    public WidgetSet Widgets { get; } = widgets;
    
    protected virtual float DialogGap => Px(10f);
    
    protected virtual float DialogTop => Px(8f);
    
    protected virtual float CompactInset => Px(10f);
    
    protected virtual float MenuPadding => Px(12f);
    
    protected virtual Vector2 SectionGap => Px(6f, 2f);
    
    protected virtual float SeparatorGap => Px(4f);
    
    public override bool UsesRows => true;
    
    public override float SelectableHeight => Widgets.Row.Pitch;
    
    public override bool Selectable(string label, bool selected)
    {
        return MenuItem(label, null, selected, true);
    }
    
    public override CardResult DrawCard(string id, Card card)
    {
        return Widgets.Card.Draw(id, card);
    }
    
    //cancel goes first
    public override DialogChoice Dialog(string confirm, string? cancel = null, bool danger = false, bool enabled = true)
    {
        ImGui.Dummy(new Vector2(0f, DialogTop));
        var gap = DialogGap;
        var pill = Widgets.Pill;
        var width = cancel == null ? ImGui.GetContentRegionAvail().X : MathF.Floor((ImGui.GetContentRegionAvail().X - gap) * 0.5f);
        
        if (cancel == null) return pill.Draw(confirm, width, danger, enabled) ? DialogChoice.Confirm : DialogChoice.None;
        
        var choice = DialogChoice.None;
        if (danger)
        {
            if (pill.Draw(cancel, width, false, true)) choice = DialogChoice.Cancel;
            ImGui.SameLine(0f, gap);
            if (pill.Draw(confirm, width, true, enabled)) choice = DialogChoice.Confirm;
            return choice;
        }
        
        if (pill.Draw(confirm, width, false, enabled)) choice = DialogChoice.Confirm;
        ImGui.SameLine(0f, gap);
        if (pill.Draw(cancel, width, false, true)) choice = DialogChoice.Cancel;
        return choice;
    }
    
    public override bool Toggle(string label, ref bool value, string? hint = null)
    {
        var id = ImGui.GetID($"##knob-{label}");
        var clicked = Widgets.Row.Press(label, hint, out var bounds);
        if (clicked) value = !value;
        
        var topLeft = Widgets.Row.Trailing(bounds, Widgets.Switch.Size);
        Widgets.Switch.Draw(ImGui.GetWindowDrawList(), topLeft, id, value);
        return clicked;
    }
    
    //slider
    public override bool Slider(string label, ref int value, int min, int max, string format = "%d", string? hint = null)
    {
        var row = Widgets.Row.Begin(label);
        var changed = ImGui.SliderInt($"##{label}", ref value, min, max, format);
        Widgets.Row.End(row, hint);
        return changed;
    }
    
    public override bool Slider(string label, ref float value, float min, float max, string format = "%.2f",
        string? hint = null)
    {
        var row = Widgets.Row.Begin(label);
        var changed = ImGui.SliderFloat($"##{label}", ref value, min, max, format);
        Widgets.Row.End(row, hint);
        return changed;
    }
    
    public override bool Combo(string label, string preview, IReadOnlyList<string> items, ref int index,
        string? hint = null)
    {
        var popup = $"##pick-{label}";
        var row = Widgets.Row;
        if (row.Press(label, hint, out var bounds)) ImGui.OpenPopup(popup);
        
        Widgets.Picker.DrawChoice(preview, bounds.Min.X + row.Column(label, bounds.Width), bounds, row.Inset);
        return Widgets.Picker.Draw(popup, items, ref index);
    }
    
    public override bool CompactCombo(string id, string preview, IReadOnlyList<string> items, ref int index)
    {
        var popup = $"##pick-{id}";
        var min = ImGui.GetCursorScreenPos();
        var bounds = new RowBounds(min, min + new Vector2(ImGui.CalcItemWidth(), ImGui.GetFrameHeight()));
        
        if (NavCursor.Button($"##box-{id}", bounds.Size)) ImGui.OpenPopup(popup);
        
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(bounds.Min, bounds.Max, Rgba(Palette.Surface), ImGui.GetStyle().FrameRounding);
        Widgets.Row.DrawBackground(draw, ImGui.GetItemID(), bounds, Widgets.Input.ItemFocused, Widgets.Input.ItemHovered);
        Widgets.Picker.DrawChoice(preview, bounds.Min.X + CompactInset, bounds, CompactInset);
        return Widgets.Picker.Draw(popup, items, ref index);
    }
    
    public override bool Swatch(string id, Vector4 color, bool selected, string? hint = null)
    {
        return Widgets.Swatch.Draw(id, color, selected, hint);
    }
    
    public override bool ColorEdit(string label, ref Vector3 color)
    {
        return Widgets.Color.Draw(label, ref color);
    }
    
    public override bool Input(string label, ref string value, uint maxLength = 256, string? placeholder = null,
        string? hint = null)
    {
        var row = Widgets.Row.Begin(label);
        if (placeholder == null) ImGui.InputText($"##{label}", ref value, maxLength);
        else ImGui.InputTextWithHint($"##{label}", placeholder, ref value, maxLength);
        var finished = ImGui.IsItemDeactivatedAfterEdit();
        Widgets.Row.End(row, hint);
        return finished;
    }
    
    public override bool InputInt(string label, ref int value, string? hint = null)
    {
        var row = Widgets.Row.Begin(label);
        var changed = ImGui.InputInt($"##{label}", ref value);
        Widgets.Row.End(row, hint);
        return changed;
    }
    
    public override bool Button(string label, string? hint = null)
    {
        return Widgets.Row.Press(label, hint, out _);
    }
    
    public override bool BeginContextMenu(string id)
    {
        var size = new Vector2(CenteredPopup.Width, 0f);
        if (!CenteredPopup.Begin(id, MenuPadding, ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize, size)) return false;
        
        _inContextMenu = true;
        Widgets.Input.ShowCursor();
        return true;
    }
    
    public override void EndContextMenu()
    {
        _inContextMenu = false;
        ImGui.EndPopup();
    }
    
    public override void MenuNote(string text)
    {
        Indent();
        ImGui.TextDisabled(text);
    }
    
    public override bool MenuItem(string label, string? shortcut, bool selected, bool enabled)
    {
        var clicked = Widgets.Row.Press(label, null, out var bounds, enabled);
        if (clicked && _inContextMenu) ImGui.CloseCurrentPopup();
        if (selected) Widgets.Row.DrawCheck(bounds);
        return clicked;
    }
    
    public override void Text(string text)
    {
        Indent();
        ImGui.TextUnformatted(text);
    }
    
    public override void Section(string title)
    {
        var gap = SectionGap;
        ImGui.Dummy(new Vector2(0f, gap.X));
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.AccentText);
        ImGui.TextUnformatted(title);
        ImGui.PopStyleColor();
        ImGui.Dummy(new Vector2(0f, gap.Y));
    }
    
    public override void Separator()
    {
        ImGui.Dummy(new Vector2(0f, SeparatorGap)); //only gap, not show divisor
    }
    
    //dimmed wrapped text aligned with the row labels
    public override void Note(string text)
    {
        Indent();
        ImGui.PushTextWrapPos(0f);
        ImGui.TextDisabled(text);
        ImGui.PopTextWrapPos();
    }
    
    public override void Hint(string? text)
    {
        if (text != null && (Widgets.Input.ItemHovered || Widgets.Input.ItemFocused)) Widgets.Hints.Show(text); //send hint to the shell
    }
    
    protected void Indent()
    {
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Widgets.Row.Inset);
    }
}
