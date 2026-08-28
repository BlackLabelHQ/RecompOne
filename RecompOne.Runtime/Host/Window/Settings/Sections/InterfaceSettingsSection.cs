using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Config;

namespace RecompOne.Runtime.Host.Window;

internal sealed class InterfaceSettingsSection : ISettingsSection
{
    public string Id => "interface";
    public string TitleKey => "settings.interface";
    public int Order => -10;

    public void Draw()
    {
        DrawLanguage();
        ImGui.Spacing();
        DrawSwatches("settings.interface.accent", "accent", Theme.Accents, Theme.MatchAccent(), Theme.Accent, Theme.SetAccent);
        ImGui.Spacing();
        DrawSwatches("settings.interface.background", "background", Theme.Backgrounds, Theme.MatchBackground(), Theme.Background, Theme.SetBackground);
        ImGui.Spacing();
        DrawScale();
        ImGui.Spacing();
        DrawShowFps();
    }

    static void DrawShowFps()
    {
        bool show = ConfigManager.View.ShowFps;
        if (ImGui.Checkbox(Localization.T("settings.interface.show_fps"), ref show))
        {
            ConfigManager.View.ShowFps = show;
            ConfigManager.SaveView(PanelManager.Panels);
        }
    }

    static void DrawLanguage()
    {
        var languages = Localization.Languages;
        var current = Localization.CurrentCode;
        string label = current;
        foreach (var language in languages)
            if (language.Code == current) label = language.Name;

        ImGui.TextUnformatted(Localization.T("settings.interface.language"));
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("##language", label))
        {
            foreach (var language in languages)
                if (ImGui.Selectable(language.Name, language.Code == current))
                {
                    Localization.SetLanguage(language.Code);
                    ConfigManager.View.Language = language.Code;
                    ConfigManager.SaveView(PanelManager.Panels);
                }
            ImGui.EndCombo();
        }
        ImGui.TextDisabled(Localization.T("settings.interface.language_hint"));
    }

    static void DrawSwatches(string labelKey, string id, Theme.Preset[] presets, int selected, Vector4 current, Action<Vector4> apply)
    {
        ImGui.TextUnformatted(Localization.T(labelKey));

        float size = ImGui.GetFrameHeight();
        for (int i = 0; i < presets.Length; i++)
        {
            if (i > 0) ImGui.SameLine();
            if (Swatch($"##{id}-{i}", presets[i].Color, selected == i, size))
            {
                apply(presets[i].Color);
                ConfigManager.SaveView(PanelManager.Panels);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Localization.T(presets[i].LabelKey));
        }

        var custom = new Vector3(current.X, current.Y, current.Z);
        if (ImGui.ColorEdit3($"{Localization.T("settings.interface.custom")}##{id}-custom", ref custom,
                ImGuiColorEditFlags.NoInputs))
        {
            apply(new Vector4(custom, 1f));
            ConfigManager.SaveView(PanelManager.Panels);
        }
    }

    static void DrawScale()
    {
        float scale = ConfigManager.View.UiScale;
        ImGui.TextUnformatted(Localization.T("settings.interface.ui_scale"));
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputFloat("##ui-scale", ref scale, 0.05f, 0.25f, "%.2fx"))
        {
            ConfigManager.View.UiScale = Math.Clamp(scale, 0.5f, 3f);
            ImGui.GetIO().FontGlobalScale = ConfigManager.View.UiScale;
            Theme.Apply();
            ConfigManager.SaveView(PanelManager.Panels);
        }
        ImGui.TextDisabled(Localization.T("settings.interface.ui_scale_hint"));
    }

    static bool Swatch(string id, Vector4 color, bool selected, float size)
    {
        var pos = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
        bool hovered = ImGui.IsItemHovered();

        var draw = ImGui.GetWindowDrawList();
        var style = ImGui.GetStyle();
        float rounding = style.FrameRounding;

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
