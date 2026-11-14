namespace RecompOne.UI.BigPicture.Tabs.Pages;

public static class ShaderPageSource
{
    public const string Installed = "shaders.installed";
    public const string Browse = "shaders.browse";
    
    public static IReadOnlyList<ShellPage> Pages()
    {
        if (PopupManager.Get<ShadersPopup>() is not { } popup) return [];
        return
        [
            MenuPageSource.Page(Installed, popup.DrawInstalled, popup.EnterEmbedded),
            MenuPageSource.Page(Browse, popup.DrawBrowse, popup.EnterEmbedded)
        ];
    }
}
