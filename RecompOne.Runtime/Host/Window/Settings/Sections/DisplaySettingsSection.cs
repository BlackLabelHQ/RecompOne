using ImGuiNET;
using RecompOne.Runtime.Config;

namespace RecompOne.Runtime.Host.Window;

internal sealed class DisplaySettingsSection : ISettingsSection
{
    public string Id => "display";
    public string TitleKey => "settings.display";
    public int Order => 5;

    private static readonly string[] Backends = ["auto", "gl45", "gl33", "gl21"];

    public void Draw()
    {
        var fullscreen = ConfigManager.View.Fullscreen;
        if (ImGui.Checkbox(Localization.T("settings.display.fullscreen"), ref fullscreen))
        {
            ConfigManager.View.Fullscreen = fullscreen;
            HostWindow.SetFullscreen(fullscreen);
            ConfigManager.SaveView(PanelManager.Panels);
        }

        var vsync = ConfigManager.View.VSync;
        if (ImGui.Checkbox(Localization.T("settings.display.vsync"), ref vsync))
        {
            ConfigManager.View.VSync = vsync;
            HostWindow.SetVSync(vsync);
            ConfigManager.SaveView(PanelManager.Panels);
        }

        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Localization.T("settings.display.vsync_hint"));

        var scale = ConfigManager.View.RenderScale;
        if (ImGui.SliderInt(Localization.T("settings.display.render_scale"), ref scale, 1, 8, "%dx"))
        {
            ConfigManager.View.RenderScale = scale;
            ConfigManager.SaveView(PanelManager.Panels);
            NoticePopup.Show(Localization.T("common.restart_required"));
        }

        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Localization.T("settings.display.render_scale_hint"));

        var lines = Hle.GpuHle.LastDisplayH;
        var width = Hle.GpuHle.LastDisplayW;
        if (lines > 0)
            ImGui.TextDisabled(Localization.T("settings.display.render_scale_lines",
                width, lines, width * scale, lines * scale, scale));

        if (scale != Hle.GlVram.Scale)
            ImGui.TextDisabled(Localization.T("settings.display.restart_pending"));

        ImGui.Separator();

        var index = Array.IndexOf(Backends, ConfigManager.View.GpuBackend);
        if (index < 0) index = 0;
        if (ImGui.Combo(Localization.T("settings.display.backend"), ref index, Backends, Backends.Length))
        {
            ConfigManager.View.GpuBackend = Backends[index];
            ConfigManager.SaveView(PanelManager.Panels);
            NoticePopup.Show(Localization.T("common.restart_required"));
        }

        ImGui.TextDisabled(Localization.T("settings.display.backend_running", Hle.GpuBackendFactory.Selected));
    }
}