using System.Numerics;

namespace RecompOne.UI;

public enum DialogChoice { None, Confirm, Cancel }

/// <summary>Te components every interface style has to provide. called trough <see cref="Ui"/></summary>
public abstract class UiComponents
{
    /// <summary>True when this style lays screens out as full row</summary>
    public abstract bool UsesRows { get; }
    
    /// <summary>Height of a <see cref="Selectable"/>, for lists that only draw what is visible.</summary>
    public abstract float SelectableHeight { get; }
    
    /// <summary>Entry of a list, marked when <paramref name="selected"/></summary>
    public abstract bool Selectable(string label, bool selected);
    
    public abstract CardResult DrawCard(string id, Card card);
    
    /// <summary>Buttons at the end of a dialog. No <paramref name="cancel"/> means a single button.</summary>
    public abstract DialogChoice Dialog(string confirm, string? cancel = null, bool danger = false, bool enabled = true);
    
    /// <summar> Starts a context menu, fill it with <see cref="MenuItem"/> and close it with <see cref="EndContextMenu"/>.</summary>
    public abstract bool BeginContextMenu(string id);
    
    public abstract void EndContextMenu();
    
    /// <summary>context menu thingy</summary>
    public abstract void MenuNote(string text);
    
    /// <summary>on\off switch</summary>
    public abstract bool Toggle(string label, ref bool value, string? hint = null);
    
    /// <summary>whole number slider</summary>
    public abstract bool Slider(string label, ref int value, int min, int max, string format = "%d", string? hint = null);
    
    /// <summary>decimal slider</summary>
    public abstract bool Slider(string label, ref float value, float min, float max, string format = "%.2f",
        string? hint = null);
    
    /// <summary>pick one of <paramref name="items"/>. This returns true when <paramref name="index"/> changes.</summary>
    public abstract bool Combo(string label, string preview, IReadOnlyList<string> items, ref int index, string? hint = null);
    
    /// <summary>aame as <see cref="Combo"/></summary>
    public abstract bool CompactCombo(string id, string preview, IReadOnlyList<string> items, ref int index);
    
    public abstract bool InputInt(string label, ref int value, string? hint = null);
    
    /// <summary>Editable text boxx.</summary>
    public abstract bool Input(string label, ref string value, uint maxLength = 256, string? placeholder = null, string? hint = null);
    
    /// <summary>Clickable color sample, (fix for bigpicture).</summary>
    public abstract bool Swatch(string id, Vector4 color, bool selected, string? hint = null);
    
    /// <summary>Free color choicer</summary>
    public abstract bool ColorEdit(string label, ref Vector3 color);
    
    /// <summary>Button.. It's a button, what do you expect? Return true when clicked</summary>
    public abstract bool Button(string label, string? hint = null);
    
    /// <summary>One entry of a menu, shown checked when <paramref name="selected"/></summary>
    public abstract bool MenuItem(string label, string? shortcut, bool selected, bool enabled); //true when pressed
    
    /// <summary>Draws a text, duh.</summary>
    public abstract void Text(string text);
    
    /// <summary>Title that opens a group of components.</summary>
    public abstract void Section(string title);
    
    /// <summary>Line between two groups of components.</summary>
    public abstract void Separator();
    
    /// <summary>Secondary text.</summary>
    public abstract void Note(string text);
    
    /// <summary>Explains the component drawn before this one.</summary>
    public abstract void Hint(string? text);
    
    public virtual ImGuiUtils.Scope Disabled(bool when = true) => ImGuiUtils.Disabled(when);
    
    /// <summary>The part of a label that is showed.</summary>
    protected static string VisibleLabel(string label)
    {
        var id = label.IndexOf("##", StringComparison.Ordinal);
        return id < 0 ? label : label[..id];
    }
    
    /// <summary>Draws the list of a combo.</summary>
    protected static bool ComboItems(IReadOnlyList<string> items, ref int index)
    {
        var changed = false;
        for (var i = 0; i < items.Count; i++)
        {
            var selected = i == index;
            if (!ImGui.Selectable($"{items[i]}##{i}", selected) || selected) continue;
            index = i;
            changed = true;
        }
        
        return changed;
    }
}
