using System.Numerics;

namespace RecompOne.UI;

public static class GameStatsOverlay
{
    private const float Margin = 8f;
    private const float Padding = 6f;
    private static string[] _lines = [];
    private static Vector2 _size;
    private static long _refreshAt;

    public static void Draw(Vector2 imagePosition, Vector2 imageSize)
    {
        if (!InterfaceOptions.ShowFps || imageSize.X <= 0f || imageSize.Y <= 0f) return;

        var now = Environment.TickCount64;
        if (now >= _refreshAt) Refresh(now);

        var min = imagePosition + new Vector2(imageSize.X - _size.X - Margin, Margin);
        var max = min + _size;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.72f)), 4f);

        var textPosition = min + new Vector2(Padding, Padding);
        foreach (var line in _lines)
        {
            draw.AddText(textPosition, ImGui.GetColorU32(ImGuiCol.Text), line);
            textPosition.Y += ImGui.GetTextLineHeight();
        }
    }

    private static void Refresh(long now)
    {
        FrameClock.ReadFrameStats(out var average, out var p95, out var maximum, out var samples);
        var lines = new List<string>(7)
        {
            $"Present {FrameClock.PresentFps:F1} FPS  |  Game {FrameClock.Fps:F1} FPS",
            samples > 0
                ? $"Frame {FrameClock.LastFrameMs:F2} ms  avg {average:F2}  p95 {p95:F2}  max {maximum:F2}"
                : "Frame time: waiting for samples",
            $"Audio mix {Audio.LastMixMs:F2} ms  max {Audio.MaxMixMs:F2} ms",
            $"XA buffer {XaAudio.BufferedSamples * 1000.0 / 44100.0:F0} ms",
            $"GPU wait {GpuJobs.LastWaitMs:F2} ms  max {GpuJobs.MaxWaitMs:F2} ms",
            $"GC {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}"
        };

        if (RecompOne.Runtime.Interp.Interp.Backend is { } backend)
            lines.Add($"Interp {RecompOne.Runtime.Interp.Interp.EffectiveTarget} FPS  |  {backend.Stats.LastTris} tris / {backend.Stats.LastGroups} groups");

        var lineHeight = ImGui.GetTextLineHeight();
        var width = lines.Max(line => ImGui.CalcTextSize(line).X);
        _size = new Vector2(width + Padding * 2f, lineHeight * lines.Count + Padding * 2f);
        _lines = lines.ToArray();
        _refreshAt = now + 250;
    }
}
