using System.Diagnostics;

namespace RecompOne.Runtime.Host;

internal static class FrameClock
{
    const double FrameMs = 1000.0 / 60.0;
    const double SpinMs = 1.5;

    static readonly Stopwatch _clock = Stopwatch.StartNew();
    static double _nextFrameMs;

    public static bool VSync { get; set; }

    public static double LastFrameMs { get; private set; }

    public static double Fps { get; private set; }
    static double _fpsAccumMs;
    static int _fpsFrames;
    public static double LastWaitMs { get; private set; }

    static double _lastStart;


    public static void Throttle()
    {
        double now = _clock.Elapsed.TotalMilliseconds;
        LastFrameMs = now - _lastStart;
        _lastStart = now;

        _fpsAccumMs += LastFrameMs;
        _fpsFrames++;
        if (_fpsAccumMs >= 1000.0)
        {
            Fps = _fpsFrames * 1000.0 / _fpsAccumMs;
            _fpsAccumMs = 0;
            _fpsFrames = 0;
        }

        _nextFrameMs += FrameMs;
        double wait = _nextFrameMs - now;

        if (wait < -100)
        {
            _nextFrameMs = now;
            LastWaitMs = 0;
            return;
        }

        if (wait <= 0)
        {
            LastWaitMs = 0;
            return;
        }

        if (VSync && wait < FrameMs * 0.75)
        {
            LastWaitMs = 0;
            return;
        }

        double sleepUntil = _nextFrameMs - SpinMs;
        if (now < sleepUntil)
        {
            int ms = (int)(sleepUntil - now);
            if (ms > 0) Thread.Sleep(ms);
        }

        while (_clock.Elapsed.TotalMilliseconds < _nextFrameMs)
            Thread.SpinWait(48);

        LastWaitMs = wait;
    }

    public static void Resync() => _nextFrameMs = _clock.Elapsed.TotalMilliseconds;
}
