using Silk.NET.OpenGL;

namespace RecompOne.UI;

using Runtime = RecompOne.Runtime.Runtime;

public class ImGuiInterface : IUserInterface
{
    private ImGuiController? _imgui;
    
    public virtual bool BlocksStartup => StartupNoticePopup.NeedsAck;
    
    public virtual bool BlocksExit => CrashPopup.IsPending || PopupManager.Get<CrashPopup>()?.IsOpen == true;
    
    public virtual void Load(HostContext host)
    {
        InterfaceLayout.Load();
        _imgui = new ImGuiController(host.Gl, host.Window, host.Input, Configure);
        
        RegisterPanels();
        RegisterPopups();
        RegisterMenus();
        RegisterSettings();
        
        InterfaceLayout.ApplyPanels(PanelManager.Panels);
        
        var cdPath = Settings.System.DiscPath;
        if (string.IsNullOrWhiteSpace(cdPath) || !File.Exists(cdPath) || Runtime.ValidateDisc(cdPath) != null)
            PopupManager.Open<DiscPickerPopup>();
    }
    
    protected virtual void Configure()
    {
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.ConfigWindowsMoveFromTitleBarOnly = true;
        io.FontGlobalScale = InterfaceOptions.Scale;
        unsafe
        {
            io.NativePtr->IniFilename = null;
        }
        
        FontSet.Load(16f * HostWindow.DpiScale);
        Localization.Load();
        Theme.Load();
        
        if (InterfaceLayout.ApplyImGui()) MainDock.CancelLayout();
    }
    
    protected virtual void RegisterPanels()
    {
        PanelManager.Register(new OutputPanel());
        PanelManager.Register(new VramViewerPanel());
        PanelManager.Register(new TextureInspectorPanel());
        PanelManager.Register(new CpuStatePanel());
        PanelManager.Register(new RamMapPanel());
        PanelManager.Register(new MemoryEditorPanel());
        PanelManager.Register(new SpuViewerPanel());
        PanelManager.Register(new CdDebugPanel());
        PanelManager.Register(new InterpDebugPanel());
        PanelManager.Register(new ConsolePanel());
        PanelManager.Register(new OverlayEventsPanel());
        PanelManager.Register(new AssetPackEditor());
    }
    
    protected virtual void RegisterPopups()
    {
        PopupManager.Register(new SettingsPopup());
        PopupManager.Register(new ModsPopup());
        PopupManager.Register(new ModHubPopup());
        PopupManager.Register(new AssetPacksPopup());
        PopupManager.Register(new NewPackPopup());
        PopupManager.Register(new ConfirmPopup());
        PopupManager.Register(new ModLoadingPopup());
        PopupManager.Register(new NoticePopup());
        PopupManager.Register(new CrashPopup());
        PopupManager.Register(new StartupNoticePopup());
        PopupManager.Register(new DiscPickerPopup());
        PopupManager.Register(new ShadersPopup());
        if (AutoUpdater.Configured) PopupManager.Register(new UpdatePopup());
    }
    
    protected virtual void RegisterMenus()
    {
        MainMenuBar.RegisterBuiltins();
    }
    
    protected virtual void RegisterSettings()
    {
        InterfacePage.Register();
        LanguagePage.Register();
        InputPage.Register();
        DisplayPage.Register();
        AudioPage.Register();
        PathsPage.Register();
        ShadersPage.Register();
    }
    
    public virtual void BeginFrame(double delta)
    {
        _imgui!.Update((float)delta);
        Autohide.Update();
        ToolUi.Pump(); //update thread state
        
        var background = Theme.Background;
        HostWindow.ClearColor = new System.Numerics.Vector3(background.X, background.Y, background.Z);
        
        var ramOpen = PanelManager.Get<RamMapPanel>()?.IsOpen == true;
        HostOutput.VramEnabled = PanelManager.Get<VramViewerPanel>()?.IsOpen == true;
        HostOutput.RamEnabled = ramOpen;
        HostOutput.MemoryTracking = ramOpen || PanelManager.Get<MemoryEditorPanel>()?.IsOpen == true;
    }
    
    public virtual void Draw()
    {
        var overlay = Autohide.BarOverlay;
        if (!overlay && Autohide.BarVisible) MainMenuBar.Draw();
        
        MainDock.Draw(overlay);
        PanelManager.DrawPanels();
        if (overlay && Autohide.BarVisible) MainMenuBar.Draw();
        PopupManager.Draw();
    }
    
    public virtual void EndFrame()
    {
        _imgui!.Render();
    }
    
    public virtual void Close()
    {
        InterfaceLayout.Save(PanelManager.Panels);
        PanelManager.Shutdown();
        PopupManager.Shutdown();
        _imgui?.Dispose();
        _imgui = null;
    }
    
    public virtual void ShowNotice(string message)
    {
        NoticePopup.Show(message);
    }
    
    public virtual void ReportCrash(Exception error)
    {
        CrashPopup.Report(error);
    }
    
    public virtual void BeginModLoading(int total)
    {
        ModLoadingPopup.Begin(total);
    }
    
    public virtual void UpdateModLoading(int current, string name)
    {
        ModLoadingPopup.Update(current, name);
    }
    
    public virtual void EndModLoading()
    {
        ModLoadingPopup.End();
    }
}
