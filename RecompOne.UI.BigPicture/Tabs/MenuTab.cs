namespace RecompOne.UI.BigPicture.Tabs;

public class MenuTab(MenuNode node) : ShellTab(node.LabelKey)
{
    private const string SubmenuSuffix = "   >";
    
    private readonly Stack<MenuNode> _path = new();
    
    public MenuNode Menu => _path.Count > 0 ? _path.Peek() : node;
    
    public override string Icon => node.Icon ?? ShellCatalog.IconFor(node.LabelKey, ShellCatalog.DefaultTabIcon);
    
    public override string Label => Localization.T(node.LabelKey);
    
    public override void Activate(ShellNavigation navigation)
    {
        _path.Clear();
        if (node.Entries.Count == 0 && node.Panel?.Invoke() is not IEmbeddedPanel)
        {
            node.OnClick?.Invoke();
            return;
        }
        
        navigation.Focus.Move(ShellFocus.Content);
    }
    
    public override bool Back(ShellNavigation navigation)
    {
        if (navigation.Focus.Area != ShellFocus.Content || _path.Count == 0) return false;
        _path.Pop();
        navigation.Focus.Reclaim();
        return true;
    }
    
    public override void Leave()
    {
        _path.Clear();
    }
    
    public override void Draw(ShellBody body)
    {
        var menu = Menu;
        body.Content($"##bp-menu{_path.Count}", () => DrawMenu(body.Navigation, menu));
    }
    
    protected virtual void DrawMenu(ShellNavigation navigation, MenuNode menu)
    {
        Ui.Section(Localization.T(menu.LabelKey));
        if (menu.Panel?.Invoke() is IEmbeddedPanel panel)
        {
            panel.DrawContent();
            return;
        }
        
        foreach (var entry in menu.Entries.ToArray())
        {
            if (ShellCatalog.Hidden(entry.Key)) continue;
            
            if (entry is not MenuNode child)
            {
                entry.Draw();
                continue;
            }
            
            var enabled = child.Enabled?.Invoke() ?? true;
            if (!Ui.MenuItem($"{Localization.T(child.LabelKey)}{SubmenuSuffix}", null, false, enabled)) continue;
            _path.Push(child);
            navigation.Focus.Move(ShellFocus.Content);
        }
    }
}
