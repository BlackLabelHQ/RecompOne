using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Cdrom;

namespace RecompOne.Runtime.Host.Window;

internal sealed class CdDebugPanel : IPanel
{
    public string Name => "CD Debug";
    public bool IsOpen { get; set; }

    readonly List<string> _events = new();
    bool _autoScroll = true;

    public void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2(640, 420) * HostWindow.DpiScale, ImGuiCond.FirstUseEver);
        bool open = IsOpen;
        if (!ImGui.Begin(Name, ref open)) { IsOpen = open; ImGui.End(); return; }

        var cd = Runtime.Cd;

        cd.CaptureDebug(out var d, _events);

        DrawStatus(d);
        ImGui.Separator();
        DrawEvents();

        IsOpen = open;
        ImGui.End();
    }

    static void DrawStatus(CdController.CdDebug d)
    {
        Pair("Seek LBA", $"{d.SeekLba} ({Msf(d.SeekLba)})");
        ImGui.SameLine(); ImGui.Spacing(); ImGui.SameLine();
        Pair("Last read", $"{d.LastReadLba} ({Msf(d.LastReadLba)})");
        ImGui.SameLine(); ImGui.Spacing(); ImGui.SameLine();
        Pair("Sectors read", d.SectorsRead.ToString());

        Pair("Reading", d.Reading ? "yes" : "no");
        ImGui.SameLine(); ImGui.Spacing(); ImGui.SameLine();
        Pair("Stream pending", d.StreamPending ? "yes" : "no");
        ImGui.SameLine(); ImGui.Spacing(); ImGui.SameLine();
        Pair("Data ready", d.DataReady ? "yes" : "no");
        ImGui.SameLine(); ImGui.Spacing(); ImGui.SameLine();
        Pair("Data FIFO", $"{d.DataFifoPos}/{d.DataBufLength}");

        Pair("IRQ flags", $"{d.IrqFlags:X2}");
        ImGui.SameLine(); ImGui.Spacing(); ImGui.SameLine();
        Pair("Last IRQ", d.LastIrq.ToString());
        ImGui.SameLine(); ImGui.Spacing(); ImGui.SameLine();
        Pair("Pending IRQs", d.PendingIrqCount.ToString());
        ImGui.SameLine(); ImGui.Spacing(); ImGui.SameLine();
        Pair("Param/Resp", $"{d.ParamCount}/{d.ResponseCount}");
        ImGui.SameLine(); ImGui.Spacing(); ImGui.SameLine();
        Pair("Index", d.Index.ToString());
    }

    void DrawEvents()
    {
        ImGui.TextDisabled("Events");
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear"))
        {
            Runtime.Cd?.ClearDebugEvents();
            _events.Clear();
        }
        ImGui.SameLine();
        ImGui.Checkbox("Auto-scroll", ref _autoScroll);

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 1));

        if (!ImGui.BeginChild("##cdevents", Vector2.Zero, ImGuiChildFlags.None))
        {
            ImGui.PopStyleVar();
            ImGui.EndChild();
            return;
        }

        float rowH = ImGui.GetTextLineHeightWithSpacing();
        bool atBottom = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - rowH;

        foreach (var e in _events)
            ImGui.TextUnformatted(e);

        if (_autoScroll && atBottom)
            ImGui.SetScrollY(_events.Count * rowH);

        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    static string Msf(int lba)
    {
        int abs = lba + 150;
        if (abs < 0) abs = 0;
        int m = abs / (60 * 75);
        int s = abs / 75 % 60;
        int f = abs % 75;
        return $"{m:D2}:{s:D2}:{f:D2}";
    }

    static void Pair(string label, string value)
    {
        ImGui.TextDisabled(label);
        ImGui.SameLine();
        ImGui.TextUnformatted(value);
    }
}
