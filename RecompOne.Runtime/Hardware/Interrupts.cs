using System;
using System.Runtime.CompilerServices;
using RecompOne.Runtime.Bios;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Dispatch;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime;

public static class Interrupts
{
    private static bool _inHandler;
    private static bool _servicing;

    public static bool Servicing => _servicing;
    private static readonly bool[] _pending = new bool[16];

    private static bool _irqEnabled = true;

    private const uint IrqBits = 0x7FFu;
    private static uint _istat;
    private static uint _imask = IrqBits;

    public static uint ReadStat()
    {
        if (Hardware.Sio0.ConsumeAck()) Raise(7);
        return _istat;
    }

    public static uint ReadMask()
    {
        return _imask;
    }

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
                if (_irqEnabled) Log.Irq("EnterCriticalSection: irq turned off");
                _irqEnabled = false;
                break;
            case 2:
                if (!_irqEnabled) Log.Irq("ExitCriticalSection: irq turned on");
                _irqEnabled = true;
                cpu.V0 = 0u;
                DrainPending(cpu, mem);
                break;
            default:
                cpu.V0 = 0u;
                break;
        }
    }

    private static void DrainPending(CpuContext cpu, IMemory mem)
    {
        if (_inHandler) return;
        for (var i = 0; i < _pending.Length; i++)
        {
            if (!_pending[i] || Masked(i)) continue;
            _pending[i] = false;
            Deliver(i, cpu, mem);
        }
    }

    private const int PollInterval = 2048;
    private static int _countdown = PollInterval;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    public static double MsToNextVBlank
    {
        get
        {
            var left = Host.FrameClock.Due - Host.FrameClock.Now;
            return left > 0.0 ? left : 0.0;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)] //just making sure the stupid jit doenst fuck it up :D, it SHOULD be big enough now to not cause issues, but the previous one did
    private static void PollSlow(CpuContext cpu, IMemory mem)
    {
        _countdown = PollInterval;
        TickVBlank();
        Runtime.Timers?.Poll(RaiseTimer);
        if (_inHandler || _servicing || !_irqEnabled) return;

        var snap = cpu.Snapshot();
        TakeExceptionStack(cpu);
        try
        {
            DrainPending(cpu, mem);
            BiosB.PumpCardEvents(cpu, mem);
            Runtime.Cd?.AdvanceStreaming();
        }
        finally
        {
            cpu.Restore(snap);
        }
    }

    private const uint ExceptionStackTop = 0x0000E000u;

    private static void TakeExceptionStack(CpuContext cpu)
    {
        cpu.SP = ExceptionStackTop;
        cpu.FP = ExceptionStackTop;
    }

    public static bool Turbo { get; set; }

    public static int VBlankCount => (int)Host.FrameClock.Count;

    public static double ClockMs => Host.FrameClock.Now;

    public static void ForceVBlank(CpuContext cpu, IMemory mem) //not ideal i believe
    {
        Host.FrameClock.Force();
        Raise(0);
        PollNow(cpu, mem);
    }

    private static void TickVBlank()
    {
        if (Host.FrameClock.Catch() > 0) Raise(0);
    }

    public static void ResyncVBlank()
    {
        Host.FrameClock.Resync();
    }

    private static void RaiseTimer(int irq)
    {
        if ((uint)irq >= _pending.Length) return;
        _istat |= 1u << irq;
        _pending[irq] = true;
    }

    public static void Raise(int irq)
    {
        if ((uint)irq >= _pending.Length) return;
        _istat |= 1u << irq;
        _pending[irq] = true;
        _countdown = 1;
    }

    public static void ClearPending()
    {
        Array.Clear(_pending);
        _istat = 0u;
        _inHandler = false;
    }

    private static bool Callable(uint addr)
    {
        if (addr == 0u || (addr & 3u) != 0u) return false;
        var ram = addr & 0x1FFFFFFFu;
        if (ram < 0x00010000u || ram >= 0x00200000u) return false;
        return Dispatcher.CanCall(addr);
    }

    private static bool Masked(int irq)
    {
        return (_imask & (1u << irq)) == 0;
    }

    public static void Deliver(int irq, CpuContext cpu, IMemory mem)
    {
        if ((uint)irq >= _pending.Length) return;

        _istat |= 1u << irq;

        if (_inHandler || !_irqEnabled || Masked(irq))
        {
            _pending[irq] = true;
            return;
        }

        _inHandler = true;
        try
        {
            Dispatch(irq, cpu, mem);

            var again = true;
            while (again)
            {
                again = false;
                for (var i = 0; i < _pending.Length; i++)
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

    private static void Dispatch(int irq, CpuContext cpu, IMemory mem)
    {
        ServiceIrq(irq, cpu, mem);

        for (var i = 0; i < _pending.Length; i++)
        {
            if (i == irq || ((_istat & (1u << i)) == 0 && !_pending[i])) continue;
            _pending[i] = false;
            ServiceIrq(i, cpu, mem);
        }
    }

    private static void ServiceIrq(int irq, CpuContext cpu, IMemory mem)
    {
        BiosB.DeliverIrqEvents(cpu, mem, irq);

        DispatchChains(cpu, mem);

        var intrEnv = BiosB.IntrEnvInInterruptAddr;
        var slot = intrEnv + 2u + (uint)irq * 4u;
        var handler = intrEnv != 0 ? mem.ReadU32(slot) : 0u;
        Log.Irq($"irq {irq} env=0x{intrEnv:X8} handler=0x{handler:X8} mask=0x{_imask:X}");
        if (handler != 0 && !Callable(handler))
        {
            Console.WriteLine($"[Interrupts] dropping stale handler 0x{handler:X8} for irq {irq}");
            mem.WriteU32(slot, 0u);
            handler = 0u;
        }

        if (handler == 0)
        {
            Ack(irq);
            return;
        }
        var snap = cpu.Snapshot();
        TakeExceptionStack(cpu);
        mem.WriteU16(intrEnv, 1);
        var prev = _servicing;
        _servicing = true;
        try
        {
            Dispatcher.Call(cpu, mem, handler);
        }
        finally
        {
            _servicing = prev;
        }

        mem.WriteU16(intrEnv, 0);
        cpu.Restore(snap);
        if (!_pending[irq]) Ack(irq);
    }

    private static bool DispatchChains(CpuContext cpu, IMemory mem)
    {
        var handled = false;
        var snap = cpu.Snapshot();
        TakeExceptionStack(cpu);
        var prev = _servicing;
        _servicing = true;
        try
        {
            for (var priority = 0; priority < 4; priority++)
            {
                var node = BiosB.IntChain(priority);
                var guard = 0;
                while (node != 0 && guard++ < 32)
                {
                    if ((node & 3u) != 0u || (node & 0x1FFFFFFFu) >= 0x00200000u) break;

                    var verifier = mem.ReadU32(node + 8u);
                    var handler = mem.ReadU32(node + 4u);
                    if (verifier != 0 && !Callable(verifier)) break;
                    if (handler != 0 && !Callable(handler)) break;

                    if (verifier != 0)
                    {
                        Dispatcher.Call(cpu, mem, verifier);
                        var taken = cpu.V0;
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

    private static void Ack(int irq)
    {
        _istat &= ~(1u << irq);
    }
}