namespace RecompOne.UI.BigPicture.Tabs;

public class PagedTab(string id, string icon, string labelKey, Func<IReadOnlyList<ShellPage>> source) : ShellTab(id)
{
    private readonly List<RailItem> _items = [];
    private IReadOnlyList<ShellPage> _pages = [];
    private string? _entered;
    
    public int Page { get; private set; }
    
    public IReadOnlyList<ShellPage> Pages => _pages;
    
    public override string Icon => icon;
    
    public override string Label => Localization.T(labelKey);
    
    public override bool HasPages => true;
    
    public override void Refresh()
    {
        _pages = source();
        Page = Math.Clamp(Page, 0, Math.Max(0, _pages.Count - 1));
        _items.Clear();
        foreach (var page in _pages) _items.Add(new RailItem(page.Icon, page.Title));
    }
    
    public override void Activate(ShellNavigation navigation)
    {
        navigation.Focus.Move(ShellFocus.Pages);
    }
    
    public override void MovePage(int delta)
    {
        Page = Math.Clamp(Page + delta, 0, Math.Max(0, _pages.Count - 1));
    }
    
    public override void Leave()
    {
        _entered = null;
    }
    
    public override void Draw(ShellBody body)
    {
        var clicked = body.PageList(_items, Page);
        if (clicked >= 0) body.Navigation.Defer(() => Open(body.Navigation, clicked));
        if (_pages.Count == 0) return;
        
        var current = _pages[Page];
        if (_entered != current.Id)
        {
            _entered = current.Id;
            current.Enter?.Invoke();
        }
        
        body.Content($"##bp-page-{current.Id}", () =>
        {
            Ui.Section(current.Title);
            ImGui.PushID(current.Id);
            current.Draw();
            ImGui.PopID();
        });
    }
    
    private void Open(ShellNavigation navigation, int page)
    {
        Page = Math.Clamp(page, 0, Math.Max(0, _pages.Count - 1));
        navigation.Focus.Move(ShellFocus.Content);
    }
}
