using System.Numerics;

namespace RecompOne.UI;

public sealed class ConsolePanel : IPanel
{
    public string Name => "Console";
    public string TitleKey => "panel.console";
    public bool IsOpen { get; set; }

    private readonly List<string> _lines = new();
    private readonly List<string> _visible = new();
    private int _version = -1;
    private string _filter = "";
    private string _lastFilter = "";
    private bool _autoScroll = true;

    public void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2(720, 320), ImGuiCond.FirstUseEver);
        var open = IsOpen;
        if (!ImGui.Begin(this.Title(), ref open, ImGuiWindowFlags.MenuBar))
        {
            IsOpen = open;
            ImGui.End();
            return;
        }

        DrawMenuBar();
        RefreshLines();
        DrawLines();

        IsOpen = open;
        ImGui.End();
    }

    private void DrawMenuBar()
    {
        if (!ImGui.BeginMenuBar()) return;

        if (ImGui.BeginMenu("Categories"))
        {
            if (ImGui.MenuItem("BIOS", null, Log.BiosOn)) Log.BiosOn = !Log.BiosOn;
            if (ImGui.MenuItem("SPU", null, Log.SpuOn)) Log.SpuOn = !Log.SpuOn;
            if (ImGui.MenuItem("GPU", null, Log.GpuOn)) Log.GpuOn = !Log.GpuOn;
            if (ImGui.MenuItem("DMA", null, Log.DmaOn)) Log.DmaOn = !Log.DmaOn;
            if (ImGui.MenuItem("CD", null, Log.CdOn)) Log.CdOn = !Log.CdOn;
            if (ImGui.MenuItem("SDK", null, Log.SdkOn)) Log.SdkOn = !Log.SdkOn;
            if (ImGui.MenuItem("MDEC", null, Log.MdecOn)) Log.MdecOn = !Log.MdecOn;
            if (ImGui.MenuItem("IRQ", null, Log.IrqOn)) Log.IrqOn = !Log.IrqOn;
            ImGui.EndMenu();
        }

        if (ImGui.MenuItem("Clear")) ConsoleMirror.Clear();

        ImGui.Checkbox("Auto-scroll", ref _autoScroll);

        ImGui.SetNextItemWidth(180);
        ImGui.InputTextWithHint("##filter", "filter", ref _filter, 128);

        ImGui.EndMenuBar();
    }

    private void RefreshLines()
    {
        var changed = false;
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
                _visible.AddRange(_lines);
            else
                foreach (var l in _lines)
                    if (l.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                        _visible.Add(l);
        }
    }

    private void DrawLines()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 1));

        if (!ImGui.BeginChild("##consolescroll", Vector2.Zero, ImGuiChildFlags.None))
        {
            ImGui.PopStyleVar();
            ImGui.EndChild();
            return;
        }

        var rowH = ImGui.GetTextLineHeightWithSpacing();
        var total = _visible.Count;

        var scrollY = ImGui.GetScrollY();
        var maxY = ImGui.GetScrollMaxY();
        var atBottom = scrollY >= maxY - rowH;

        var firstRow = Math.Max(0, (int)(scrollY / rowH) - 1);
        var visRows = (int)(ImGui.GetWindowHeight() / rowH) + 2;
        var lastRow = Math.Min(total, firstRow + visRows);

        if (firstRow > 0)
            ImGui.Dummy(new Vector2(1f, firstRow * rowH));

        for (var i = firstRow; i < lastRow; i++)
            ImGui.TextUnformatted(_visible[i]);

        var remaining = (total - lastRow) * rowH;
        if (remaining > 0f)
            ImGui.Dummy(new Vector2(1f, remaining));

        if (_autoScroll && atBottom)
            ImGui.SetScrollY(total * rowH);

        ImGui.EndChild();
        ImGui.PopStyleVar();
    }
}