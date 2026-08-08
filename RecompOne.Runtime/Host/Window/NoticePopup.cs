using System.Numerics;
using ImGuiNET;

namespace RecompOne.Runtime.Host.Window;

/// <summary>generic notice modal</summary>
internal static class NoticePopup
{
    const string PopupId = "##notice";
    static readonly Queue<string> _pending = new();
    static string _message = "";
    static bool _open;

    public static void Show(string message)
    {
        if (!string.IsNullOrEmpty(message)) _pending.Enqueue(message);
    }

    public static void Draw()
    {
        if (!_open && _pending.Count > 0)
        {
            _message = _pending.Dequeue();
            _open = true;
            ImGui.OpenPopup(PopupId);
        }
        if (!_open) return;

        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(vp.GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(420 * HostWindow.DpiScale, 0), ImGuiCond.Appearing);

        bool open = _open;
        if (ImGui.BeginPopupModal(PopupId, ref open,
                ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoTitleBar))
        {
            UiText.CenteredWrapped(_message); //looks more pretty
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            if (ImGui.Button("OK", new Vector2(120 * HostWindow.DpiScale, 0)))
            {
                ImGui.CloseCurrentPopup();
                _open = false;
            }
            ImGui.EndPopup();
        }
        else
        {
            _open = false;
        }
    }
}
