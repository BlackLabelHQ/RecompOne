using System.Numerics;

namespace RecompOne.UI;

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

        if (Ui.Dialog(Localization.T("common.ok")) != DialogChoice.None) Close();
    }
}