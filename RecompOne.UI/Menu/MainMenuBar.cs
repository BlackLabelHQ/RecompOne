
namespace RecompOne.UI;

using Runtime = RecompOne.Runtime.Runtime;

public static class MainMenuBar
{
    private static bool _registered;

    public static void RegisterBuiltins()
    {
        if (_registered) return;
        _registered = true;

        MenuRegistry.Menu("menu.system")
            .Popup<SettingsPopup>("menu.system.settings")
            .Separator("menu.system.view_separator")
            .Check("menu.system.show_menu_bar", static () => !InterfaceOptions.HideMenuBar, static show => InterfaceOptions.HideMenuBar.Value = !show, "F1")
            .Check("menu.system.autohide_menu_bar", static () => InterfaceOptions.AutoHideMenuBar, static on => InterfaceOptions.AutoHideMenuBar.Value = on)
            .Tooltip("menu.system.autohide_menu_bar.tooltip")
            .Check("menu.system.fullscreen", static () => DisplayOptions.Fullscreen, Fullscreen, "F11")
            .Separator("menu.system.reset_separator")
            .Item("menu.system.hard_reset", Runtime.HardReset)
            .Separator("menu.system.quit_separator")
            .Item("menu.system.quit", QuitPrompt.Ask);

        MenuRegistry.Menu("menu.mods").After("menu.system")
            .Popup<ModsPopup>("menu.mods.manage")
            .Popup<ModHubPopup>("menu.mods.hub").Enabled(static () => ModHub.Configured)
            .Popup<AssetPacksPopup>("menu.mods.asset_packs");

        MenuRegistry.BarItem("menu.shaders", PopupManager.Open<ShadersPopup>).After("menu.mods");

        MenuRegistry.Menu("menu.debug")
            .Submenu("menu.debug.gpu")
            .Panel<OutputPanel>("panel.output")
            .Panel<VramViewerPanel>("panel.vram_viewer")
            .Panel<TextureInspectorPanel>("panel.texture_inspector")
            .Panel<InterpDebugPanel>("panel.interp_debug")
            .Separator()
            .Check("menu.debug.dump_textures",
                static () => TextureDumper.Tiles,
                static on => TextureDumper.SetTiles(on))
            .Tooltip("menu.debug.dump_textures.tooltip")
            .Check("menu.debug.dump_pages",
                static () => TextureDumper.Pages,
                static on => TextureDumper.SetPages(on))
            .Tooltip("menu.debug.dump_pages.tooltip")
            .End()
            .Submenu("menu.debug.cpu")
            .Panel<CpuStatePanel>("panel.cpu_state")
            .Separator()
            .Custom(DrawTurbo)
            .End()
            .Submenu("menu.debug.memory")
            .Panel<RamMapPanel>("panel.ram_map")
            .Panel<MemoryEditorPanel>("panel.memory_editor")
            .End()
            .Submenu("menu.debug.audio")
            .Panel<SpuViewerPanel>("panel.spu_viewer")
            .End()
            .Submenu("menu.debug.cd")
            .Panel<CdDebugPanel>("panel.cd_debug")
            .End()
            .Submenu("menu.debug.system")
            .Panel<OverlayEventsPanel>("panel.overlay_events")
            .Panel<ConsolePanel>("panel.console")
            .End()
            .Separator()
            .Item("menu.debug.reset_view", ResetView);

        if (!AutoUpdater.Configured) return;

        MenuRegistry.Menu("menu.updates").After("menu.debug")
            .Text(UpdatePopup.StatusLine)
            .Separator()
            .Check("menu.updates.on_startup",
                static () => AutoUpdater.CheckOnStartup,
                static enabled => AutoUpdater.CheckOnStartup.Value = enabled)
            .Item("menu.updates.check_now", AutoUpdater.CheckNow)
            .Enabled(static () => AutoUpdater.Phase is UpdatePhase.Idle or UpdatePhase.Available)
            .Item("menu.updates.releases_page", AutoUpdater.OpenReleasesPage);
    }

    public static void Draw()
    {
        MenuRegistry.RightAligned = DrawFps;
        MenuRegistry.Draw();
    }

    private static void DrawTurbo()
    {
        var turbo = Interrupts.Turbo;

        if (ImGuiNET.ImGui.MenuItem("Turbo", null, turbo))
        {
            Interrupts.Turbo = !turbo;
            if (!Interrupts.Turbo) Interrupts.ResyncVBlank();
        }
    }

    private static void DrawFps()
    {
        if (!InterfaceOptions.ShowFps) return;

        var text = $"{FrameClock.PresentFps:F0} / {FrameClock.Fps:F0} fps";
        var width = ImGuiNET.ImGui.CalcTextSize(text).X;
        ImGuiNET.ImGui.SameLine(ImGuiNET.ImGui.GetWindowWidth() - width -
                                ImGuiNET.ImGui.GetStyle().FramePadding.X * 2f);
        ImGuiNET.ImGui.TextUnformatted(text);
    }


    private static void ResetView()
    {
        InterfaceLayout.Reset(PanelManager.Panels);
        MainDock.RequestLayout();
    }

    private static void Fullscreen(bool fullscreen)
    {
        DisplayOptions.Fullscreen.Value = fullscreen;
        HostWindow.SetFullscreen(fullscreen);
    }
}