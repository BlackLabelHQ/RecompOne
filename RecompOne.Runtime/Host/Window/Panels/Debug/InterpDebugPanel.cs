using System.Numerics;
using ImGuiNET;
using ImPlotNET;
using RecompOne.Runtime.Interp;

namespace RecompOne.Runtime.Host.Window;

internal sealed class InterpDebugPanel : IPanel
{
    private const float PlotHeight = 110f;
    private const ImPlotFlags PlotFlags = ImPlotFlags.NoMenus | ImPlotFlags.NoBoxSelect | ImPlotFlags.NoMouseText;
    private const ImPlotAxisFlags TimeAxis = ImPlotAxisFlags.AutoFit | ImPlotAxisFlags.NoLabel | ImPlotAxisFlags.NoTickLabels;
    private const ImPlotAxisFlags ValueAxis = ImPlotAxisFlags.NoLabel;

    public string Name => "Interpolation";
    public string TitleKey => "panel.interp_debug";
    public bool IsOpen { get; set; }

    private bool _paused;
    private readonly Dictionary<InterpRing, InterpRing> _frozen = new();

    public void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2(760, 640), ImGuiCond.FirstUseEver);
        var open = IsOpen;
        if (!ImGui.Begin(this.Title(), ref open))
        {
            IsOpen = open;
            ImGui.End();
            return;
        }

        var backend = Interp.Interp.Backend;
        if (backend == null)
        {
            ImGui.TextDisabled("Interpolation backend not created");
            IsOpen = open;
            ImGui.End();
            return;
        }

        var stats = backend.Stats;
        DrawToolbar(backend, stats);
        DrawSummary(backend, stats);
        DrawPlots(backend, stats);

        IsOpen = open;
        ImGui.End();
    }

    private void DrawToolbar(InterpBackend backend, InterpStats stats)
    {
        if (ImGui.SmallButton("Reset"))
        {
            stats.Reset();
            _frozen.Clear();
        }

        ImGui.SameLine();
        if (ImGui.Checkbox("Pause", ref _paused) && !_paused) _frozen.Clear();

        ImGui.SameLine();
        var marker = backend.Marker;
        if (ImGui.Checkbox("Weight marker", ref marker)) backend.Marker = marker;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Draws a bar in the top-left of the game image whose length is the weight of that display frame.\nRed while interpolating, green on a real game frame.");

        ImGui.SameLine();
        var state = stats.LastSkip == InterpSkip.None ? "interpolating" : stats.LastSkip.ToString();
        ImGui.TextDisabled($"| {state}");
    }

    private static void DrawSummary(InterpBackend backend, InterpStats stats)
    {
        if (!ImGui.BeginTable("##interp-summary", 6, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV)) return;

        Cell("Target", $"{Interp.Interp.EffectiveTarget}/{Interp.Interp.RefreshRate}hz");
        Cell("Game", $"{backend.SourceRate} fps");
        Cell("Delay", $"{backend.Delay:0.00} vb");
        Cell("Pace", backend.PaceMs <= 0.0 ? "-" : Runtime.SwapBlocks ? "vsync" : $"{backend.PaceMs:0.0}ms");
        Cell("VSync", Interp.Interp.VSync ? "on" : "off");
        Cell("Tris", stats.LastTris.ToString());

        Cell("Published", stats.Published.ToString());
        Cell("Empty", stats.PublishedEmpty.ToString());
        Cell("Swap cuts", stats.SwapPublished.ToString());
        Cell("Unseen", stats.Skipped.ToString());
        Cell("Holds", stats.Endpoints.ToString());
        Cell("Settle/RB", $"{stats.Settles}/{stats.Readbacks}");

        Cell("Live", $"{stats.LiveX},{stats.LiveY}");
        Cell("Drawn", $"{stats.ShownX},{stats.ShownY}");
        Cell("Remaps", stats.Remaps.ToString());
        Cell("Unmapped", stats.Unmapped.ToString());
        Cell("Interp/s", stats.InterpolatedRate.Last.ToString("0"));
        Cell("Shown/s", stats.PresentRate.Last.ToString("0"));

        ImGui.EndTable();
    }

    private static void Cell(string label, string value)
    {
        ImGui.TableNextColumn();
        ImGui.TextDisabled(label);
        ImGui.SameLine();
        ImGui.TextUnformatted(value);
    }

    private void DrawPlots(InterpBackend backend, InterpStats stats)
    {
        if (!ImGui.BeginTable("##interp-plots", 2, ImGuiTableFlags.SizingStretchSame)) return;

        var size = new Vector2(-1, PlotHeight * Theme.Scale);

        ImGui.TableNextColumn();
        if (Begin("Weight per display frame", size))
        {
            ImPlot.SetupAxisLimits(ImAxis.Y1, -0.05, 1.05, ImPlotCond.Always);
            Line("weight", View(stats.FrameWeight), "0.00");
            ImPlot.EndPlot();
        }

        ImGui.TableNextColumn();
        if (Begin("Display frame time (ms)", size))
        {
            ImPlot.SetupAxis(ImAxis.Y1, "", ValueAxis | ImPlotAxisFlags.AutoFit);
            Line("frame", View(stats.FrameMs), "0.0");
            if (backend.PaceMs > 0.0)
            {
                var target = (float)backend.PaceMs;
                ImPlot.SetNextLineStyle(new Vector4(1f, 0.6f, 0.2f, 0.8f));
                ImPlot.PlotInfLines($"target {target:0.0}", ref target, 1, ImPlotInfLinesFlags.Horizontal);
            }
            ImPlot.EndPlot();
        }

        ImGui.TableNextColumn();
        if (Begin("Game cadence (vblanks)", size))
        {
            ImPlot.SetupAxisLimits(ImAxis.Y1, 0, 6.5, ImPlotCond.Always);
            Stairs("interval", View(stats.PairVblanks));
            Line("delay", View(stats.PairDelay), "0.00");
            ImPlot.EndPlot();
        }

        ImGui.TableNextColumn();
        if (Begin("Rates (per second)", size))
        {
            ImPlot.SetupAxis(ImAxis.Y1, "", ValueAxis | ImPlotAxisFlags.AutoFit);
            Line("game", View(stats.PublishedRate), "0");
            Line("interp", View(stats.InterpolatedRate), "0");
            Line("shown", View(stats.PresentRate), "0");
            ImPlot.EndPlot();
        }

        ImGui.TableNextColumn();
        if (Begin("Transform matching", size))
        {
            ImPlot.SetupAxis(ImAxis.Y1, "", ValueAxis | ImPlotAxisFlags.AutoFit);
            Line("groups", View(stats.PairGroups), "0");
            Line("id", View(stats.PairIdMatched), "0");
            Line("heur", View(stats.PairHeuristic), "0");
            Line("rej", View(stats.PairRejected), "0");
            Line("cam", View(stats.PairCameraOnly), "0");
            Line("cuts", View(stats.PairCameraCuts), "0");
            ImPlot.EndPlot();
        }

        ImGui.TableNextColumn();
        if (Begin("Warp coverage (%)", size))
        {
            ImPlot.SetupAxisLimits(ImAxis.Y1, 0, 105, ImPlotCond.Always);
            Shaded("warped", View(stats.FrameWarped));
            ImPlot.EndPlot();
        }

        ImGui.EndTable();
        DrawReasons(stats);
    }

    private static bool Begin(string title, Vector2 size)
    {
        ImGui.TextDisabled(title);
        if (!ImPlot.BeginPlot($"##{title}", size, PlotFlags)) return false;

        ImPlot.SetupAxes("", "", TimeAxis, ValueAxis);
        ImPlot.SetupLegend(ImPlotLocation.NorthWest, ImPlotLegendFlags.Horizontal | ImPlotLegendFlags.NoButtons);
        return true;
    }

    private static void DrawReasons(InterpStats stats)
    {
        ImGui.TextDisabled("Pair decisions");
        ImGui.SameLine();
        foreach (var reason in Enum.GetValues<InterpSkip>())
        {
            ImGui.SameLine();
            ImGui.TextUnformatted($"{reason}: {stats.SkipReasons[(int)reason]}");
        }

        ImGui.TextDisabled("Tracked prims");
        ImGui.SameLine();
        ImGui.TextUnformatted($"ids {stats.LastPrimIds}  paired {stats.LastPrimsPaired}  " +
                             $"blended {stats.LastPrimsTracked} (3D {stats.LastPrimsSpatial})  " +
                             $"respawned {stats.LastPrimsRejected}");
    }

    private InterpRing View(InterpRing ring)
    {
        if (!_paused) return ring;
        if (_frozen.TryGetValue(ring, out var frozen)) return frozen;

        frozen = new InterpRing(ring.Data.Length);
        Array.Copy(ring.Data, frozen.Data, ring.Data.Length);
        frozen.Head = ring.Head;
        frozen.Count = ring.Count;
        _frozen[ring] = frozen;
        return frozen;
    }

    private static void Line(string label, InterpRing ring, string format)
    {
        if (ring.Count == 0) return;
        ImPlot.PlotLine($"{label} {ring.Last.ToString(format)}###{label}", ref ring.Data[0], ring.Count, 1.0, 0.0, ImPlotLineFlags.None, ring.Offset);
    }

    private static void Stairs(string label, InterpRing ring)
    {
        if (ring.Count == 0) return;
        ImPlot.PlotStairs($"{label} {ring.Last:0}###{label}", ref ring.Data[0], ring.Count, 1.0, 0.0, ImPlotStairsFlags.None, ring.Offset);
    }

    private static void Shaded(string label, InterpRing ring)
    {
        if (ring.Count == 0) return;
        var name = $"{label} {ring.Last:0}%###{label}";
        ImPlot.PlotShaded(name, ref ring.Data[0], ring.Count, 0.0, 1.0, 0.0, ImPlotShadedFlags.None, ring.Offset);
        ImPlot.PlotLine(name, ref ring.Data[0], ring.Count, 1.0, 0.0, ImPlotLineFlags.None, ring.Offset);
    }
}
