using System.Numerics;

namespace RecompOne.UI;

public sealed class CrashPopup : Popup
{
    private static string _logName = "";
    private static string _errorCode = "";
    private static bool _pending;

    protected override string TitleKey => "crash.title";
    protected override Vector2 Size => new(560f, 0f);
    protected override bool Closable => false;

    public static bool IsPending => _pending;

    public static string Report(Exception error)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        _logName = $"{timestamp}.log";
        _errorCode = $"0x{error.HResult:X8}";

        try
        {
            File.WriteAllText(_logName, error.ToString());
        }
        catch (Exception logError)
        {
            Console.Error.WriteLine($"[Crash(not the bandicoot!)] failed to write {_logName}: {logError.Message}");
            _logName = "log file unavailable";
        }

        Console.Error.WriteLine($"[Crash(not the bandicoot!)] {_errorCode}; log: {_logName}");
        _pending = true;
        return _errorCode;
    }

    protected internal override void Update()
    {
        if (!_pending || IsOpen) return;
        _pending = false;
        Open();
    }

    protected override void DrawContent()
    {
        ImGui.TextWrapped(Localization.T("crash.body", _logName));
        ImGui.Spacing();
        ImGui.TextWrapped(Localization.T("crash.error", _errorCode));
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        const float width = 140f;
        ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - width) * 0.5f + ImGui.GetCursorPosX());
        if (ImGui.Button(Localization.T("common.ok"), new Vector2(width, 0f))) Close();
    }
}
