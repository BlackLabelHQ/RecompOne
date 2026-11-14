namespace RecompOne.UI.BigPicture.Tabs;

public static class ShellCatalog
{
    private static readonly Dictionary<string, string> _icons = [];
    private static readonly Dictionary<string, Func<IReadOnlyList<ShellPage>>> _sources = [];
    private static readonly HashSet<string> _paged = [];
    private static readonly HashSet<string> _hidden = [];
    private static readonly List<ShellTab> _extra = [];
    
    static ShellCatalog()
    {
        ShellDefaults.Register();
    }
    
    public static string DefaultTabIcon { get; set; } = FontSet.Ellipsis;
    
    public static string DefaultPageIcon { get; set; } = FontSet.Sliders;
    
    public static int Version { get; private set; }
    
    public static IReadOnlyList<ShellTab> Extra => _extra;
    
    public static void Icon(string key, string icon)
    {
        _icons[key] = icon;
        Version++;
    }
    
    public static string IconFor(string key, string fallback)
    {
        return _icons.GetValueOrDefault(key, fallback);
    }
    
    public static void Paged(string menuKey)
    {
        _paged.Add(menuKey);
        Version++;
    }
    
    public static void Pages(string menuKey, Func<IReadOnlyList<ShellPage>> source)
    {
        _sources[menuKey] = source;
        Version++;
    }
    
    public static void Hide(string key)
    {
        _hidden.Add(key);
    }
    
    public static void Show(string key)
    {
        _hidden.Remove(key);
    }
    
    public static bool Hidden(string? key) => key != null && _hidden.Contains(key);
    
    public static void Add(ShellTab tab)
    {
        _extra.RemoveAll(t => t.Id == tab.Id);
        _extra.Add(tab);
    }
    
    public static bool Remove(string id)
    {
        return _extra.RemoveAll(t => t.Id == id) > 0;
    }
    
    public static ShellTab Create(MenuNode root)
    {
        var key = root.LabelKey;
        var icon = root.Icon ?? IconFor(key, DefaultTabIcon);
        if (_sources.TryGetValue(key, out var source)) return new PagedTab(key, icon, key, source);
        if (_paged.Contains(key)) return new PagedTab(key, icon, key, () => MenuPageSource.Pages(root));
        return new MenuTab(root);
    }
}
