using RecompOne.Runtime.Config;

namespace RecompOne.Runtime.Host.Window;

public static class MainMenuBar
{
    static bool _registered;

    public static void RegisterBuiltins()
    {
        if (_registered) return;
        _registered = true;

        MenuRegistry.Menu("menu.system")
            .Popup<SettingsPopup>("menu.system.settings")
            .Separator("menu.system.view_separator")
            .Check("menu.system.show_menu_bar", () => !ConfigManager.View.HideTopBar, ShowMenuBar, "F1")
            .Check("menu.system.autohide_menu_bar", () => ConfigManager.View.AutoHideMenuBar, AutoHideMenuBar).Disabled()
            .Check("menu.system.fullscreen", () => ConfigManager.View.Fullscreen, Fullscreen, "F11")
            .Separator("menu.system.reset_separator")
            .Item("menu.system.hard_reset", Runtime.HardReset)
            .Separator("menu.system.quit_separator")
            .Item("menu.system.quit", static () => Environment.Exit(0));

        MenuRegistry.Menu("menu.mods").After("menu.system")
            .Popup<ModsPopup>("menu.mods.manage")
            .Item("menu.mods.hub", static () => { }).Disabled();

        MenuRegistry.Menu("menu.debug")
            .Submenu("menu.debug.gpu")
                .Panel<OutputPanel>("panel.output")
                .Panel<VramViewerPanel>("panel.vram_viewer")
                .Panel<TextureInspectorPanel>("panel.texture_inspector")
                .Separator()
                .Check("menu.debug.dump_textures",
                    static () => Assets.Textures.TextureDumper.Tiles,
                    static on => Assets.Textures.TextureDumper.SetTiles(on))
                    .Tooltip("menu.debug.dump_textures.tooltip")
                .Check("menu.debug.dump_pages",
                    static () => Assets.Textures.TextureDumper.Pages,
                    static on => Assets.Textures.TextureDumper.SetPages(on))
                    .Tooltip("menu.debug.dump_pages.tooltip")
                .End()
            .Submenu("menu.debug.cpu")
                .Panel<CpuStatePanel>("panel.cpu_state")
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
            .Submenu("menu.debug.input")
                .Panel<InputRollPanel>("panel.input_roll")
                .Separator()
                .Custom(DrawRecordItem)
                .Submenu("menu.debug.input.play")
                    .Custom(DrawPlayList)
                    .End()
                .Separator()
                .Custom(DrawRecorderStatus)
                .End()
            .Submenu("menu.debug.system")
                .Panel<OverlayEventsPanel>("panel.overlay_events")
                .Panel<ConsolePanel>("panel.console")
                .End()
            .Separator()
            .Item("menu.debug.reset_view", ResetView);
    }

    public static void Draw()
    {
        MenuRegistry.RightAligned = DrawFps;
        MenuRegistry.Draw();
    }

    static void DrawFps()
    {
        if (!ConfigManager.View.ShowFps) return;

        string text = $"{FrameClock.Fps:F1} fps";
        float width = ImGuiNET.ImGui.CalcTextSize(text).X;
        ImGuiNET.ImGui.SameLine(ImGuiNET.ImGui.GetWindowWidth() - width - ImGuiNET.ImGui.GetStyle().FramePadding.X * 2f);
        ImGuiNET.ImGui.TextUnformatted(text);
    }


    static void DrawRecordItem()
    {
        bool recording = InputRecorder.State == InputRecorder.Mode.Recording;
        if (ImGuiNET.ImGui.MenuItem(Localization.T(recording ? "menu.debug.input.stop_record" : "menu.debug.input.record"), "", recording))
        {
            if (recording) InputRecorder.Stop();
            else InputRecorder.StartRecording();
        }
    }

    static void DrawPlayList()
    {
        var names = InputRecorder.Available();
        if (names.Count == 0)
        {
            ImGuiNET.ImGui.TextDisabled(Localization.T("menu.debug.input.empty"));
            return;
        }

        bool playing = InputRecorder.State == InputRecorder.Mode.Playing;
        foreach (var name in names)
            if (ImGuiNET.ImGui.MenuItem(name, "", playing && InputRecorder.Name == name))
            {
                if (playing && InputRecorder.Name == name) InputRecorder.Stop();
                else InputRecorder.StartPlayback(name);
            }
    }

    static void DrawRecorderStatus()
    {
        switch (InputRecorder.State)
        {
            case InputRecorder.Mode.Recording:
                ImGuiNET.ImGui.TextDisabled(Localization.T("menu.debug.input.recording", InputRecorder.Name, InputRecorder.Frame));
                break;
            case InputRecorder.Mode.Playing:
                ImGuiNET.ImGui.TextDisabled(Localization.T("menu.debug.input.playing", InputRecorder.Name, InputRecorder.Frame, InputRecorder.Length));
                break;
            default:
                ImGuiNET.ImGui.TextDisabled(Localization.T("menu.debug.input.idle"));
                break;
        }
    }

    static void ResetView()
    {
        ConfigManager.ResetView(PanelManager.Panels);
        HostWindow.RequestLayout();
    }

    static void ShowMenuBar(bool show)
    {
        ConfigManager.View.HideTopBar = !show;
        ConfigManager.SaveView(PanelManager.Panels);
    }

    static void AutoHideMenuBar(bool autoHide)
    {
        ConfigManager.View.AutoHideMenuBar = autoHide;
        ConfigManager.SaveView(PanelManager.Panels);
    }

    static void Fullscreen(bool fullscreen)
    {
        ConfigManager.View.Fullscreen = fullscreen;
        HostWindow.SetFullscreen(fullscreen);
        ConfigManager.SaveView(PanelManager.Panels);
    }
}
