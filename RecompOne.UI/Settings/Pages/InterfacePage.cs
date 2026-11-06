using System.Numerics;

namespace RecompOne.UI;

public static class InterfacePage
{
    public static void Register()
    {
        SettingsPages.Page(SettingsPages.Interface, "settings.interface", -10)
            .Add("accent", DrawAccent, 0)
            .Add("background", DrawBackground, 100)
            .Add("scale", DrawScale, 200)
            .Add("fps", DrawShowFps, 300);
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

    private static void DrawSwatches(string labelKey, Theme.Preset[] presets, int selected, Vector4 current,
        Action<Vector4> apply)
    {
        ImGui.TextUnformatted(Localization.T(labelKey));

        var size = ImGui.GetFrameHeight();
        for (var i = 0; i < presets.Length; i++)
        {
            if (i > 0) ImGui.SameLine();
            if (Swatch($"##swatch-{i}", presets[i].Color, selected == i, size)) apply(presets[i].Color);

            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Localization.T(presets[i].LabelKey));
        }

        var custom = new Vector3(current.X, current.Y, current.Z);
        if (ImGui.ColorEdit3($"{Localization.T("settings.interface.custom")}##custom", ref custom,
                ImGuiColorEditFlags.NoInputs))
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

    private static bool Swatch(string id, Vector4 color, bool selected, float size)
    {
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
        var hovered = ImGui.IsItemHovered();

        var draw = ImGui.GetWindowDrawList();
        var style = ImGui.GetStyle();
        var rounding = style.FrameRounding;

        draw.AddRectFilled(pos, pos + new Vector2(size, size), ImGui.ColorConvertFloat4ToU32(color), rounding);
        draw.AddRect(pos, pos + new Vector2(size, size),
            ImGui.ColorConvertFloat4ToU32(style.Colors[(int)ImGuiCol.Border]), rounding);

        if (selected || hovered)
            draw.AddRect(pos - Vector2.One, pos + new Vector2(size + 1f, size + 1f),
                ImGui.ColorConvertFloat4ToU32(style.Colors[(int)ImGuiCol.Text] with { W = selected ? 0.95f : 0.4f }),
                rounding, ImDrawFlags.None, 2f);

        return clicked;
    }
}
