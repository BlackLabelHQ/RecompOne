using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Sdk;

public static class LibEtc
{
    static int _vcount;
    static readonly VSyncEvent _vsyncEvent = new();

    const double HblankHz = 15734.0; //correct?

    static int _lastVSyncCount;
    static double _lastVSyncMs;

    public static void VSync(CpuContext c, IMemory m)
    {
        int mode = (int)c.A0;
        Log.Sdk($"VSync({mode})");
        if (mode < 0) { c.V0 = (uint)Interrupts.VBlankCount; return; }
        if (mode == 1) { c.V0 = Elapsed(); return; }

        Runtime.PresentFrame();
        WaitVBlanks(c, m, mode == 0 ? 1 : mode);
        uint elapsed = Elapsed();
        _lastVSyncCount = Interrupts.VBlankCount;
        _lastVSyncMs = Interrupts.ClockMs;
        _vcount++;

        if (Event.HasAnyListeners<VSyncEvent>())
        {
            var e = _vsyncEvent;
            e.Context = c; e.Memory = m;
            e.Frame = _vcount;
            Event.Dispatch(e);
        }

        c.V0 = elapsed;
    }

    static uint Elapsed() => (uint)((Interrupts.ClockMs - _lastVSyncMs) * HblankHz / 1000.0) & 0xFFFF;

    static void WaitVBlanks(CpuContext c, IMemory m, int count)
    {
        int target = _lastVSyncCount + count;
        while (Interrupts.VBlankCount < target)
        {
            double remaining = Interrupts.MsToNextVBlank;
            if (remaining > 1.0) Thread.Sleep(1);
            else Thread.SpinWait(256);

            Interrupts.PollNow(c, m);
        }
    }
}
