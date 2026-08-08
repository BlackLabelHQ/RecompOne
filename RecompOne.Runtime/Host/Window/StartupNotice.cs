using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Config;

namespace RecompOne.Runtime.Host.Window;

/// <summary>same as NoticePopup but blocking, intended to give a message on game startup (as name implyes)</summary>
internal static class StartupNotice
{
    static string? _message;
    static string _title = "Notice";
    static string _ackKey = "StartupNoticeAck";

    public static void Set(string message, string title, string ackKey)
    {
        _message = message;
        _title = string.IsNullOrEmpty(title) ? "Notice" : title;
        _ackKey = string.IsNullOrEmpty(ackKey) ? "StartupNoticeAck" : ackKey;
    }

    public static bool NeedsAck => !string.IsNullOrEmpty(_message) && !ConfigManager.View.GetBool(_ackKey);

    public static void Draw()
    {
        if (string.IsNullOrEmpty(_message)) return;

        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(vp.GetCenter(), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(460 * HostWindow.DpiScale, 0), ImGuiCond.Always);

        if (ImGui.Begin(_title, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking |
                ImGuiWindowFlags.NoSavedSettings))
        {
            UiText.CenteredWrapped(_message);
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            if (ImGui.Button("I understand", new Vector2(-1, 0)))
            {
                ConfigManager.View.SetBool(_ackKey, true);
                ConfigManager.SaveView(PanelManager.Panels);
            }
        }
        ImGui.End();
    }
}
