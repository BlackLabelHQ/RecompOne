namespace RecompOne.UI.BigPicture.Tabs;

public static class ShellDefaults
{
    public const string System = "menu.system";
    public const string Mods = "menu.mods";
    public const string Shaders = "menu.shaders";
    public const string Misc = "menu.misc";
    public const string Debug = "menu.debug";
    public const string Updates = "menu.updates";
    public const string Help = "menu.help";
    
    public static void Register()
    {
        ShellCatalog.Icon(System, FontSet.Microchip);
        ShellCatalog.Icon(Mods, FontSet.Puzzle);
        ShellCatalog.Icon(Shaders, FontSet.Wand);
        ShellCatalog.Icon(Misc, FontSet.Sliders);
        ShellCatalog.Icon(Debug, FontSet.Bug);
        ShellCatalog.Icon(Updates, FontSet.Download);
        ShellCatalog.Icon(Help, FontSet.Info);
        
        ShellCatalog.Icon("menu.mods.manage", FontSet.Puzzle);
        ShellCatalog.Icon("menu.mods.hub", FontSet.Download);
        ShellCatalog.Icon("menu.mods.asset_packs", FontSet.FolderOpen);
        ShellCatalog.Icon(ShaderPageSource.Installed, FontSet.Wand);
        ShellCatalog.Icon(ShaderPageSource.Browse, FontSet.Download);
        
        ShellCatalog.Paged(Misc);
        ShellCatalog.Paged(Mods);
        ShellCatalog.Pages(Shaders, ShaderPageSource.Pages);
        
        ShellCatalog.Hide("menu.system.settings");
        ShellCatalog.Hide("menu.system.show_menu_bar");
        ShellCatalog.Hide("menu.system.autohide_menu_bar");
        ShellCatalog.Hide("menu.system.quit");
    }
}
