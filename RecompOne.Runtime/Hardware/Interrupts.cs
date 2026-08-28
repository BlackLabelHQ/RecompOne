using System;
using System.Runtime.CompilerServices;
using RecompOne.Runtime.Bios;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Dispatch;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime;

public static class Interrupts
{
    static bool _inHandler;
    static bool _servicing;

    public static bool Servicing => _servicing;
    static readonly bool[] _pending = new bool[16];

    static bool _irqEnabled = true;

    const uint IrqBits = 0x7FFu;
    static uint _istat;
    static uint _imask = IrqBits;

    public static uint ReadStat()
    {
        if (Hardware.Sio0.ConsumeAck()) Raise(7);
        return _istat;
    }
    public static uint ReadMask() => _imask;

    public static void WriteStat(uint value)
    {
        _istat &= value & IrqBits;
    }
    public static void WriteMask(uint value)
    {
        Log.Irq($"imask {_imask:X3} -> {value & IrqBits:X3}");
        _imask = value & IrqBits;
    }

    public static void Syscall(CpuContext cpu, IMemory mem)
    {
        switch (cpu.A0)
        {
            case 1:
                cpu.V0 = _irqEnabled ? 1u : 0u;
                _irqEnabled = false;
                break;
            case 2:
                _irqEnabled = true;
                cpu.V0 = 0u;
                DrainPending(cpu, mem);
                break;
            default:
                cpu.V0 = 0u;
                break;
        }
    }

    static void DrainPending(CpuContext cpu, IMemory mem)
    {
        if (_inHandler) return;
        for (int i = 0; i < _pending.Length; i++)
        {
            if (!_pending[i] || Masked(i)) continue;
            _pending[i] = false;
            Deliver(i, cpu, mem);
        }
    }

    const int PollInterval = 2048;
    static int _countdown = PollInterval;

    public static void Poll(CpuContext cpu, IMemory mem)
    {
        if (--_countdown > 0) return;
        PollSlow(cpu, mem);
    }

    public static void PollNow(CpuContext cpu, IMemory mem)
    {
        _countdown = 1;
        PollSlow(cpu, mem);
    }

    public static double MsToNextVBlank => _nextVBlank - _vblankClock.Elapsed.TotalMilliseconds;

    [MethodImpl(MethodImplOptions.NoInlining)] //just making sure the stupid jit doenst fuck it up :D, it SHOULD be big enough now to not cause issues, but the previous one did
    static void PollSlow(CpuContext cpu, IMemory mem)
    {
        _countdown = PollInterval;
        TickVBlank();
        if (_inHandler || _servicing || !_irqEnabled) return;
        DrainPending(cpu, mem);
        BiosB.PumpCardEvents(cpu, mem);
        Runtime.Cd?.AdvanceStreaming();
    }

    const double VBlankMs = 1000.0 / 60.0;
    static readonly System.Diagnostics.Stopwatch _vblankClock = System.Diagnostics.Stopwatch.StartNew();
    static double _nextVBlank = VBlankMs;

    static int _vblankCount;

    public static int VBlankCount => _vblankCount;

    public static double ClockMs => _vblankClock.Elapsed.TotalMilliseconds;

    static void TickVBlank()
    {
        double now = _vblankClock.Elapsed.TotalMilliseconds;
        if (now < _nextVBlank) return;
        double late = now - _nextVBlank;
        
        _nextVBlank = late > VBlankMs * 2 ? now + VBlankMs : now + VBlankMs - late;
        _vblankCount++;
        Raise(0);
    }

    public static void Raise(int irq)
    {
        if ((uint)irq >= _pending.Length) return;
        _istat |= 1u << irq;
        _pending[irq] = true;
        _countdown = 1;
    }

    static bool Masked(int irq) => (_imask & (1u << irq)) == 0;

    public static void Deliver(int irq, CpuContext cpu, IMemory mem)
    {
        if ((uint)irq >= _pending.Length) return;

        _istat |= 1u << irq;

        if (_inHandler || !_irqEnabled || Masked(irq)) { _pending[irq] = true; return; }

        _inHandler = true;
        try
        {
            Dispatch(irq, cpu, mem);

            bool again = true;
            while (again)
            {
                again = false;
                for (int i = 0; i < _pending.Length; i++)
                {
                    if (!_pending[i] || Masked(i)) continue;
                    _pending[i] = false;
                    Dispatch(i, cpu, mem);
                    again = true;
                }
            }
        }
        finally
        {
            _inHandler = false;
        }
    }

    static void Dispatch(int irq, CpuContext cpu, IMemory mem)
    {
        ServiceIrq(irq, cpu, mem);

        for (int i = 0; i < _pending.Length; i++)
        {
            if (i == irq || ((_istat & (1u << i)) == 0 && !_pending[i])) continue;
            _pending[i] = false;
            ServiceIrq(i, cpu, mem);
        }
    }

    static void ServiceIrq(int irq, CpuContext cpu, IMemory mem)
    {
        BiosB.DeliverIrqEvents(cpu, mem, irq);

        DispatchChains(cpu, mem);

        uint intrEnv = BiosB.IntrEnvInInterruptAddr;
        uint handler = intrEnv != 0 ? mem.ReadU32(intrEnv + 2u + (uint)irq * 4u) : 0u;
        Log.Irq($"irq {irq} env=0x{intrEnv:X8} handler=0x{handler:X8} mask=0x{_imask:X}");
        if (handler == 0) { Ack(irq); return; }

        //takes a snap, apparently interrupt callbacks dont operate at the same context? could be wrong in mips3000, need to check furter TODO, seens to be accurate
        var snap = cpu.Snapshot();
        mem.WriteU16(intrEnv, 1);
        bool prev = _servicing;
        _servicing = true;
        try { Dispatcher.Call(cpu, mem, handler); }
        finally { _servicing = prev; }
        mem.WriteU16(intrEnv, 0);
        cpu.Restore(snap);
        if (!_pending[irq]) Ack(irq);
    }

    static bool DispatchChains(CpuContext cpu, IMemory mem)
    {
        bool handled = false;
        var snap = cpu.Snapshot();
        bool prev = _servicing;
        _servicing = true;
        try
        {
            for (int priority = 0; priority < 4; priority++)
            {
                uint node = BiosB.IntChain(priority);
                int guard = 0;
                while (node != 0 && guard++ < 32)
                {
                    uint verifier = mem.ReadU32(node + 8u);
                    uint handler = mem.ReadU32(node + 4u);
                    if (verifier != 0)
                    {
                        Dispatcher.Call(cpu, mem, verifier);
                        uint taken = cpu.V0;
                        if (taken != 0)
                        {
                            handled = true;
                            if (handler != 0)
                            {
                                cpu.A0 = taken;
                                Dispatcher.Call(cpu, mem, handler);
                            }
                        }
                    }
                    node = mem.ReadU32(node);
                }
            }
        }
        finally
        {
            _servicing = prev;
            cpu.Restore(snap);
        }
        return handled;
    }

    static void Ack(int irq) => _istat &= ~(1u << irq);
}
