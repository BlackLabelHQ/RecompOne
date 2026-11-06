using System.Numerics;

namespace RecompOne.UI;

public static class InterfacePage
{
    public static void Register()
    {
        SettingsPages.Page(SettingsPages.Interface, "settings.interface", -10).WithIcon(FontSet.Palette)
            .Add("accent", DrawAccent, 0)
            .Add("background", DrawBackground, 100)
            .Add("scale", DrawScale, 200)
            .Add("fps", DrawShowFps, 300)
            .Add("big_picture", DrawBigPicture, 400, null, static () => Features.BigPictureInterface);
    }

    private static void DrawAccent()
    {
        DrawSwatches("settings.interface.accent", Theme.Accents, Theme.MatchAccent(), Theme.Accent, Theme.SetAccent);
    }

    private static void DrawBackground()
    {
        DrawSwatches("settings.interface.background", Theme.Backgrounds, Theme.MatchBackground(), Theme.Background,
            Theme.SetBackground);
    }

    private static void DrawShowFps()
    {
        SettingsWidgets.Toggle(InterfaceOptions.ShowFps, "settings.interface.show_fps");
    }

    private static void DrawBigPicture()
    {
        if (SettingsWidgets.Toggle(InterfaceOptions.BigPicture, "settings.interface.big_picture"))
            NoticePopup.Show(Localization.T("common.restart_required"));
    }

    private static void DrawSwatches(string labelKey, Theme.Preset[] presets, int selected, Vector4 current,
        Action<Vector4> apply)
    {
        Ui.Text(Localization.T(labelKey));

        for (var i = 0; i < presets.Length; i++)
        {
            if (i > 0) ImGui.SameLine();
            if (Ui.Swatch($"##swatch-{i}", presets[i].Color, selected == i, Localization.T(presets[i].LabelKey)))
                apply(presets[i].Color);
        }

        var custom = new Vector3(current.X, current.Y, current.Z);
        if (Ui.ColorEdit($"{Localization.T("settings.interface.custom")}##custom", ref custom))
            apply(new Vector4(custom, 1f));
    }

    private static void DrawScale()
    {
        var scale = InterfaceOptions.Scale.Value;
        ImGui.TextUnformatted(Localization.T("settings.interface.ui_scale"));
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputFloat("##ui-scale", ref scale, 0.05f, 0.25f, "%.2fx"))
        {
            InterfaceOptions.Scale.Value = scale;
            ImGui.GetIO().FontGlobalScale = InterfaceOptions.Scale.Value;
            Theme.Apply();
        }

        ImGui.TextDisabled(Localization.T("settings.interface.ui_scale_hint"));
    }
}
