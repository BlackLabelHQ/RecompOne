
namespace RecompOne.UI;

public static class SettingsWidgets
{
    public static bool Toggle(Option<bool> option, string labelKey, string? hintKey = null)
    {
        var value = option.Value;
        var changed = ImGui.Checkbox(Localization.T(labelKey), ref value);
        if (changed) option.Value = value;
        Hint(hintKey);
        return changed;
    }

    public static bool Slider(Option<int> option, string labelKey, int min, int max, string format = "%d",
        string? hintKey = null)
    {
        var value = option.Value;
        var changed = ImGui.SliderInt(Localization.T(labelKey), ref value, min, max, format);
        if (changed) option.Value = Math.Clamp(value, min, max);
        Hint(hintKey);
        return changed;
    }

    public static bool Slider(Option<float> option, string labelKey, float min, float max, string format = "%.2f",
        string? hintKey = null)
    {
        var value = option.Value;
        var changed = ImGui.SliderFloat(Localization.T(labelKey), ref value, min, max, format);
        if (changed) option.Value = Math.Clamp(value, min, max);
        Hint(hintKey);
        return changed;
    }

    public static bool Combo<T>(Option<T> option, string labelKey, IReadOnlyList<(T Value, string Label)> items,
        string? hintKey = null)
    {
        var current = option.Value;
        var preview = current?.ToString() ?? "";
        foreach (var item in items)
            if (EqualityComparer<T>.Default.Equals(item.Value, current))
                preview = item.Label;

        var changed = false;
        if (ImGui.BeginCombo(Localization.T(labelKey), preview))
        {
            for (var i = 0; i < items.Count; i++)
            {
                var selected = EqualityComparer<T>.Default.Equals(items[i].Value, current);
                if (ImGui.Selectable($"{items[i].Label}##{i}", selected) && !selected)
                {
                    option.Value = items[i].Value;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        Hint(hintKey);
        return changed;
    }

    public static void Hint(string? hintKey)
    {
        if (hintKey != null && ImGui.IsItemHovered()) ImGui.SetTooltip(Localization.T(hintKey));
    }

    public static void Note(string key, params object[] args)
    {
        ImGui.PushTextWrapPos(0f);
        ImGui.TextDisabled(Localization.T(key, args));
        ImGui.PopTextWrapPos();
    }
}
