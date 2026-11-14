namespace RecompOne.UI.BigPicture.Components.Widgets;

public class ColorWidget(WidgetSet widgets) : Widget(widgets)
{
    private const ImGuiColorEditFlags PickerFlags =
        ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.NoSmallPreview | ImGuiColorEditFlags.DisplayRgb;
    
    public virtual Vector2 Sample => Px(30f, 30f);
    
    public virtual float SampleRounding => Px(6f);
    
    public virtual float Padding => Px(16f);
    
    public virtual float PickerWidth => Px(320f);
    
    public virtual bool Draw(string label, ref Vector3 color)
    {
        var popup = $"##color-{label}";
        if (Widgets.Row.Press(label, null, out var bounds)) ImGui.OpenPopup(popup);
        
        var sample = Widgets.Row.Trailing(bounds, Sample);
        ImGui.GetWindowDrawList().AddRectFilled(sample, sample + Sample, Rgba(new Vector4(color, 1f)), SampleRounding);
        
        if (!CenteredPopup.Begin(popup, Padding, ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize)) return false;
        
        Input.ShowCursor();
        ImGui.SetNextItemWidth(PickerWidth);
        var changed = ImGui.ColorPicker3($"##picker-{label}", ref color, PickerFlags);
        ImGui.EndPopup();
        return changed;
    }
}
