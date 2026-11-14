namespace RecompOne.UI.BigPicture;

public class BigPictureInterface : ImGuiInterface
{
    private const string LanguagesResource = "RecompOne.UI.BigPicture.Assets.languages.json";
    
    private readonly ClassicComponents _classic = new();
    private bool _active;
    
    protected MenuInput Input { get; private set; } = null!;
    
    protected HintChannel Hints { get; private set; } = null!;
    
    protected BigPictureComponents Components { get; private set; } = null!;
    
    protected HostProfile Profile { get; private set; } = null!;
    
    protected ShellSession Session { get; private set; } = null!;
    
    protected ShellNavigation Navigation { get; private set; } = null!;
    
    protected BigPictureShell Shell { get; private set; } = null!;
    
    protected BackRouter Back { get; private set; } = null!;
    
    protected PopupPrimer Primer { get; private set; } = null!;
    
    protected MenuButtonOverlay Overlay { get; private set; } = null!;
    
    public bool ShellOpen => Session?.IsOpen == true;
    
    protected virtual MenuInput CreateInput() => new();
    
    protected virtual WidgetSet CreateWidgets() => new(Input, Hints);
    
    protected virtual BigPictureComponents CreateBigPictureComponents() => new(CreateWidgets());
    
    protected virtual IPopupStyle CreatePopupStyle() => new BigPicturePopupStyle(Input);
    
    protected virtual ShellTabs CreateTabs() => new();
    
    protected virtual BigPictureShell CreateShell() => new(Navigation, Input, Hints);
    
    protected override UiComponents CreateComponents() => _active ? Components : _classic;
    
    protected virtual void Compose()
    {
        Input = CreateInput();
        Hints = new HintChannel();
        Components = CreateBigPictureComponents();
        Profile = new HostProfile(CreatePopupStyle());
        Session = new ShellSession(Input);
        Navigation = new ShellNavigation(CreateTabs(), Input, Session.Resume);
        Shell = CreateShell();
        Back = new BackRouter(Session, Navigation, Input);
        Primer = new PopupPrimer(Input);
        Overlay = new MenuButtonOverlay(Session);
        Session.Opened += Navigation.Reset;
        Session.Opened += Shell.Reset;
    }
    
    public override void Load(HostContext host)
    {
        Compose();
        Profile.Measure(host);
        Localization.MergeEmbedded(typeof(BigPictureInterface).Assembly, LanguagesResource);
        ButtonGlyphs.Install();
        Onboarding.FirstBoot();
        
        _active = BigPictureOptions.Enabled;
        Profile.ApplyStyle(_active);
        base.Load(host);
    }
    
    protected override void RegisterPopups()
    {
        base.RegisterPopups();
        PopupManager.Register(new BigPictureAskPopup());
    }
    
    protected override void RegisterSettings()
    {
        base.RegisterSettings();
        SettingsPages.Page(SettingsPages.Interface).Add("big_picture", DrawToggle, -100);
    }
    
    private static void DrawToggle()
    {
        SettingsWidgets.Toggle(BigPictureOptions.Enabled, "settings.interface.big_picture", "settings.interface.big_picture_hint");
    }
    
    protected override void LoadFonts()
    {
        base.LoadFonts();
        ShellFonts.Icons = FontSet.LoadIcons();
    }
    
    protected override void ApplyFontSize()
    {
        if (_active) Profile.ApplyFontSize();
        else base.ApplyFontSize();
    }
    
    protected override void Configure()
    {
        base.Configure();
        Profile.ApplyFlags(_active);
    }
    
    public override void EndFrame()
    {
        base.EndFrame();
        if (BigPictureOptions.Enabled.Value != _active) Switch();
    }
    
    private void Switch()
    {
        PopupManager.CloseAll();
        PadFeed.Release();
        
        _active = !_active;
        Ui.Components = CreateComponents();
        Profile.ApplyStyle(_active);
        Profile.ApplyFlags(_active);
        ApplyFontSize();
        Theme.Apply();
        
        if (_active) Session.Open();
        else Session.Close();
    }
    
    public override void BeginFrame(double delta)
    {
        if (!_active)
        {
            base.BeginFrame(delta);
            return;
        }
        
        var pad = InputManager.MenuPad;
        var interacting = Session.IsOpen || PopupManager.AnyOpen;
        PadFeed.Feed(pad, interacting);
        
        if (Input.MenuPressed(pad) && !PopupManager.AnyOpen) Session.Toggle();
        
        Session.Update(pad, interacting);
        ShellStyle.Apply();
        base.BeginFrame(delta);
    }
    
    public override void Draw()
    {
        Onboarding.AskOnce();
        if (!_active)
        {
            base.Draw();
            return;
        }
        
        Input.Track(InputManager.MenuPad);
        Back.Update();
        OutputPanel.ShowToasts = !Session.IsOpen;
        MainDock.Draw(true);
        PanelManager.DrawPanels();
        
        if (Session.IsOpen) Shell.Draw();
        else Overlay.Draw();
        Onboarding.ShowMenuHint(Session);
        
        MainDock.SendToBack(); //fix
        
        if (PopupManager.AnyOpen) Input.ShowCursor();
        PopupManager.Draw();
        Primer.Update();
    }
}
