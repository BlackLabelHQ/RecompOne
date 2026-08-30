using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Dispatch;

namespace RecompOne.Runtime.Host.Window;

//ive never been so proud of a stupid gui :3
internal sealed class OverlayEventsPanel : IPanel
{
    public string Name => "Overlay Events";
    public string TitleKey => "panel.overlay_events";
    public bool IsOpen { get; set; }

    private readonly List<OverlayEvent> _snapshot = [];
    private int _lastCount;
    private bool _autoScroll = true;
    private bool _scrollPending;

    private static readonly Vector4 ColLoaded = new(0.30f, 1.00f, 0.40f, 1f);
    private static readonly Vector4 ColUnloaded = new(0.55f, 0.55f, 0.55f, 1f);
    private static readonly Vector4 ColOverwritten = new(1.00f, 0.70f, 0.15f, 1f);
    private static readonly Vector4 ColVramCollision = new(1.00f, 0.35f, 0.35f, 1f);

    public void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2(600, 340), ImGuiCond.FirstUseEver);
        var open = IsOpen;
        if (!ImGui.Begin(this.Title(), ref open, ImGuiWindowFlags.MenuBar))
        {
            IsOpen = open;
            ImGui.End();
            return;
        }

        DrawMenuBar();
        DrawActiveOverlays();
        ImGui.Separator();
        DrawEventTable();

        IsOpen = open;
        ImGui.End();
    }

    private void DrawMenuBar()
    {
        if (!ImGui.BeginMenuBar()) return;

        if (ImGui.MenuItem("Clear")) Runtime.OverlayLog.Clear();
        ImGui.MenuItem("Auto-scroll", null, ref _autoScroll);

        ImGui.EndMenuBar();
    }

    private void DrawActiveOverlays()
    {
        ImGui.TextDisabled("Active: ");
        ImGui.SameLine();

        var any = false;
        foreach (var name in Dispatcher.ActiveNames)
        {
            if (any)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("·");
                ImGui.SameLine();
            }

            ImGuiEx.TextColored(ColLoaded, name);
            any = true;
        }

        if (!any) ImGui.TextDisabled("none");
    }

    private void DrawEventTable()
    {
        RefreshSnapshot();

        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter |
                         ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingFixedFit |
                         ImGuiTableFlags.ScrollY;

        if (!ImGui.BeginTable("##events", 4, tableFlags)) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 76);
        ImGui.TableSetupColumn("Event", ImGuiTableColumnFlags.WidthFixed, 104);
        ImGui.TableSetupColumn("Overlay", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Notes", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        for (var i = 0; i < _snapshot.Count; i++)
        {
            var ev = _snapshot[i];
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGuiEx.TextDisabled(FormatTime(ev.TimestampMs));

            ImGui.TableSetColumnIndex(1);
            var (label, color) = ev.Kind switch
            {
                OverlayEventKind.Loaded => ("loaded", ColLoaded),
                OverlayEventKind.Unloaded => ("unloaded", ColUnloaded),
                OverlayEventKind.Overwritten => ("overwritten", ColOverwritten),
                OverlayEventKind.VramCollision => ("vram collision", ColVramCollision),
                _ => ("?", ColUnloaded)
            };
            ImGuiEx.TextColored(color, label);

            ImGui.TableSetColumnIndex(2);
            ImGui.TextUnformatted(ev.OverlayName);

            ImGui.TableSetColumnIndex(3);
            if (ev.DisplacedBy != null)
                ImGuiEx.TextDisabled(ev.Kind == OverlayEventKind.VramCollision
                    ? $"with {ev.DisplacedBy}"
                    : $"by {ev.DisplacedBy}");
        }

        if (_scrollPending || (_autoScroll && _snapshot.Count > 0))
        {
            ImGui.SetScrollHereY(1f);
            _scrollPending = false;
        }

        ImGui.EndTable();
    }

    private void RefreshSnapshot()
    {
        var current = Runtime.OverlayLog.Count;
        if (current == _lastCount) return;
        _snapshot.Clear();
        Runtime.OverlayLog.Read(_snapshot);
        _lastCount = current;
        if (_autoScroll) _scrollPending = true;
    }

    private static string FormatTime(long ms)
    {
        var s = ms / 1000;
        var m = s / 60;
        var h = m / 60;
        return h > 0 ? $"{h}:{m % 60:D2}:{s % 60:D2}.{ms % 1000 / 10:D2}" : $"{m:D2}:{s % 60:D2}.{ms % 1000 / 10:D2}";
    }
}