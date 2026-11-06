using System.Numerics;

namespace RecompOne.UI;

/// <summary>
/// Classic component interface, its just imgui without much modifications, the interface is slighly tweaked by <see cref="Theme"/>
/// </summary>
public class ClassicComponents : UiComponents
{
    public override bool UsesRows => false;
    
    public override float SelectableHeight => ImGui.GetTextLineHeightWithSpacing();
    
    public override bool Selectable(string label, bool selected)
    {
        return ImGui.Selectable(label, selected);
    }
    
    public override bool Toggle(string label, ref bool value, string? hint = null)
    {
        var changed = ImGui.Checkbox(label, ref value);
        Hint(hint);
        return changed;
    }
    
    public override bool Slider(string label, ref int value, int min, int max, string format = "%d", string? hint = null)
    {
        var changed = ImGui.SliderInt(label, ref value, min, max, format);
        Hint(hint);
        return changed;
    }
    
    public override bool Slider(string label, ref float value, float min, float max, string format = "%.2f",
        string? hint = null)
    {
        var changed = ImGui.SliderFloat(label, ref value, min, max, format);
        Hint(hint);
        return changed;
    }
    
    public override bool Combo(string label, string preview, IReadOnlyList<string> items, ref int index,
        string? hint = null)
    {
        var changed = false;
        if (ImGui.BeginCombo(label, preview))
        {
            changed = ComboItems(items, ref index);
            ImGui.EndCombo();
        }
        
        Hint(hint);
        return changed;
    }
    
    public override bool CompactCombo(string id, string preview, IReadOnlyList<string> items, ref int index)
    {
        return Combo(id, preview, items, ref index);
    }
    
    public override bool Input(string label, ref string value, uint maxLength = 256, string? placeholder = null,
        string? hint = null)
    {
        if (placeholder == null) ImGui.InputText(label, ref value, maxLength);
        else ImGui.InputTextWithHint(label, placeholder, ref value, maxLength);
        
        var finished = ImGui.IsItemDeactivatedAfterEdit();
        Hint(hint);
        return finished;
    }
    
    public override bool Swatch(string id, Vector4 color, bool selected, string? hint = null)
    {
        var size = ImGui.GetFrameHeight();
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
        var hovered = ImGui.IsItemHovered();

        var draw = ImGui.GetWindowDrawList();
        var style = ImGui.GetStyle();
        var rounding = style.FrameRounding;

        draw.AddRectFilled(pos, pos + new Vector2(size, size), ImGui.ColorConvertFloat4ToU32(color), rounding);
        draw.AddRect(pos, pos + new Vector2(size, size),
            ImGui.ColorConvertFloat4ToU32(style.Colors[(int)ImGuiCol.Border]), rounding);

        if (selected || hovered)
            draw.AddRect(pos - Vector2.One, pos + new Vector2(size + 1f, size + 1f),
                ImGui.ColorConvertFloat4ToU32(style.Colors[(int)ImGuiCol.Text] with { W = selected ? 0.95f : 0.4f }),
                rounding, ImDrawFlags.None, 2f);

        Hint(hint);
        return clicked;
    }
    
    public override bool ColorEdit(string label, ref Vector3 color)
    {
        return ImGui.ColorEdit3(label, ref color, ImGuiColorEditFlags.NoInputs);
    }
    
    public override bool Button(string label, string? hint = null)
    {
        var clicked = ImGui.Button(label);
        Hint(hint);
        return clicked;
    }
    
    public override bool MenuItem(string label, string? shortcut, bool selected, bool enabled)
    {
        return ImGui.MenuItem(label, shortcut, selected, enabled);
    }
    
    public override void Text(string text)
    {
        ImGui.TextUnformatted(text);
    }
    
    public override void Section(string title)
    {
        ImGui.SeparatorText(title);
    }
    
    public override void Separator()
    {
        ImGui.Separator();
    }
    
    public override void Note(string text)
    {
        ImGui.PushTextWrapPos(0f);
        ImGui.TextDisabled(text);
        ImGui.PopTextWrapPos();
    }
    
    public override void Hint(string? text)
    {
        if (text != null && ImGui.IsItemHovered()) ImGui.SetTooltip(text);
    }
}
