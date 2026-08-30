namespace RecompOne.Runtime.Host.Window;

public static class SettingsRegistry
{
    private static readonly List<ISettingsSection> _sections = [];
    private static readonly Dictionary<string, List<Action>> _extensions = new(StringComparer.OrdinalIgnoreCase);
    private static bool _dirty;

    public static void Register(ISettingsSection section)
    {
        if (section == null) return;
        _sections.RemoveAll(s => s.Id == section.Id);
        _sections.Add(section);
        _dirty = true;
    }

    public static void Unregister(string id)
    {
        _sections.RemoveAll(s => s.Id == id);
    }

    public static void Extend(string sectionId, Action draw)
    {
        if (!_extensions.TryGetValue(sectionId, out var list))
        {
            list = [];
            _extensions[sectionId] = list;
        }

        list.Add(draw);
    }

    internal static IReadOnlyList<Action> GetExtensions(string sectionId)
    {
        return _extensions.TryGetValue(sectionId, out var list) ? list : [];
    }

    public static IReadOnlyList<ISettingsSection> Sections
    {
        get
        {
            if (_dirty)
            {
                _sections.Sort((a, b) => a.Order != b.Order
                    ? a.Order.CompareTo(b.Order)
                    : string.Compare(a.TitleKey, b.TitleKey, StringComparison.Ordinal));
                _dirty = false;
            }

            return _sections;
        }
    }
}