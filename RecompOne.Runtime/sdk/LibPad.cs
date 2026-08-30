using RecompOne.Runtime.Context;
using RecompOne.Runtime.Hardware;
using RecompOne.Runtime.Host;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Sdk;

public static class LibPad
{
    private const byte Connected = 0x00;
    private const byte Disconnected = 0xFF;
    private const byte DigitalId = 0x41;
    private const uint PadStateDiscon = 0;
    private const uint PadStateStable = 6;

    private static uint _buf1;
    private static uint _buf2;
    private static int _smallMotorIdx = 0;
    private static int _largeMotorIdx = 1;

    internal static void Reset()
    {
        _buf1 = 0;
        _buf2 = 0;
    }

    public static void PadInitDirect(CpuContext c, IMemory m)
    {
        _buf1 = c.A0;
        _buf2 = c.A1;
        Log.Sdk($"PadInitDirect buf1=0x{_buf1:X8} buf2=0x{_buf2:X8}");
        c.V0 = 0;
    }

    public static void PadStartCom(CpuContext c, IMemory m)
    {
        Refresh(m);
        c.V0 = 0;
    }

    public static void PadStopCom(CpuContext c, IMemory m)
    {
        c.V0 = 0;
    }

    public static void PadEnableCom(CpuContext c, IMemory m)
    {
        c.V0 = 0;
    }

    public static void PadChkVsync(CpuContext c, IMemory m)
    {
        c.V0 = 1;
    }

    public static void PadChkMtap(CpuContext c, IMemory m)
    {
        c.V0 = 0;
    }

    public static void PadGetState(CpuContext c, IMemory m)
    {
        c.V0 = IsPort1(c.A0) || Controller.Connected2 ? PadStateStable : PadStateDiscon;
    }

    public static void PadInfoMode(CpuContext c, IMemory m)
    {
        c.V0 = 0;
    }

    public static void PadInfoComb(CpuContext c, IMemory m)
    {
        c.V0 = 0;
    }

    public static void PadInfoAct(CpuContext c, IMemory m)
    {
        c.V0 = (int)c.A2 < 0 ? 2u : 1u;
    }

    public static void PadSetMainMode(CpuContext c, IMemory m)
    {
        c.V0 = 0;
    }

    public static void PadSetActAlign(CpuContext c, IMemory m)
    {
        if (!IsPort1(c.A0))
        {
            c.V0 = 1;
            return;
        }

        var ptr = c.A1;
        var len = c.A2;
        if (ptr == 0 || len < 2)
        {
            c.V0 = 0;
            return;
        }

        for (var i = 0; i < (int)len && i < 6; i++)
        {
            var v = m.ReadU8(ptr + (uint)i);
            if (v == 0x00) _smallMotorIdx = i;
            else if (v == 0x01) _largeMotorIdx = i;
        }

        c.V0 = 1;
    }

    public static void PadSetAct(CpuContext c, IMemory m)
    {
        if (!IsPort1(c.A0))
        {
            c.V0 = 1;
            return;
        }

        var ptr = c.A1;
        var len = c.A2;
        if (ptr == 0 || len == 0)
        {
            c.V0 = 0;
            return;
        }

        var small = _smallMotorIdx < (int)len ? m.ReadU8(ptr + (uint)_smallMotorIdx) : (byte)0;
        var large = _largeMotorIdx < (int)len ? m.ReadU8(ptr + (uint)_largeMotorIdx) : (byte)0;
        InputManager.SetRumble(large, small);
        c.V0 = 1;
    }

    public static void Refresh(IMemory m)
    {
        if (_buf1 != 0)
            WritePad(m, _buf1, Controller.State, true,
                Controller.RightX, Controller.RightY, Controller.LeftX, Controller.LeftY);
        if (_buf2 != 0)
            WritePad(m, _buf2, Controller.State2, Controller.Connected2,
                Controller.RightX2, Controller.RightY2, Controller.LeftX2, Controller.LeftY2);
    }

    private static bool IsPort1(uint port)
    {
        return (port & 0x10u) == 0;
    }

    private static void WritePad(IMemory m, uint buf, ushort buttons, bool present, byte rx, byte ry, byte lx, byte ly)
    {
        m.WriteU8(buf + 0, present ? Connected : Disconnected);
        m.WriteU8(buf + 1, present ? DigitalId : Disconnected);
        m.WriteU8(buf + 2, (byte)(buttons & 0xFF));
        m.WriteU8(buf + 3, (byte)(buttons >> 8));
        m.WriteU8(buf + 4, present ? rx : (byte)0x80);
        m.WriteU8(buf + 5, present ? ry : (byte)0x80);
        m.WriteU8(buf + 6, present ? lx : (byte)0x80);
        m.WriteU8(buf + 7, present ? ly : (byte)0x80);
    }
}