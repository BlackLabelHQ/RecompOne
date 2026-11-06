using System.Numerics;

namespace RecompOne.UI;

public sealed class UpdatePopup : Popup
{
    protected override string TitleKey => "update.title";
    protected override Vector2 Size => new(560f, 460f);
    
    protected internal override void Update()
    {
        if (AutoUpdater.PromptVisible && !IsOpen) Open();
        else if (!AutoUpdater.PromptVisible && IsOpen) Close();
    }
    
    protected override void OnClosed() => AutoUpdater.Dismiss();
    
    protected override void DrawContent()
    {
        DrawHeader();
        ImGui.Spacing();
        
        float footer = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y * 2f;
        if (AutoUpdater.Phase is UpdatePhase.Downloading or UpdatePhase.Applying)
            footer += ImGui.GetFrameHeightWithSpacing();
        
        ImGui.BeginChild("##notes", new Vector2(0, -footer), ImGuiChildFlags.Borders);
        DrawNotes();
        ImGui.EndChild();
        
        ImGui.Spacing();
        DrawActions();
    }
    
    public static string StatusLine() => AutoUpdater.Phase switch
    {
        UpdatePhase.Checking => Localization.T("update.status.checking"),
        UpdatePhase.Available => Localization.T("update.status.available", AutoUpdater.LatestTag),
        UpdatePhase.Downloading => Localization.T("update.status.downloading", AutoUpdater.LatestTag),
        UpdatePhase.Applying => Localization.T("update.status.applying"),
        UpdatePhase.Failed => Localization.T("update.status.failed"),
        _ => AutoUpdater.CurrentTag == null
            ? Localization.T("update.status.dev")
            : Localization.T("update.status.running", AutoUpdater.CurrentTag),
    };
    
    private static void DrawHeader()
    {
        string headline = AutoUpdater.Phase switch
        {
            UpdatePhase.Downloading => Localization.T("update.headline.downloading", AutoUpdater.LatestTag),
            UpdatePhase.Applying => Localization.T("update.headline.installing", AutoUpdater.LatestTag),
            UpdatePhase.Failed => Localization.T("update.headline.failed"),
            _ => Localization.T("update.headline.available", AutoUpdater.LatestTag),
        };
        
        var tail = Localization.T("update.current", AutoUpdater.CurrentTag ?? "dev");
        ImGui.TextUnformatted(headline);
        ImGui.SameLine();
        float off = ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(tail).X;
        if (off > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + off);
        ImGui.TextDisabled(tail);
        ImGui.Separator();
    }
    
    private static void DrawNotes()
    {
        if (AutoUpdater.Phase == UpdatePhase.Failed)
        {
            ImGui.TextWrapped(AutoUpdater.Error);
            return;
        }
        
        if (!AutoUpdater.CanWriteInstallDir())
        {
            ImGui.TextWrapped(Localization.T("update.not_writable"));
            ImGui.Spacing();
        }
        
        if (string.IsNullOrWhiteSpace(AutoUpdater.ReleaseNotes))
        {
            ImGui.TextDisabled(Localization.T("update.no_notes"));
            return;
        }
        
        foreach (var line in AutoUpdater.ReleaseNotes.Replace("\r", "").Split('\n'))
        {
            if (line.Length == 0) { ImGui.Spacing(); continue; }
            if (line.StartsWith('#'))
            {
                ImGui.Spacing();
                ImGui.SeparatorText(line.TrimStart('#').Trim());
                continue;
            }
            ImGui.Bullet();
            ImGui.TextWrapped(line.TrimStart('-', '*', ' '));
        }
    }
    
    private static void DrawActions()
    {
        switch (AutoUpdater.Phase)
        {
            case UpdatePhase.Available when !AutoUpdater.CanWriteInstallDir():
                if (Row(0, 2, Localization.T("update.releases_page"))) AutoUpdater.OpenReleasesPage();
                if (Row(1, 2, Localization.T("update.not_now"))) AutoUpdater.Dismiss();
                break;
            
            case UpdatePhase.Available:
                if (Row(0, 3, Localization.T("update.install"))) AutoUpdater.StartUpdate();
                if (Row(1, 3, Localization.T("update.not_now"))) AutoUpdater.Dismiss();
                if (Row(2, 3, Localization.T("update.skip", AutoUpdater.LatestTag))) AutoUpdater.SkipLatest();
                break;
            
            case UpdatePhase.Downloading:
            {
                long total = AutoUpdater.TotalBytes;
                long done = AutoUpdater.DownloadedBytes;
                ImGui.ProgressBar(total > 0 ? done / (float)total : 0f, new Vector2(-1, 0),
                    total > 0
                        ? $"{done / 1048576f:0.0} / {total / 1048576f:0.0} MB"
                        : $"{done / 1048576f:0.0} MB");
                ImGui.Spacing();
                if (Row(0, 1, Localization.T("common.cancel"))) AutoUpdater.CancelDownload();
                break;
            }
            
            case UpdatePhase.Applying:
                ImGui.ProgressBar(1f, new Vector2(-1, 0), Localization.T("update.restarting"));
                ImGui.Spacing();
                ImGui.BeginDisabled();
                Row(0, 1, Localization.T("update.reopening"));
                ImGui.EndDisabled();
                break;
            
            case UpdatePhase.Failed:
                if (Row(0, 3, Localization.T("update.retry"))) AutoUpdater.StartUpdate();
                if (Row(1, 3, Localization.T("update.releases_page"))) AutoUpdater.OpenReleasesPage();
                if (Row(2, 3, Localization.T("common.close"))) AutoUpdater.AcknowledgeFailure();
                break;
        }
    }
    
    private static bool Row(int index, int count, string label)
    {
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float width = (ImGui.GetContentRegionAvail().X - spacing * (count - 1)) / count;
        if (index > 0) ImGui.SameLine();
        return ImGui.Button(label, new Vector2(width, 0));
    }
}
