using System.Numerics;
using System.Text;
using ImGuiNET;
using NativeFileDialogSharp;
using RecompOne.Runtime.Config;

namespace RecompOne.Runtime.Host.Window;

internal sealed class DiscPickerPopup : IPanel
{
    public string Name => "Disc Setup";
    public bool IsOpen { get; set; }

    const string PopupId = "##DiscPicker";
    const int BufSize = 1024;

    byte[] _pathBuf = new byte[BufSize];
    string _error = "";
    bool _pendingOpen;

    public void Show()
    {
        var current = ConfigManager.Game.CdPath ?? "";
        _pathBuf = new byte[BufSize];
        var bytes = Encoding.UTF8.GetBytes(current);
        Array.Copy(bytes, _pathBuf, Math.Min(bytes.Length, BufSize - 1));
        _error = "";
        _pendingOpen = true;
        IsOpen = true;
    }

    public void Draw()
    {
        if (!IsOpen) return;

        if (_pendingOpen)
        {
            ImGui.OpenPopup(PopupId);
            _pendingOpen = false;
        }

        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(vp.GetCenter(), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(520 * HostWindow.DpiScale, 0), ImGuiCond.Always);

        bool open = true;
        if (!ImGui.BeginPopupModal(PopupId, ref open,
                ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
        {
            if (!open) IsOpen = false;
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.85f, 0.3f, 1f));
        ImGui.TextUnformatted("Disc image not found");
        ImGui.PopStyleColor();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextWrapped("Please provide the path to the game's disc file.");
        ImGui.Spacing();

        float browseW = 80 * HostWindow.DpiScale;
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - browseW - spacing);
        ImGui.InputText("##discpath", _pathBuf, (uint)BufSize);
        ImGui.SameLine();
        if (ImGui.Button("Browse...", new Vector2(browseW, 0)))
            OpenBrowser();

        if (_error.Length > 0)
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.3f, 0.3f, 1f));
            ImGui.TextWrapped(_error);
            ImGui.PopStyleColor();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        float btnW = 100 * HostWindow.DpiScale;
        float total = btnW * 2 + spacing;
        ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - total) * 0.5f + ImGui.GetStyle().WindowPadding.X);

        if (ImGui.Button("Confirm", new Vector2(btnW, 0)))
            TryConfirm();
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(btnW, 0)))
            Dismiss();

        ImGui.Spacing();
        ImGui.EndPopup();
    }

    void OpenBrowser()
    {
        try
        {
            var currentPath = Encoding.UTF8.GetString(_pathBuf).TrimEnd('\0').Trim();
            string? defaultDir = null;
            if (currentPath.Length > 0 && File.Exists(currentPath))
                defaultDir = Path.GetDirectoryName(Path.GetFullPath(currentPath));
            else if (currentPath.Length > 0 && Directory.Exists(currentPath))
                defaultDir = Path.GetFullPath(currentPath);

            var result = Dialog.FileOpen("cue", defaultDir);
            if (result.IsOk && !string.IsNullOrWhiteSpace(result.Path))
            {
                SetPathBuf(result.Path);
            }
            else if (result.IsError)
            {
                _error = $"Dialog error: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            _error = $"Failed to open th native picker: {ex.Message}";
        }
    }

    void SetPathBuf(string path)
    {
        _pathBuf = new byte[BufSize];
        var bytes = Encoding.UTF8.GetBytes(path);
        Array.Copy(bytes, _pathBuf, Math.Min(bytes.Length, BufSize - 1));
        _error = "";
    }

    void TryConfirm()
    {
        var path = Encoding.UTF8.GetString(_pathBuf).TrimEnd('\0').Trim();
        if (!File.Exists(path))
        {
            _error = "File was not found please check the path and try again";
            return;
        }

        
        if (Runtime.ValidateDisc(path) is { } problem)
        {
            _error = problem;
            return;
        }

        ConfigManager.Game.CdPath = path;
        ConfigManager.SaveGame();
        Dismiss();
    }

    void Dismiss()
    {
        IsOpen = false;
        ImGui.CloseCurrentPopup();
    }
}
