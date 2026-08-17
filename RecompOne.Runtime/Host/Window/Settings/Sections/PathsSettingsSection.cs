using System.Numerics;
using ImGuiNET;
using NativeFileDialogNET;
using RecompOne.Runtime.Config;

namespace RecompOne.Runtime.Host.Window;

internal sealed class PathsSettingsSection : ISettingsSection
{
    public string Id => "paths";
    public string TitleKey => "settings.paths";
    public int Order => 20;

    string _discError = "";

    public void Draw()
    {
        var game = ConfigManager.Game;

        ImGui.SeparatorText(Localization.T("settings.paths.disc"));

        if (PathRow("##disc", game.CdPath, "cue,chd", false, out string disc))
        {
            var problem = Runtime.ValidateDisc(disc);
            if (problem != null)
            {
                _discError = problem;
            }
            else
            {
                _discError = "";
                game.CdPath = disc;
                ConfigManager.SaveGame();
                NoticePopup.Show(Localization.T("common.restart_required"));
            }
        }

        if (_discError.Length > 0)
            ImGuiEx.TextWrappedColored(new Vector4(1f, 0.38f, 0.38f, 1f), _discError);

        ImGui.Spacing();
        ImGui.SeparatorText(Localization.T("settings.paths.memory_cards"));

        Card("settings.paths.card_a", game.CardAEnabled, v => game.CardAEnabled = v, game.CardAPath, p => game.CardAPath = p);
        ImGui.Spacing();
        Card("settings.paths.card_b", game.CardBEnabled, v => game.CardBEnabled = v, game.CardBPath, p => game.CardBPath = p);
    }

    static void Card(string labelKey, bool enabled, Action<bool> setEnabled, string path, Action<string> setPath)
    {
        bool on = enabled;
        if (ImGui.Checkbox(Localization.T(labelKey), ref on))
        {
            setEnabled(on);
            ConfigManager.SaveGame();
            NoticePopup.Show(Localization.T("common.restart_required"));
        }

        if (PathRow($"##{labelKey}", path, "sav", true, out string picked))
        {
            setPath(picked);
            ConfigManager.SaveGame();
            NoticePopup.Show(Localization.T("common.restart_required"));
        }
    }

    static bool PathRow(string id, string current, string filter, bool save, out string result)
    {
        result = "";

        float browse = 90f;
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - browse - spacing);

        string buffer = current ?? "";
        bool changed = false;
        if (ImGui.InputText(id, ref buffer, 1024, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            result = buffer.Trim();
            changed = true;
        }

        ImGui.SameLine();
        if (ImGui.Button($"{Localization.T("common.browse")}{id}", new Vector2(browse, 0f)))
        {
            string? directory = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(current) && File.Exists(current))
                    directory = Path.GetDirectoryName(Path.GetFullPath(current));
            }
            catch
            {
            }

            using var dialog = new NativeFileDialog();
            if (save) dialog.SaveFile(); else dialog.SelectFile();
            if (!string.IsNullOrWhiteSpace(filter)) dialog.AddFilter("Files", filter);

            if (dialog.Open(out string? picked, directory) == DialogResult.Okay && !string.IsNullOrWhiteSpace(picked))
            {
                result = picked;
                changed = true;
            }
        }

        return changed;
    }
}
