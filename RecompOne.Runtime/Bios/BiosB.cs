using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Bios;

public static class BiosB
{
    private static readonly PadReadEvent _padEvent = new();

    private struct EvCB
    {
        public uint Status, Class, Spec, Mode, Func;
    }

    private const int MaxEvents = 64;
    private static readonly EvCB[] _evCBs = new EvCB[MaxEvents];

    private struct TCB
    {
        public bool Used;
    }

    private const int MaxThreads = 4;
    private static readonly TCB[] _tcbs = new TCB[MaxThreads];

    private static readonly uint[] _intChain = new uint[4];

    public static uint IntrEnvInInterruptAddr = 0u;

    public static uint IntChain(int priority)
    {
        return (uint)priority < 4u ? _intChain[priority] : 0u;
    }

    private static uint _padBuf;
    private static uint _padCardBuf1, _padCardBuf2;
    private static bool _padCardStarted;
    private static bool _cardPadEnable;

    private const uint B0TableAddr = 0x00000874u;
    private const uint KernelStubBase = 0x00006000u;
    private const int B0TableEntries = 0xB0;
    private const uint ChangeClearPadEntry = KernelStubBase + 0x5Bu * 4u;

    public const uint PadInitStub = ChangeClearPadEntry + 0x884u;
    public const uint PadStopStub = ChangeClearPadEntry + 0x894u;

    public static uint GetB0Table(IMemory m) //gen this
    {
        for (var n = 0; n < B0TableEntries; n++)
            m.WriteU32(B0TableAddr + (uint)n * 4u, KernelStubBase + (uint)n * 4u);

        return B0TableAddr;
    }

    public static bool KernelStub(uint addr, out uint fn)
    {
        if (addr == PadInitStub)
        {
            fn = 0x15u;
            return true;
        }

        if (addr == PadStopStub)
        {
            fn = 0x16u;
            return true;
        }
        
        if ((addr & 3u) == 0u && addr >= KernelStubBase && addr < KernelStubBase + (uint)B0TableEntries * 4u)
        {
            fn = (addr - KernelStubBase) / 4u;
            return true;
        }
        
        fn = 0u;
        return false;
    }

    private const uint KernelPadBuf1 = 0x0000E100u;
    private const uint KernelPadBuf2 = 0x0000E140u;
    private const uint PadSlotSize = 0x22u;

    public static uint PadBuffer1 => _padCardBuf1;
    public static uint PadBuffer2 => _padCardBuf2;

    public static void ResetCallbacks()
    {
        Array.Clear(_intChain);
        IntrEnvInInterruptAddr = 0u;
    }

    public static void Reset()
    {
        Array.Clear(_evCBs);
        Array.Clear(_tcbs);
        Array.Clear(_intChain);
        IntrEnvInInterruptAddr = 0u;
        _padBuf = 0u;
        _padCardBuf1 = _padCardBuf2 = 0u;
        _padCardStarted = false;
        _cardPadEnable = false;
    }

    public static void DeliverEvent(uint @class, uint spec)
    {
        for (var i = 0; i < MaxEvents; i++)
            if (_evCBs[i].Status == 2u && _evCBs[i].Class == @class && _evCBs[i].Spec == spec)
                _evCBs[i].Status = 4u;
    }

    public static void DeliverEventIntr(CpuContext c, IMemory m, uint @class, uint spec)
    {
        for (var i = 0; i < MaxEvents; i++)
        {
            if (_evCBs[i].Status != 2u || _evCBs[i].Class != @class || _evCBs[i].Spec != spec) continue;
            if ((_evCBs[i].Mode & 0x1000u) != 0 && _evCBs[i].Func != 0u)
            {
                if (!RecompOne.Runtime.Dispatch.Dispatcher.CanCall(_evCBs[i].Func))
                {
                    Console.WriteLine($"[Bios] dropping an stale event callback 0x{_evCBs[i].Func:X8}");
                    _evCBs[i].Func = 0u;
                    continue;
                }

                var snap = c.Snapshot();
                RecompOne.Runtime.Dispatch.Dispatcher.Call(c, m, _evCBs[i].Func);
                c.Restore(snap);
            }
            else
            {
                _evCBs[i].Status = 4u;
            }
        }
    }

    //not sure if the adresses are correct for all games, todo: verify if this is correct, (note to self openbios does this way and seens to be valid with sotn psyq decomp 
    private static readonly uint[] IrqClass =
    {
        0xF0000001u, 0xF0000002u, 0xF0000003u, 0xF0000004u, 0xF0000005u, 0xF0000006u, 0xF0000007u, 0xF0000008u,
        0xF000000Bu, 0xF0000009u, 0xF000000Au
    };

    private const uint EvSpINT = 0x0002u;
    private const uint EvSpGENERAL = 0x1000u;

    public static void DeliverIrqEvents(CpuContext c, IMemory m, int irq)
    {
        switch (irq)
        {
            case 0: DeliverEventIntr(c, m, 0xF2000003u, EvSpINT); break;
            case 4: DeliverEventIntr(c, m, 0xF2000000u, EvSpINT); break;
            case 5: DeliverEventIntr(c, m, 0xF2000001u, EvSpINT); break;
            case 6: DeliverEventIntr(c, m, 0xF2000002u, EvSpINT); break;
        }

        if ((uint)irq < IrqClass.Length)
            DeliverEventIntr(c, m, IrqClass[irq], EvSpGENERAL);
    }

    private static readonly Queue<uint> _cardEvents = new();
    private static bool _pumpingCard;


    public static void CardComplete(CpuContext c, IMemory m, uint port)
    {
        CardComplete(port);
    }

    public static void CardComplete(uint port)
    {
        var card = (port & 0x10u) != 0 ? Runtime.CardB : Runtime.CardA;
        var spec = card.Enabled ? 0x0004u : 0x0100u;

        lock (_cardEvents)
        {
            _cardEvents.Enqueue(spec); //fixes formedievil, needs to raise 7 on irq, is this the intended behaviour?
        }

        _cardEventFrame = Interrupts.VBlankCount;
        Interrupts.Raise(7);
    }

    private static int _cardEventFrame = -1;

    public static void PumpCardEvents(CpuContext c, IMemory m)
    {
        PumpCardEvents(c, m, false);
    }

    //canr send all in one go otherwise medievil bitches and breaks
    public static void PumpCardEvents(CpuContext c, IMemory m, bool now)
    {
        if (_pumpingCard || (!now && Interrupts.VBlankCount == _cardEventFrame)) return;

        _pumpingCard = true;
        try
        {
            while (true)
            {
                uint spec;
                lock (_cardEvents)
                {
                    if (_cardEvents.Count == 0) break;
                    spec = _cardEvents.Dequeue();
                }

                //Log.Bios($"  card complete w/ spec {spec:X4}");
                DeliverEventIntr(c, m, 0xF4000001u, spec);
                DeliverEventIntr(c, m, 0xF0000011u, spec);
            }
        }
        finally
        {
            _pumpingCard = false;
        }
    }

    private static void CardRead(CpuContext c, IMemory m)
    {
        var card = (c.A0 & 0x10u) != 0 ? Runtime.CardB : Runtime.CardA;
        if (card.Enabled && c.A2 != 0u)
        {
            Span<byte> f = stackalloc byte[0x80];
            card.FrameRead((int)(c.A1 & 0x3FFu), f);
            for (uint i = 0; i < 0x80u; i++) m.WriteU8(c.A2 + i, f[(int)i]);
        }

        CardComplete(c, m, c.A0);
        c.V0 = 1u;
    }

    private static void CardWrite(CpuContext c, IMemory m)
    {
        var card = (c.A0 & 0x10u) != 0 ? Runtime.CardB : Runtime.CardA;
        if (card.Enabled && c.A2 != 0u)
        {
            Span<byte> f = stackalloc byte[0x80];
            for (uint i = 0; i < 0x80u; i++) f[(int)i] = m.ReadU8(c.A2 + i);
            card.FrameWrite((int)(c.A1 & 0x3FFu), f);
        }

        CardComplete(c, m, c.A0);
        c.V0 = 1u;
    }

    public static uint GetFreeEvSlot()
    {
        for (var i = 0; i < MaxEvents; i++)
            if (_evCBs[i].Status == 0u)
                return (uint)i;
        return 0xFFFFFFFFu;
    }

    private static ushort FirePad(IMemory m, int port, ushort buttons)
    {
        if (!Event.HasAnyListeners<PadReadEvent>()) return buttons;
        var e = _padEvent;
        e.Context = Runtime.Cpu!;
        e.Memory = m;
        e.Port = port;
        e.Buttons = buttons;
        Event.Dispatch(e);
        return e.Buttons;
    }

    private static void PadRead(IMemory m)
    {
        if (_padBuf == 0) return;
        var s = Hardware.Controller.State;
        var swapped = (ushort)((s >> 8) | (s << 8));
        var s2 = Hardware.Controller.State2;
        var swapped2 = (ushort)((s2 >> 8) | (s2 << 8));
        swapped = FirePad(m, 0, swapped);
        swapped2 = FirePad(m, 1, swapped2);
        m.WriteU32(_padBuf, ((uint)swapped2 << 16) | swapped);
        m.WriteU8(_padBuf + 4, Hardware.Controller.RightX);
        m.WriteU8(_padBuf + 5, Hardware.Controller.RightY);
        m.WriteU8(_padBuf + 6, Hardware.Controller.LeftX);
        m.WriteU8(_padBuf + 7, Hardware.Controller.LeftY);
    }

    private static void InitPad(IMemory m, uint buf1, uint siz1, uint buf2, uint siz2)
    {
        _padCardBuf1 = buf1;
        _padCardBuf2 = buf2;
        for (uint i = 0; i < siz1; i++) m.WriteU8(buf1 + i, 0);
        for (uint i = 0; i < siz2; i++) m.WriteU8(buf2 + i, 0);
    }

    private static void PadCardIrq(IMemory m)
    {
        if (!_padCardStarted) return;

        var b1 = Unswap(FirePad(m, 0, Swap(Hardware.Controller.State)));
        WritePadSlot(m, _padCardBuf1, true, b1,
            Hardware.Controller.RightX, Hardware.Controller.RightY,
            Hardware.Controller.LeftX, Hardware.Controller.LeftY, Hardware.Controller.Analog);

        var b2 = Unswap(FirePad(m, 1, Swap(Hardware.Controller.State2)));
        WritePadSlot(m, _padCardBuf2, Hardware.Controller.Connected2, b2,
            Hardware.Controller.RightX2, Hardware.Controller.RightY2,
            Hardware.Controller.LeftX2, Hardware.Controller.LeftY2, Hardware.Controller.Analog2);
    }

    private static ushort Swap(ushort v)
    {
        return (ushort)((v >> 8) | (v << 8));
    }

    private static ushort Unswap(ushort v)
    {
        return (ushort)((v >> 8) | (v << 8));
    }

    private static void WritePadSlot(IMemory m, uint buf, bool connected, ushort buttons,
        byte rx, byte ry, byte lx, byte ly, bool analog = false)
    {
        if (buf == 0) return;
        if (!connected)
        {
            m.WriteU8(buf, 0xFF);
            m.WriteU8(buf + 1, 0);
            return;
        }

        m.WriteU8(buf, 0);
        m.WriteU8(buf + 1, analog ? (byte)0x73 : (byte)0x41);
        m.WriteU8(buf + 2, (byte)buttons);
        m.WriteU8(buf + 3, (byte)(buttons >> 8));
        m.WriteU8(buf + 4, rx);
        m.WriteU8(buf + 5, ry);
        m.WriteU8(buf + 6, lx);
        m.WriteU8(buf + 7, ly);
    }

    public static void RefreshPad(IMemory m)
    {
        PadRead(m);
        PadCardIrq(m);
    }

    public static void Dispatch(CpuContext c, IMemory m, uint fn)
    {
        Log.Bios($"B({fn:X2}) {BiosNames.B(fn)}");
        switch (fn)
        {
            case 0x00: c.V0 = 0u; break;
            case 0x01: break;
            case 0x02: c.V0 = 0u; break;
            case 0x03: c.V0 = 0u; break;
            case 0x04: break;
            case 0x05: break;
            case 0x06: break;
            case 0x07:
                Log.Bios($"  DeliverEvent class={c.A0:X8} spec={c.A1:X4}");
                DeliverEventIntr(c, m, c.A0, c.A1);
                break;
            case 0x08:
                c.V0 = OpenEvent(c.A0, c.A1, c.A2, c.A3);
                Log.Bios($"  OpenEvent class={c.A0:X8} spec={c.A1:X4} mode={c.A2:X4} func={c.A3:X8} -> {c.V0:X8}");
                break;
            case 0x09:
                CloseEvent(c.A0);
                c.V0 = 1u;
                break;
            case 0x0A: c.V0 = WaitEvent(c.A0); break;
            case 0x0B:
                c.V0 = TestEvent(c.A0);
                Log.Bios($"  TestEvent {c.A0:X8} ({EvClass(c.A0):X8}/{EvSpec(c.A0):X4}) -> {c.V0}");
                break;
            case 0x0C:
                EnableEvent(c.A0);
                c.V0 = 1u;
                break;
            case 0x0D:
                DisableEvent(c.A0);
                c.V0 = 1u;
                break;
            case 0x0E: c.V0 = OpenTh(c.A0, c.A1, c.A2); break;
            case 0x0F:
                CloseTh(c.A0);
                c.V0 = 1u;
                break;
            case 0x10: break;
            case 0x11: break;
            case 0x12: InitPad(m, c.A0, c.A1, c.A2, c.A3); break;
            case 0x13: _padCardStarted = true; break;
            case 0x14: _padCardStarted = false; break;
            case 0x15: _padBuf = c.A1; break;
            case 0x16: PadRead(m); break;
            case 0x17: break;
            case 0x18: IntrEnvInInterruptAddr = 0u; break;
            case 0x19: IntrEnvInInterruptAddr = c.A0 != 0u ? c.A0 - 0x36u : 0u; break;
            case 0x1A: break;
            case 0x1B: break;
            case 0x1C: break;
            case 0x1D: break;
            case 0x1E: break;
            case 0x1F: break;
            case 0x20: UnDeliverEvent(c.A0, c.A1); break;
            case 0x2B: break;
            case 0x2C: break;
            case 0x2D: break;
            case 0x2E: break;
            case 0x2F: c.V0 = 0u; break;
            case 0x30: c.V0 = 0u; break;
            case 0x31: c.V0 = 0u; break;
            case 0x32: BiosA.Dispatch(c, m, 0x00); break;
            case 0x33: BiosA.Dispatch(c, m, 0x01); break;
            case 0x34: BiosA.Dispatch(c, m, 0x02); break;
            case 0x35: BiosA.Dispatch(c, m, 0x03); break;
            case 0x36: BiosA.Dispatch(c, m, 0x04); break;
            case 0x37: BiosA.Dispatch(c, m, 0x05); break;
            case 0x38: BiosA.Dispatch(c, m, 0x06); break;
            case 0x39: c.V0 = c.A0 <= 2u ? 2u : 0u; break;
            case 0x3A: c.V0 = 0xFFFFFFFFu; break;
            case 0x3B:
                Console.Write((char)(c.A0 & 0xFF));
                c.V0 = c.A0;
                break;
            case 0x3C: c.V0 = 0xFFFFFFFFu; break;
            case 0x3D:
                Console.Write((char)(c.A0 & 0xFF));
                c.V0 = c.A0;
                break;
            case 0x3E: c.V0 = 0u; break;
            case 0x3F:
                Console.Write(Bios.ReadString(m, c.A0));
                c.V0 = c.A0;
                break;
            case 0x40: c.V0 = 1u; break;
            case 0x41: c.V0 = BiosA.CardFormat(m, c.A0); break;
            case 0x42: c.V0 = BiosA.FirstFile(m, c.A0, c.A1); break;
            case 0x43: c.V0 = BiosA.NextFile(m, c.A0); break;
            case 0x44: c.V0 = 0u; break;
            case 0x45: c.V0 = BiosA.CardDelete(m, c.A0); break;
            case 0x46: c.V0 = 0u; break;
            case 0x47: c.V0 = GetFreeEvSlot(); break;
            case 0x48: c.V0 = 0xFFFFFFFFu; break;
            case 0x49: break;
            case 0x4A:
                _cardPadEnable = c.A0 != 0u;
                c.V0 = 1u;
                break;
            case 0x4B:
                if (_cardPadEnable && _padCardBuf1 == 0u)
                    InitPad(m, KernelPadBuf1, PadSlotSize, KernelPadBuf2, PadSlotSize);
                if (_cardPadEnable) _padCardStarted = true;
                c.V0 = 1u;
                break;
            case 0x4C:
                if (_cardPadEnable) _padCardStarted = false;
                c.V0 = 1u;
                break;
            case 0x4D: break;
            case 0x4E: CardWrite(c, m); break;
            case 0x4F: CardRead(c, m); break;
            case 0x50: break;
            case 0x51: c.V0 = KromFont.Krom2RawAdd(c.A0); break;
            case 0x53: c.V0 = KromFont.Krom2Offset(c.A0); break;
            case 0x54: c.V0 = BiosA.LastErrno; break;
            case 0x55: c.V0 = 0u; break;
            case 0x56: c.V0 = 0u; break;
            case 0x57: c.V0 = GetB0Table(m); break;
            case 0x58: break;
            case 0x59: c.V0 = BiosA.TestDevice(m, c.A0); break;
            case 0x5B: c.V0 = 0u; break;
            case 0x5C: c.V0 = 1u; break;
            case 0x5D: c.V0 = 1u; break;
            default: break;
        }
    }

    private static uint OpenEvent(uint @class, uint spec, uint mode, uint func)
    {
        for (var i = 0; i < MaxEvents; i++)
            if (_evCBs[i].Status == 0u)
            {
                _evCBs[i] = new EvCB { Status = 1u, Class = @class, Spec = spec, Mode = mode, Func = func };
                return 0xF1000000u | (uint)i;
            }

        return 0xFFFFFFFFu;
    }

    private static uint EvClass(uint ev)
    {
        var s = EvSlot(ev);
        return s >= 0 ? _evCBs[s].Class : 0u;
    }

    private static uint EvSpec(uint ev)
    {
        var s = EvSlot(ev);
        return s >= 0 ? _evCBs[s].Spec : 0u;
    }

    private static int EvSlot(uint ev)
    {
        var i = (int)(ev & 0xFFFFu);
        return i < MaxEvents ? i : -1;
    }

    private static void CloseEvent(uint ev)
    {
        var s = EvSlot(ev);
        if (s >= 0) _evCBs[s] = default;
    }

    private static uint WaitEvent(uint ev)
    {
        var s = EvSlot(ev);
        if (s >= 0 && _evCBs[s].Status == 4u) _evCBs[s].Status = 2u;
        return 1u;
    }

    private static uint TestEvent(uint ev)
    {
        var s = EvSlot(ev);
        if (s >= 0 && _evCBs[s].Status == 4u)
        {
            _evCBs[s].Status = 2u;
            return 1u;
        }

        return 0u;
    }

    private static void EnableEvent(uint ev)
    {
        var s = EvSlot(ev);
        if (s >= 0) _evCBs[s].Status = 2u;
    }

    private static void DisableEvent(uint ev)
    {
        var s = EvSlot(ev);
        if (s >= 0 && _evCBs[s].Status != 0u) _evCBs[s].Status = 1u;
    }

    private static void UnDeliverEvent(uint @class, uint spec)
    {
        for (var i = 0; i < MaxEvents; i++)
            if (_evCBs[i].Status == 4u && _evCBs[i].Class == @class && _evCBs[i].Spec == spec)
                _evCBs[i].Status = 2u;
    }

    private static uint OpenTh(uint pc, uint spFp, uint gp)
    {
        for (var i = 0; i < MaxThreads; i++)
            if (!_tcbs[i].Used)
            {
                _tcbs[i] = new TCB { Used = true };
                return 0xFF000000u | (uint)i;
            }

        return 0xFFFFFFFFu;
    }

    private static void CloseTh(uint handle)
    {
        var i = (int)(handle & 0xFFu);
        if (i < MaxThreads) _tcbs[i] = default;
    }

    public static void SysEnqIntRP(CpuContext c, IMemory m)
    {
        var priority = c.A0 & 3u;
        var struc = c.A1;
        c.V0 = _intChain[priority];
        m.WriteU32(struc, _intChain[priority]);
        _intChain[priority] = struc;
    }

    public static void SysDeqIntRP(CpuContext c, IMemory m)
    {
        var priority = c.A0 & 3u;
        var struc = c.A1;
        if (_intChain[priority] == struc)
        {
            _intChain[priority] = m.ReadU32(struc);
            c.V0 = 1u;
            return;
        }

        var cur = _intChain[priority];
        while (cur != 0u)
        {
            var next = m.ReadU32(cur);
            if (next == struc)
            {
                m.WriteU32(cur, m.ReadU32(struc));
                c.V0 = 1u;
                return;
            }

            cur = next;
        }

        c.V0 = 0u;
    }
}