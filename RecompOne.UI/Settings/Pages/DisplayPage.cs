
namespace RecompOne.UI;

public static class DisplayPage
{
    private static readonly string[] Backends = ["auto", "gl45", "gl33", "gl21"];

    public static void Register()
    {
        //some games may not need these so make it toglable by the game
        SettingsPages.Page(SettingsPages.Display, "settings.display", 5).WithIcon(FontSet.Desktop)
            .Add("window", DrawWindow, 0)
            .Add("renderer", DrawRenderer, 100)
            .Add("frame_rate", DrawFrameRate, 200, "settings.display.frame_rate",
                static () => Features.FrameInterpolation);
    }

    private static void DrawWindow()
    {
        if (SettingsWidgets.Toggle(DisplayOptions.Fullscreen, "settings.display.fullscreen"))
            HostWindow.SetFullscreen(DisplayOptions.Fullscreen);

        if (SettingsWidgets.Toggle(DisplayOptions.VSync, "settings.display.vsync", "settings.display.vsync_hint"))
            HostWindow.SetVSync(DisplayOptions.VSync);
    }

    private static void DrawRenderer()
    {
        if (SettingsWidgets.Slider(DisplayOptions.RenderScale, "settings.display.render_scale", 1, 8, "%dx",
                "settings.display.render_scale_hint"))
            NoticePopup.Show(Localization.T("common.restart_required"));

        var scale = DisplayOptions.RenderScale.Value;
        var lines = GpuHle.LastDisplayH;
        var width = GpuHle.LastDisplayW;
        if (lines > 0)
            Ui.Note(Localization.T("settings.display.render_scale_lines",
                width, lines, width * scale, lines * scale, scale));

        if (scale != GlVram.Scale)
            Ui.Note(Localization.T("settings.display.restart_pending"));

        ImGui.Spacing();

        var backends = Backends.Select(static b => (b, b)).ToArray();
        if (SettingsWidgets.Combo(DisplayOptions.Backend, "settings.display.backend", backends))
            NoticePopup.Show(Localization.T("common.restart_required"));

        Ui.Note(Localization.T("settings.display.backend_running", GpuBackendFactory.Selected));

        ImGui.Spacing();

        if (SettingsWidgets.Toggle(DisplayOptions.TrueColor, "settings.display.true_color", "settings.display.true_color_hint"))
            NoticePopup.Show(Localization.T("common.restart_required"));

        var trueColor = GlVram.TrueColor;
        using (Ui.Disabled(trueColor))
        {
            SettingsWidgets.Toggle(DisplayOptions.Dithering, "settings.display.dithering", "settings.display.dithering_hint");
        }
    }

    private static void DrawFrameRate()
    {
        var available = Interp.Available;
        using (Ui.Disabled(!available))
        {

            var steps = Interp.Steps;
            var index = Math.Max(Array.IndexOf(steps, InterpOptions.TargetFps.Value), 0);

            if (Ui.Slider("##frame-rate", ref index, 0, steps.Length - 1, StepLabel(steps[index]),
                    Localization.T("settings.display.frame_rate_hint")))
            {
                InterpOptions.TargetFps.Value = steps[Math.Clamp(index, 0, steps.Length - 1)];
                HostWindow.RefreshVSync();
            }

        }

        if (!available)
            Ui.Note(Localization.T("settings.display.frame_rate_needs_native"));
    }

    private static string StepLabel(int fps)
    {
        if (fps == Interp.Native) return Localization.T("settings.display.frame_rate_native");

        var effective = Interp.Resolve(fps, Interp.RefreshRate, Interp.VSync);
        return effective > 0 && effective != fps ? $"{fps} FPS ({effective})" : $"{fps} FPS";
    }
}
