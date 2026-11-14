namespace RecompOne.UI.BigPicture.Tabs;

public class ShellTabs
{
    public const string Resume = "shell.resume";
    public const string Settings = "settings.title";
    public const string Quit = "quit.confirm";
    
    private readonly Dictionary<MenuNode, ShellTab> _menus = [];
    private readonly List<ShellTab> _tabs = [];
    private readonly ShellTab _resume;
    private readonly ShellTab _settings;
    private readonly ShellTab _quit;
    private int _version = -1;
    
    public ShellTabs()
    {
        _resume = CreateResume();
        _settings = CreateSettings();
        _quit = CreateQuit();
    }
    
    public virtual IReadOnlyList<ShellTab> Build()
    {
        var roots = MenuRegistry.Roots;
        if (_version != ShellCatalog.Version || _menus.Count > roots.Count)
        {
            _version = ShellCatalog.Version;
            _menus.Clear();
        }
        
        _tabs.Clear();
        _tabs.Add(_resume);
        _tabs.Add(_settings);
        foreach (var root in roots) _tabs.Add(For(root));
        foreach (var tab in ShellCatalog.Extra) _tabs.Add(tab);
        _tabs.Add(_quit);
        return _tabs;
    }
    
    protected virtual ShellTab CreateResume()
    {
        return new ActionTab(Resume, FontSet.Play, Resume, static navigation => navigation.Resume());
    }
    
    protected virtual ShellTab CreateSettings()
    {
        return new PagedTab(Settings, FontSet.Gear, Settings, SettingsPageSource.Pages);
    }
    
    protected virtual ShellTab CreateQuit()
    {
        return new ActionTab(Quit, FontSet.Power, Quit, static _ => QuitPrompt.Ask(), true);
    }
    
    private ShellTab For(MenuNode root)
    {
        if (_menus.TryGetValue(root, out var tab)) return tab;
        tab = ShellCatalog.Create(root);
        _menus[root] = tab;
        return tab;
    }
}
