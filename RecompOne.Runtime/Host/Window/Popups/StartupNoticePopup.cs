using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Config;

namespace RecompOne.Runtime.Host.Window;

public sealed class StartupNoticePopup : Popup
{
    private static string _message = "";
    private static string _title = "common.notice";
    private static string _ackKey = "StartupNoticeAck";

    protected override string TitleKey => _title;
    protected override Vector2 Size => new(500f, 0f);
    protected override bool Closable => false;

    public static void Set(string message, string title, string ackKey)
    {
        _message = message;
        _title = string.IsNullOrEmpty(title) ? "common.notice" : title;
        _ackKey = string.IsNullOrEmpty(ackKey) ? "StartupNoticeAck" : ackKey;
    }

    public static bool NeedsAck => !string.IsNullOrEmpty(_message) && !ConfigManager.View.GetBool(_ackKey);

    protected internal override void Update()
    {
        if (NeedsAck && !IsOpen) Open();
    }

    protected override void DrawContent()
    {
        UiText.CenteredWrapped(Localization.T(_message));

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button(Localization.T("common.understood"), new Vector2(-1f, 0f)))
        {
            ConfigManager.View.SetBool(_ackKey, true);
            ConfigManager.SaveView(PanelManager.Panels);
            Close();
        }
    }
}