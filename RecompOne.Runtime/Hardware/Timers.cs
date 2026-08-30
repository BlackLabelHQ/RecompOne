using System.Diagnostics;

namespace RecompOne.Runtime.Hardware;

//This will NOT be fully accurate, this is just an approximation and can cause issues
//but it should work most of times, i have to revisit this if it starts causing issues
public sealed class Timers
{
    private const uint Base = 0x1F801100u;
    private const uint End = 0x1F801130u;

    private const double SysClock = 33868800.0;
    private const double HblankHz = 15780.0;
    private const double DotClock = 5322240.0;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly double[] _resetT = new double[3];
    private readonly ushort[] _mode = new ushort[3];
    private readonly ushort[] _target = new ushort[3];

    public static bool InRange(uint phys)
    {
        return phys >= Base && phys < End;
    }

    public bool TryRead(uint phys, out uint value)
    {
        value = 0;
        if (!InRange(phys)) return false;
        var t = (int)((phys - Base) / 0x10u);
        switch ((phys - Base) & 0xFu)
        {
            case 0x0: value = CurrentValue(t); break;
            case 0x4: value = _mode[t]; break;
            case 0x8: value = _target[t]; break;
        }

        return true;
    }

    public bool TryWrite(uint phys, uint value)
    {
        if (!InRange(phys)) return false;
        var t = (int)((phys - Base) / 0x10u);
        switch ((phys - Base) & 0xFu)
        {
            case 0x0: _resetT[t] = _clock.Elapsed.TotalSeconds; break;
            case 0x4:
                _mode[t] = (ushort)value;
                _resetT[t] = _clock.Elapsed.TotalSeconds;
                break;
            case 0x8: _target[t] = (ushort)value; break;
        }

        return true;
    }

    private ushort CurrentValue(int t)
    {
        var elapsed = _clock.Elapsed.TotalSeconds - _resetT[t];
        var ticks = (long)(elapsed * Rate(t));
        return (ushort)(ticks & 0xFFFF);
    }

    private double Rate(int t)
    {
        return t switch
        {
            0 => (_mode[0] & 0x100u) != 0 ? DotClock : SysClock,
            1 => (_mode[1] & 0x100u) != 0 ? HblankHz : SysClock,
            2 => (_mode[2] & 0x200u) != 0 ? SysClock / 8.0 : SysClock,
            _ => SysClock
        };
    }
}