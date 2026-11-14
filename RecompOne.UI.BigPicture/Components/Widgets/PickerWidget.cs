namespace RecompOne.UI.BigPicture.Components.Widgets;

public class PickerWidget(WidgetSet widgets) : Widget(widgets)
{
    public virtual float Padding => Px(12f);
    
    public virtual float ArrowGap => Px(8f);
    
    public virtual bool Draw(string popup, IReadOnlyList<string> items, ref int index)
    {
        var row = Widgets.Row;
        var height = items.Count * row.Pitch + Padding * 2f;
        var size = new Vector2(CenteredPopup.Width, MathF.Min(height, CenteredPopup.MaxHeight));
        if (!CenteredPopup.Begin(popup, Padding, ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize, size)) return false;
        
        Input.ShowCursor();
        var changed = false;
        for (var i = 0; i < items.Count; i++)
        {
            var clicked = row.Press($"{items[i]}##{i}", null, out var bounds);
            if (i == index)
            {
                ImGui.SetItemDefaultFocus();
                if (ImGui.IsWindowAppearing()) ImGui.SetScrollHereY(0.5f);
                row.DrawCheck(bounds);
            }
            
            if (!clicked) continue;
            changed = i != index;
            index = i;
            ImGui.CloseCurrentPopup();
        }
        
        ImGui.EndPopup();
        return changed;
    }
    
    public virtual void DrawChoice(string preview, float left, RowBounds bounds, float inset)
    {
        var draw = ImGui.GetWindowDrawList();
        var y = (bounds.Min.Y + bounds.Max.Y - ImGui.GetFontSize()) * 0.5f;
        var arrow = bounds.Max.X - inset - ImGui.CalcTextSize(FontSet.ChevronDown).X;
        draw.AddText(Snap(new Vector2(arrow, y)), Palette.Muted, FontSet.ChevronDown);
        
        draw.PushClipRect(new Vector2(left, bounds.Min.Y), new Vector2(arrow - ArrowGap, bounds.Max.Y), true);
        draw.AddText(Snap(new Vector2(left, y)), Palette.Text, preview);
        draw.PopClipRect();
    }
}
