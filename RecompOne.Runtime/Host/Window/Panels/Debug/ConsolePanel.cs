using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Diagnostics;

namespace RecompOne.Runtime.Host.Window;

internal sealed class ConsolePanel : IPanel
{
    public string Name => "Console";
    public bool IsOpen { get; set; }

    readonly List<string> _lines = new();
    readonly List<string> _visible = new();
    int _version = -1;
    string _filter = "";
    string _lastFilter = "";
    bool _autoScroll = true;

    public void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2(720, 320) * HostWindow.DpiScale, ImGuiCond.FirstUseEver);
        bool open = IsOpen;
        if (!ImGui.Begin(Name, ref open, ImGuiWindowFlags.MenuBar)) { IsOpen = open; ImGui.End(); return; }

        DrawMenuBar();
        RefreshLines();
        DrawLines();

        IsOpen = open;
        ImGui.End();
    }

    void DrawMenuBar()
    {
        if (!ImGui.BeginMenuBar()) return;

        if (ImGui.BeginMenu("Categories"))
        {
            ImGui.MenuItem("BIOS", null, ref Log.BiosOn);
            ImGui.MenuItem("SPU", null, ref Log.SpuOn);
            ImGui.MenuItem("GPU", null, ref Log.GpuOn);
            ImGui.MenuItem("DMA", null, ref Log.DmaOn);
            ImGui.MenuItem("CD", null, ref Log.CdOn);
            ImGui.MenuItem("SDK", null, ref Log.SdkOn);
            ImGui.MenuItem("MDEC", null, ref Log.MdecOn);
            ImGui.EndMenu();
        }

        if (ImGui.MenuItem("Clear")) ConsoleMirror.Clear();

        ImGui.Checkbox("Auto-scroll", ref _autoScroll);

        ImGui.SetNextItemWidth(180 * HostWindow.DpiScale);
        ImGui.InputTextWithHint("##filter", "filter", ref _filter, 128);

        ImGui.EndMenuBar();
    }

    void RefreshLines()
    {
        bool changed = false;
        if (ConsoleMirror.Version != _version)
        {
            _version = ConsoleMirror.SnapshotInto(_lines);
            changed = true;
        }

        if (changed || _filter != _lastFilter)
        {
            _lastFilter = _filter;
            _visible.Clear();
            if (_filter.Length == 0)
            {
                _visible.AddRange(_lines);
            }
            else
            {
                foreach (var l in _lines)
                    if (l.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                        _visible.Add(l);
            }
        }
    }

    void DrawLines()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 1));

        if (!ImGui.BeginChild("##consolescroll", Vector2.Zero, ImGuiChildFlags.None))
        {
            ImGui.PopStyleVar();
            ImGui.EndChild();
            return;
        }

        float rowH = ImGui.GetTextLineHeightWithSpacing();
        int total = _visible.Count;

        float scrollY = ImGui.GetScrollY();
        float maxY = ImGui.GetScrollMaxY();
        bool atBottom = scrollY >= maxY - rowH;

        int firstRow = Math.Max(0, (int)(scrollY / rowH) - 1);
        int visRows = (int)(ImGui.GetWindowHeight() / rowH) + 2;
        int lastRow = Math.Min(total, firstRow + visRows);

        if (firstRow > 0)
            ImGui.Dummy(new Vector2(1f, firstRow * rowH));

        for (int i = firstRow; i < lastRow; i++)
            ImGui.TextUnformatted(_visible[i]);

        float remaining = (total - lastRow) * rowH;
        if (remaining > 0f)
            ImGui.Dummy(new Vector2(1f, remaining));

        if (_autoScroll && atBottom)
            ImGui.SetScrollY(total * rowH);

        ImGui.EndChild();
        ImGui.PopStyleVar();
    }
}
