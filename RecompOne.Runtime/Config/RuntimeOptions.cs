namespace RecompOne.Runtime.Config;

public static class InterfaceOptions
{
    public static readonly Option<string> Language = Options.String("interface.language");
    public static readonly Option<string> Accent = Options.String("interface.accent");
    public static readonly Option<string> Background = Options.String("interface.background");

    public static readonly Option<float> Scale = Options.Float("interface.scale", 1f)
        .Normalized(static v => Math.Clamp(v, 0.5f, 3f));

    public static readonly Option<bool> ShowFps = Options.Bool("interface.show_fps");
    public static readonly Option<bool> HideMenuBar = Options.Bool("interface.hide_menu_bar");
    public static readonly Option<bool> AutoHideMenuBar = Options.Bool("interface.autohide_menu_bar");
}

public static class DisplayOptions
{
    public static readonly Option<bool> Fullscreen = Options.Bool("display.fullscreen");
    public static readonly Option<bool> VSync = Options.Bool("display.vsync");

    public static readonly Option<int> RenderScale = Options.Int("display.render_scale", 4)
        .Normalized(static v => Math.Clamp(v, 1, 8));

    public static readonly Option<string> Backend = Options.String("display.backend", "auto");

    public static readonly Option<bool> Dithering = Options.Bool("display.dithering", true);
    public static readonly Option<bool> TrueColor = Options.Bool("display.true_color");
}
