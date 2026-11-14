namespace RecompOne.UI.BigPicture.Tabs.Pages;

public static class MenuPageSource
{
    //what cant be embedded becomes a page of plain rows, so it doesnt break the view
    public static IReadOnlyList<ShellPage> Pages(MenuNode menu)
    {
        var pages = new List<ShellPage>();
        foreach (var entry in menu.Entries)
        {
            if (entry is MenuNode child) AddSubmenu(pages, child);
            else if (entry is MenuItemEntry item && (item.Enabled?.Invoke() ?? true)) pages.Add(Embedded(item) ?? Page(item.LabelKey, item.Draw));
        }
        
        return pages;
    }
    
    public static ShellPage Page(string labelKey, Action draw, Action? enter = null)
    {
        return new ShellPage(labelKey, Localization.T(labelKey), ShellCatalog.IconFor(labelKey, ShellCatalog.DefaultPageIcon), draw, enter);
    }
    
    private static void AddSubmenu(List<ShellPage> pages, MenuNode menu)
    {
        var rows = new List<MenuEntry>();
        foreach (var entry in menu.Entries)
        {
            if (entry is MenuItemEntry item && Embedded(item) is { } page) pages.Add(page);
            else rows.Add(entry);
        }
        
        if (rows.Count == 0) return;
        pages.Add(Page(menu.LabelKey, () =>
        {
            foreach (var row in rows) row.Draw();
        }));
    }
    
    private static ShellPage? Embedded(MenuItemEntry item)
    {
        if (item.Screen?.Invoke() is { } popup) return Page(item.LabelKey, popup.DrawEmbedded, popup.EnterEmbedded);
        return item.Panel?.Invoke() is IEmbeddedPanel panel ? Page(item.LabelKey, panel.DrawContent) : null;
    }
}
