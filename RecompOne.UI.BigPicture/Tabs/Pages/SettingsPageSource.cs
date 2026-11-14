namespace RecompOne.UI.BigPicture.Tabs.Pages;

public static class SettingsPageSource
{
    public static IReadOnlyList<ShellPage> Pages()
    {
        var pages = new List<ShellPage>();
        foreach (var page in SettingsPages.All)
            if (page.IsVisible)
                pages.Add(new ShellPage(page.Id, Localization.T(page.TitleKey), page.Icon, page.Draw));
        return pages;
    }
}
