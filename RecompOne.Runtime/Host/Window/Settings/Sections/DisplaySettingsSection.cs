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

        ImGui.Separator();
        DrawPgxp();
    }

    static void DrawPgxp()
    {
        ImGui.TextUnformatted(Localization.T("settings.display.pgxp"));

        var enabled = ConfigManager.View.GetBool(Pgxp.Pgxp.KeyEnable);
        if (ImGui.Checkbox(Localization.T("settings.display.pgxp_enable"), ref enabled))
        {
            ConfigManager.View.SetBool(Pgxp.Pgxp.KeyEnable, enabled);
            ConfigManager.SaveView(PanelManager.Panels);
            Pgxp.Pgxp.Load();
            Pgxp.PgxpGte.Invalidate();
        }

        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Localization.T("settings.display.pgxp_enable_hint"));

        if (!enabled) ImGui.BeginDisabled();

        var column = ImGui.GetContentRegionAvail().X / 3f;

        Toggle(Pgxp.Pgxp.KeyTextureCorrection, "settings.display.pgxp_texture", "settings.display.pgxp_texture_hint",
            true);
        ImGui.SameLine(column);
        Toggle(Pgxp.Pgxp.KeyCulling, "settings.display.pgxp_culling", "settings.display.pgxp_culling_hint", true);
        ImGui.SameLine(column * 2f);
        Toggle(Pgxp.Pgxp.KeyCpu, "settings.display.pgxp_cpu", "settings.display.pgxp_cpu_hint", false);

        Toggle(Pgxp.Pgxp.KeyVertexCache, "settings.display.pgxp_vertex_cache",
            "settings.display.pgxp_vertex_cache_hint", false);
        ImGui.SameLine(column);
        Toggle(Pgxp.Pgxp.KeyMemory, "settings.display.pgxp_memory", "settings.display.pgxp_memory_hint", true);
        ImGui.SameLine(column * 2f);
        Toggle(Pgxp.Pgxp.KeyCacheW, "settings.display.pgxp_cache_w", "settings.display.pgxp_cache_w_hint", true);

        var tolerance = ConfigManager.View.GetFloat(Pgxp.Pgxp.KeyTolerance, -1f);
        if (ImGui.SliderFloat(Localization.T("settings.display.pgxp_tolerance"), ref tolerance, -1f, 10f, tolerance < 0f ? Localization.T("settings.display.pgxp_tolerance_off") : "%.2f"))
        {
            ConfigManager.View.SetFloat(Pgxp.Pgxp.KeyTolerance, tolerance);
            ConfigManager.SaveView(PanelManager.Panels);
            Pgxp.Pgxp.Load();
        }

        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Localization.T("settings.display.pgxp_tolerance_hint"));

        if (!enabled) ImGui.EndDisabled();
    }

    static void Toggle(string key, string labelKey, string hintKey, bool fallback)
    {
        var value = ConfigManager.View.GetBool(key, fallback);
        if (ImGui.Checkbox(Localization.T(labelKey), ref value))
        {
            ConfigManager.View.SetBool(key, value);
            ConfigManager.SaveView(PanelManager.Panels);
            Pgxp.Pgxp.Load();
            if (!Pgxp.Pgxp.VertexCache) Pgxp.PgxpGpu.Free();
            if (!Pgxp.Pgxp.VertexCache) Pgxp.PgxpGpu.Free();
        }

        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Localization.T(hintKey));
    }
}