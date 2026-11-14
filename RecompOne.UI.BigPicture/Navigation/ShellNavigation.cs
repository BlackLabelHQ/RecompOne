namespace RecompOne.UI.BigPicture.Navigation;

public class ShellNavigation(ShellTabs tabs, MenuInput input, Action resume)
{
    private readonly List<Action> _deferred = [];
    private IReadOnlyList<ShellTab> _tabs = [];
    private string? _selected;
    
    public FocusController Focus { get; } = new();
    
    public IReadOnlyList<ShellTab> Tabs => _tabs;
    
    public int Index { get; private set; }
    
    public ShellTab Current => _tabs[Index];
    
    //runs every frame since games and mods can add menus at any time, not very performative eh
    public void Refresh()
    {
        _tabs = tabs.Build();
        var index = Find(_selected);
        Index = index >= 0 ? index : Math.Clamp(Index, 0, _tabs.Count - 1);
        _selected = Current.Id;
        Current.Refresh();
    }
    
    public void Reset()
    {
        foreach (var tab in _tabs) tab.Leave();
        _tabs = tabs.Build();
        Index = 0;
        _selected = Current.Id;
        Current.Refresh();
        Focus.Move(ShellFocus.Rail);
    }
    
    public void Select(int index)
    {
        index = Math.Clamp(index, 0, _tabs.Count - 1);
        if (index == Index) return;
        Current.Leave();
        Index = index;
        _selected = Current.Id;
        Current.Refresh();
    }
    
    public void Activate()
    {
        Current.Activate(this);
    }
    
    public void Back()
    {
        if (Current.Back(this)) return;
        
        switch (Focus.Area)
        {
            case ShellFocus.Content:
                Focus.Move(Current.HasPages ? ShellFocus.Pages : ShellFocus.Rail);
                break;
            case ShellFocus.Pages:
                Focus.Move(ShellFocus.Rail);
                break;
            default:
                Resume();
                break;
        }
    }
    
    public void Resume()
    {
        resume();
    }
    
    public void Defer(Action action)
    {
        _deferred.Add(action);
    }
    
    public void Flush()
    {
        if (_deferred.Count == 0) return;
        var pending = _deferred.ToArray();
        _deferred.Clear();
        foreach (var action in pending) action();
    }
    
    public void Update()
    {
        if (PopupManager.AnyOpen || input.WidgetBusy) return;
        
        if (input.PreviousTab || input.NextTab)
        {
            Select(Index + (input.NextTab ? 1 : -1));
            Focus.Move(ShellFocus.Rail);
            return;
        }
        
        if (Focus.Area == ShellFocus.Rail) UpdateRail();
        else if (Focus.Area == ShellFocus.Pages) UpdatePages();
    }
    
    protected virtual void UpdateRail()
    {
        if (input.Up) Select(Index - 1);
        if (input.Down) Select(Index + 1);
        if (input.Accept) Activate();
    }
    
    protected virtual void UpdatePages()
    {
        if (input.Up) Current.MovePage(-1);
        if (input.Down) Current.MovePage(1);
        if (input.Accept) Focus.Move(ShellFocus.Content);
    }
    
    private int Find(string? id)
    {
        if (id == null) return -1;
        for (var i = 0; i < _tabs.Count; i++)
            if (_tabs[i].Id == id)
                return i;
        return -1;
    }
}
