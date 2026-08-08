using System.Numerics;
using ImGuiNET;
using NativeFileDialogSharp;
using RecompOne.Runtime.Config;

namespace RecompOne.Runtime.Host.Window;

internal sealed class PathsSettingsSection : ISettingsSection
{
    public string Id => "paths";
    public string Title => "Paths";
    public int Order => 20;

    const string RestartNotice = "You need to restart the application to apply this configuration";

    string _discError = "";

    public void Draw()
    {
        var game = ConfigManager.Game;

        ImGui.SeparatorText("Disc");

        if (PathRow("##disc", game.CdPath, "cue", false, out string disc))
        {
            var problem = Runtime.ValidateDisc(disc);
            if (problem != null) _discError = problem;
            else
            {
                _discError = "";
                game.CdPath = disc;
                ConfigManager.SaveGame();
                NoticePopup.Show(RestartNotice);
            }
        }

        if (_discError.Length > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.3f, 0.3f, 1f));
            ImGui.TextWrapped(_discError);
            ImGui.PopStyleColor();
        }

        ImGui.Spacing();
        ImGui.SeparatorText("Memory cards");

        Card("Card A", game.CardAEnabled, v => game.CardAEnabled = v, game.CardAPath, p => game.CardAPath = p);
        ImGui.Spacing();
        Card("Card B", game.CardBEnabled, v => game.CardBEnabled = v, game.CardBPath, p => game.CardBPath = p);
    }

    void Card(string label, bool enabled, Action<bool> setEnabled, string path, Action<string> setPath)
    {
        bool on = enabled;
        if (ImGui.Checkbox(label, ref on))
        {
            setEnabled(on);
            ConfigManager.SaveGame();
            NoticePopup.Show(RestartNotice);
        }

        if (PathRow($"##{label}", path, "sav", true, out string picked))
        {
            setPath(picked);
            ConfigManager.SaveGame();
            NoticePopup.Show(RestartNotice);
        }
    }

    static bool PathRow(string id, string current, string filter, bool save, out string result)
    {
        result = "";

        float browse = 80 * HostWindow.DpiScale;
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - browse - spacing);

        string buf = current ?? "";
        bool changed = false;
        if (ImGui.InputText(id, ref buf, 1024, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            result = buf.Trim();
            changed = true;
        }

        ImGui.SameLine();
        if (ImGui.Button($"Browse...{id}", new Vector2(browse, 0)))
        {
            string? dir = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(current) && File.Exists(current))
                    dir = Path.GetDirectoryName(Path.GetFullPath(current));
            }
            catch
            {
            }

            var dialog = save ? Dialog.FileSave(filter, dir) : Dialog.FileOpen(filter, dir);
            if (dialog.IsOk && !string.IsNullOrWhiteSpace(dialog.Path))
            {
                result = dialog.Path;
                changed = true;
            }
        }

        return changed;
    }
}
