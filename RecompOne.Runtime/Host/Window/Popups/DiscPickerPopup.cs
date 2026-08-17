using System.Numerics;
using ImGuiNET;
using NativeFileDialogNET;
using RecompOne.Runtime.Config;

namespace RecompOne.Runtime.Host.Window;

public sealed class DiscPickerPopup : Popup
{
    protected override string TitleKey => "disc.title";
    protected override Vector2 Size => new(540f, 0f);

    string _path = "";
    string _error = "";

    protected override void OnOpened()
    {
        _path = ConfigManager.Game.CdPath ?? "";
        _error = "";
    }

    protected override void DrawContent()
    {
        ImGui.TextWrapped(Localization.T("disc.description"));
        ImGui.Spacing();

        float browse = 90f;
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - browse - spacing);
        ImGui.InputText("##disc-path", ref _path, 1024);
        ImGui.SameLine();
        if (ImGui.Button(Localization.T("common.browse"), new Vector2(browse, 0f))) Browse();

        if (_error.Length > 0)
        {
            ImGui.Spacing();
            ImGuiEx.TextWrappedColored(new Vector4(1f, 0.38f, 0.38f, 1f), _error);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        float button = 120f;
        float total = button * 2f + spacing;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (ImGui.GetContentRegionAvail().X - total) * 0.5f);

        if (ImGui.Button(Localization.T("common.confirm"), new Vector2(button, 0f))) Confirm();
        ImGui.SameLine();
        if (ImGui.Button(Localization.T("common.cancel"), new Vector2(button, 0f))) Close();
    }

    void Browse()
    {
        try
        {
            string? directory = null;
            if (_path.Length > 0 && File.Exists(_path)) directory = Path.GetDirectoryName(Path.GetFullPath(_path));
            else if (_path.Length > 0 && Directory.Exists(_path)) directory = Path.GetFullPath(_path);

            using var dialog = new NativeFileDialog().SelectFile().AddFilter(Localization.T("disc.filter"), "cue,chd");
            if (dialog.Open(out string? picked, directory) == DialogResult.Okay && !string.IsNullOrWhiteSpace(picked))
            {
                _path = picked;
                _error = "";
            }
        }
        catch (Exception e)
        {
            _error = Localization.T("disc.error.dialog", e.Message);
        }
    }

    void Confirm()
    {
        var path = _path.Trim();
        if (!File.Exists(path))
        {
            _error = Localization.T("disc.error.not_found");
            return;
        }

        if (Runtime.ValidateDisc(path) is { } problem)
        {
            _error = problem;
            return;
        }

        ConfigManager.Game.CdPath = path;
        ConfigManager.SaveGame();
        Close();
    }
}
