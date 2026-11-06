namespace RecompOne.UI;

public static class SettingsPages
{
    public const string Interface = "interface";
    public const string Language = "language";
    public const string Input = "input";
    public const string Display = "display";
    public const string Audio = "audio";
    public const string Paths = "paths";
    public const string Shaders = "shaders";

    private const int UndeclaredOrder = 100;

    private static readonly List<SettingsPage> _pages = [];
    private static bool _sorted = true;

    public static SettingsPage Page(string id, string titleKey, int order)
    {
        var page = Page(id);
        page.TitleKey = titleKey;
        page.Order = order;
        _sorted = false;
        return page;
    }

    public static SettingsPage Page(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var page = _pages.Find(p => p.Id == id);
        if (page != null) return page;

        page = new SettingsPage(id, $"settings.{id}", UndeclaredOrder);
        _pages.Add(page);
        _sorted = false;
        return page;
    }

    public static bool Remove(string id)
    {
        return _pages.RemoveAll(p => p.Id == id) > 0;
    }

    public static void Open(string? id = null)
    {
        SettingsPopup.Select(id);
        PopupManager.Open<SettingsPopup>();
    }

    public static IReadOnlyList<SettingsPage> All
    {
        get
        {
            if (!_sorted)
            {
                _pages.Sort(static (a, b) => a.Order != b.Order
                    ? a.Order.CompareTo(b.Order)
                    : string.CompareOrdinal(a.Id, b.Id));
                _sorted = true;
            }

            return _pages;
        }
    }
}
