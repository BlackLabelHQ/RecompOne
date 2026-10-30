using System.Numerics;
using ImGuiNET;
using NativeFileDialogNET;
using RecompOne.Runtime.Config;

namespace RecompOne.Runtime.Host.Window;

internal static class PathsPage
{
    private static string _discError = "";

    public static void Register()
    {
        SettingsPages.Page(SettingsPages.Paths, "settings.paths", 20)
            .Add("disc", DrawDisc, 0, "settings.paths.disc")
            .Add("memory_cards", DrawMemoryCards, 100, "settings.paths.memory_cards");
    }

    private static void DrawDisc()
    {
        var system = Settings.System;
        if (PathRow("##disc", system.DiscPath, "cue,chd", false, out var disc))
        {
            var problem = Runtime.ValidateDisc(disc);
            if (problem != null)
            {
                _discError = problem;
            }
            else
            {
                _discError = "";
                system.DiscPath = disc;
                RestartRequired();
            }
        }

        if (_discError.Length > 0)
            ImGuiUtils.TextWrappedColored(new Vector4(1f, 0.38f, 0.38f, 1f), _discError);
    }

    private static void DrawMemoryCards()
    {
        Card("settings.paths.card_a", Settings.System.CardA);
        ImGui.Spacing();
        Card("settings.paths.card_b", Settings.System.CardB);
    }

    private static void Card(string labelKey, MemoryCardSettings card)
    {
        var on = card.Enabled;
        if (ImGui.Checkbox(Localization.T(labelKey), ref on))
        {
            card.Enabled = on;
            RestartRequired();
        }

        if (PathRow($"##{labelKey}", card.Path, "sav", true, out var picked))
        {
            card.Path = picked;
            RestartRequired();
        }
    }

    private static void RestartRequired()
    {
        Settings.MarkDirty();
        NoticePopup.Show(Localization.T("common.restart_required"));
    }

    private static bool PathRow(string id, string current, string filter, bool save, out string result)
    {
        result = "";

        var browse = 90f;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - browse - spacing);

        var buffer = current ?? "";
        var changed = false;
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
            if (save) dialog.SaveFile();
            else dialog.SelectFile();
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
