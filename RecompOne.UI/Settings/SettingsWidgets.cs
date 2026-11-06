namespace RecompOne.UI;

public static class SettingsWidgets
{
    private static string? Text(string? key) => key == null ? null : Localization.T(key);
    
    public static bool Toggle(Option<bool> option, string labelKey, string? hintKey = null)
    {
        var value = option.Value;
        var changed = Ui.Toggle(Localization.T(labelKey), ref value, Text(hintKey));
        if (changed) option.Value = value;
        return changed;
    }

    public static bool Slider(Option<int> option, string labelKey, int min, int max, string format = "%d", string? hintKey = null)
    {
        var value = option.Value;
        var changed = Ui.Slider(Localization.T(labelKey), ref value, min, max, format, Text(hintKey));
        if (changed) option.Value = Math.Clamp(value, min, max);
        return changed;
    }

    public static bool Slider(Option<float> option, string labelKey, float min, float max, string format = "%.2f", string? hintKey = null)
    {
        var value = option.Value;
        var changed = Ui.Slider(Localization.T(labelKey), ref value, min, max, format, Text(hintKey));
        if (changed) option.Value = Math.Clamp(value, min, max);
        return changed;
    }

    public static bool Combo<T>(Option<T> option, string labelKey, IReadOnlyList<(T Value, string Label)> items, string? hintKey = null)
    {
        var current = option.Value;
        var index = -1;
        var labels = new string[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            labels[i] = items[i].Label;
            if (EqualityComparer<T>.Default.Equals(items[i].Value, current)) index = i;
        }

        var preview = index >= 0 ? labels[index] : current?.ToString() ?? "";
        var changed = Ui.Combo(Localization.T(labelKey), preview, labels, ref index, Text(hintKey));
        if (changed) option.Value = items[index].Value;
        return changed;
    }

    public static void Hint(string? hintKey)
    {
        Ui.Hint(Text(hintKey));
    }

    public static void Note(string key, params object[] args)
    {
        Ui.Note(Localization.T(key, args));
    }
}
