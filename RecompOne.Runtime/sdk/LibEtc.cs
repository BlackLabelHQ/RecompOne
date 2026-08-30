using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Sdk;

public static class LibEtc
{
    private static int _vcount;
    private static readonly VSyncEvent _vsyncEvent = new();

    private const double HblankHz = 15734.0; //correct?

    private static int _lastVSyncCount;
    private static double _lastVSyncMs;

    public static void VSync(CpuContext c, IMemory m)
    {
        var mode = (int)c.A0;
        Log.Sdk($"VSync({mode})");
        if (mode < 0)
        {
            c.V0 = (uint)Interrupts.VBlankCount;
            return;
        }

        if (mode == 1)
        {
            c.V0 = Elapsed();
            return;
        }

        Runtime.PresentFrame();
        WaitVBlanks(c, m, mode == 0 ? 1 : mode);
        var elapsed = Elapsed();
        _lastVSyncCount = Interrupts.VBlankCount;
        _lastVSyncMs = Interrupts.ClockMs;
        _vcount++;

        if (Event.HasAnyListeners<VSyncEvent>())
        {
            var e = _vsyncEvent;
            e.Context = c;
            e.Memory = m;
            e.Frame = _vcount;
            Event.Dispatch(e);
        }

        c.V0 = elapsed;
    }

    private static uint Elapsed()
    {
        return (uint)((Interrupts.ClockMs - _lastVSyncMs) * HblankHz / 1000.0) & 0xFFFF;
    }

    private const double SleepMarginMs = 2.0;

    private static void WaitVBlanks(CpuContext c, IMemory m, int count)
    {
        var target = _lastVSyncCount + count;
        var floor = Interrupts.VBlankCount + 1;
        if (target < floor) target = floor;

        var began = Interrupts.ClockMs;

        while (Interrupts.VBlankCount < target)
        {
            var remaining = Interrupts.MsToNextVBlank;
            if (remaining > SleepMarginMs)
            {
                var ms = (int)(remaining - SleepMarginMs);
                if (ms > 0) Thread.Sleep(ms);
            }
            else
            {
                Thread.SpinWait(64);
            }

            Interrupts.PollNow(c, m);
        }

        var waited = Interrupts.ClockMs - began;

        var extra = Interrupts.VBlankCount - target;
    }
}