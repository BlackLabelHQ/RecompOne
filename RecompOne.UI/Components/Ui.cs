using System.Numerics;

namespace RecompOne.UI;

public static class Ui
{
    public static UiComponents Components { get; set; } = new ClassicComponents();
    
    public static bool UsesRows => Components.UsesRows;
    
    public static float SelectableHeight => Components.SelectableHeight;
    
    public static bool Selectable(string label, bool selected) => Components.Selectable(label, selected);
    
    public static bool Toggle(string label, ref bool value, string? hint = null) => Components.Toggle(label, ref value, hint);
    
    public static bool Slider(string label, ref int value, int min, int max, string format = "%d", string? hint = null) => Components.Slider(label, ref value, min, max, format, hint);
    
    public static bool Slider(string label, ref float value, float min, float max, string format = "%.2f", string? hint = null) => Components.Slider(label, ref value, min, max, format, hint);
    
    public static bool Combo(string label, string preview, IReadOnlyList<string> items, ref int index, string? hint = null) => Components.Combo(label, preview, items, ref index, hint);
    
    public static bool CompactCombo(string id, string preview, IReadOnlyList<string> items, ref int index) => Components.CompactCombo(id, preview, items, ref index);
    
    public static bool Input(string label, ref string value, uint maxLength = 256, string? placeholder = null, string? hint = null) => Components.Input(label, ref value, maxLength, placeholder, hint);
    
    public static bool Swatch(string id, Vector4 color, bool selected, string? hint = null) => Components.Swatch(id, color, selected, hint);
    
    public static bool ColorEdit(string label, ref Vector3 color) => Components.ColorEdit(label, ref color);
    
    public static bool Button(string label, string? hint = null) => Components.Button(label, hint);
    
    public static bool MenuItem(string label, string? shortcut, bool selected, bool enabled) => Components.MenuItem(label, shortcut, selected, enabled);
    
    public static void Text(string text) => Components.Text(text);
    public static void Section(string title) => Components.Section(title);
    public static void Separator() => Components.Separator();
    public static void Note(string text) => Components.Note(text);
    
    public static void Hint(string? text) => Components.Hint(text);
    public static ImGuiUtils.Scope Disabled(bool when = true) => Components.Disabled(when);
}
