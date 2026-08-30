using System.Numerics;
using ImGuiNET;

namespace RecompOne.Runtime.Host.Window;

public sealed class NoticePopup : Popup
{
    private static readonly Queue<string> _pending = new();

    private string _message = "";

    protected override string TitleKey => "common.notice";
    protected override Vector2 Size => new(440f, 0f);

    public static void Show(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        lock (_pending)
        {
            _pending.Enqueue(message);
        }
    }

    protected internal override void Update()
    {
        if (IsOpen) return;

        lock (_pending)
        {
            if (_pending.Count == 0) return;
            _message = _pending.Dequeue();
        }

        Open();
    }

    protected override void DrawContent()
    {
        UiText.CenteredWrapped(_message);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var width = 140f;
        ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - width) * 0.5f + ImGui.GetCursorPosX());
        if (ImGui.Button(Localization.T("common.ok"), new Vector2(width, 0f))) Close();
    }
}