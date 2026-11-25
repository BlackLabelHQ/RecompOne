using System.Diagnostics;

namespace RecompOne.Runtime.Host;

public static class FrameClock
{
    private const int SlipFrames = 8;

    private static readonly Stopwatch _clock = Stopwatch.StartNew();

    private static double _due;
    private static long _count;

    private static double _fpsAccumMs;
    private static int _fpsFrames;

    private static double _frameMark;
    private const int FrameSampleCount = 300;
    private static readonly double[] _frameSamples = new double[FrameSampleCount];
    private static readonly double[] _frameScratch = new double[FrameSampleCount];
    private static readonly object _frameStatsGate = new();
    private static int _frameSampleHead;
    private static int _frameSamplesUsed;

    private static double _presentStartMs;
    private static int _presentFrames;

    public static bool VSync { get; set; }

    public static double LastFrameMs { get; private set; }

    public static double Fps { get; private set; }

    public static double PresentFps { get; private set; }

    public static double FrameMs => Sdk.LibGpu.Pal ? 1000.0 / 50.0 : 1000.0 / 60.0;

    public static double Now => _clock.Elapsed.TotalMilliseconds;

    public static long Count => _count;

    public static double Due => _due;

    public static int Catch()
    {
        var now = Now;

        if (_due <= 0.0)
        {
            _due = now + FrameMs;
            return 0;
        }

        if (now < _due) return 0;

        var frame = FrameMs;
        var ticks = (int)((now - _due) / frame) + 1;

        _due = ticks > SlipFrames ? now + frame : _due + ticks * frame;
        _count += ticks;
        return ticks;
    }

    public static void Force()
    {
        var now = Now;
        _count++;
        _due = now + FrameMs;
    }

    public static void Resync()
    {
        _due = Now + FrameMs;
    }

    public static void MarkPresent()
    {
        var now = Now;
        _presentFrames++;

        var elapsed = now - _presentStartMs;
        if (elapsed < 1000.0) return;

        PresentFps = _presentFrames * 1000.0 / elapsed;
        _presentStartMs = now;
        _presentFrames = 0;
    }

    public static void MarkFrame()
    {
        var now = Now;

        if (_frameMark <= 0.0)
        {
            _frameMark = now;
            return;
        }

        LastFrameMs = now - _frameMark;
        _frameMark = now;

        lock (_frameStatsGate)
        {
            _frameSamples[_frameSampleHead] = LastFrameMs;
            _frameSampleHead = (_frameSampleHead + 1) % FrameSampleCount;
            if (_frameSamplesUsed < FrameSampleCount) _frameSamplesUsed++;
        }

        _fpsAccumMs += LastFrameMs;
        _fpsFrames++;
        if (_fpsAccumMs < 1000.0) return;

        Fps = _fpsFrames * 1000.0 / _fpsAccumMs;
        _fpsAccumMs = 0;
        _fpsFrames = 0;
    }

    public static void ReadFrameStats(out double average, out double p95, out double maximum, out int samples)
    {
        lock (_frameStatsGate)
        {
            samples = _frameSamplesUsed;
            if (samples == 0)
            {
                average = p95 = maximum = 0.0;
                return;
            }

            double total = 0.0;
            maximum = 0.0;
            for (var i = 0; i < samples; i++)
            {
                var value = _frameSamples[i];
                _frameScratch[i] = value;
                total += value;
                if (value > maximum) maximum = value;
            }

            Array.Sort(_frameScratch, 0, samples);
            average = total / samples;
            p95 = _frameScratch[Math.Clamp((int)Math.Ceiling(samples * 0.95) - 1, 0, samples - 1)];
        }
    }

}
