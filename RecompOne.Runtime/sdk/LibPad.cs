using RecompOne.Runtime.Context;
using RecompOne.Runtime.Hardware;
using RecompOne.Runtime.Host;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Sdk;
public static class LibPad
{
    const byte Connected = 0x00;
    const byte Disconnected = 0xFF;
    const byte DigitalId = 0x41;
    const byte AnalogId = 0x73;
    const uint PadStateDiscon = 0;
    const uint PadStateStable = 6;

    const uint InfoModeCurId = 1;
    const uint InfoModeCurExId = 2;
    const uint InfoModeCurExOffs = 3;
    const uint InfoModeIdTable = 4;

    static uint _buf1;
    static uint _buf2;
    static int _smallMotorIdx = 0;
    static int _largeMotorIdx = 1;
    static bool _analog1 = true;
    static bool _analog2 = true;

    public static void PadInitDirect(CpuContext c, IMemory m)
    {
        _buf1 = c.A0;
        _buf2 = c.A1;
        Log.Sdk($"PadInitDirect buf1=0x{_buf1:X8} buf2=0x{_buf2:X8}");
        c.V0 = 0;
    }

    public static void PadStartCom(CpuContext c, IMemory m) { Refresh(m); c.V0 = 0; }
    public static void PadStopCom(CpuContext c, IMemory m) { c.V0 = 0; }
    public static void PadEnableCom(CpuContext c, IMemory m) { c.V0 = 0; }

    public static void PadChkVsync(CpuContext c, IMemory m) => c.V0 = 1;

    public static void PadChkMtap(CpuContext c, IMemory m) => c.V0 = 0;

    public static void PadGetState(CpuContext c, IMemory m)
        => c.V0 = IsPort1(c.A0) || Controller.Connected2 ? PadStateStable : PadStateDiscon;

    public static void PadInfoMode(CpuContext c, IMemory m)
    {
        bool analog = IsPort1(c.A0) ? _analog1 : _analog2;
        c.V0 = c.A1 switch
        {
            InfoModeCurId => analog ? 7u : 4u,
            InfoModeCurExId => analog ? 7u : 0u,
            InfoModeCurExOffs => 1u,
            InfoModeIdTable when c.A2 == uint.MaxValue => 2u,
            InfoModeIdTable when c.A2 == 0 => 4u,
            InfoModeIdTable when c.A2 == 1 => 7u,
            _ => 0u,
        };
    }
    public static void PadInfoComb(CpuContext c, IMemory m)
    {
        c.V0 = (int)c.A1 switch
        {
            < 0 => 1u,
            0 when (int)c.A2 < 0 => 2u,
            0 when c.A2 < 2 => c.A2,
            _ => 0u,
        };
    }

    public static void PadInfoAct(CpuContext c, IMemory m)
    {
        int actuator = (int)c.A1;
        if (actuator < 0)
        {
            c.V0 = 2;
            return;
        }

        if (actuator > 1)
        {
            c.V0 = 0;
            return;
        }

        c.V0 = c.A2 switch
        {
            1 => 1u,
            2 => (uint)(actuator + 1),
            3 => (uint)actuator,
            4 => actuator == 0 ? 20u : 40u,
            5 => 0u,
            _ => 0u,
        };
    }

    public static void PadSetMainMode(CpuContext c, IMemory m)
    {
        if (IsPort1(c.A0)) _analog1 = c.A1 != 0;
        else _analog2 = c.A1 != 0;
        c.V0 = 1;
    }

    public static void PadSetActAlign(CpuContext c, IMemory m)
    {
        if (!IsPort1(c.A0)) { c.V0 = 1; return; }
        uint ptr = c.A1;
        if (ptr == 0) { c.V0 = 0; return; }
        for (int i = 0; i < 6; i++)
        {
            byte v = m.ReadU8(ptr + (uint)i);
            if (v == 0x00) _smallMotorIdx = i;
            else if (v == 0x01) _largeMotorIdx = i;
        }
        c.V0 = 1;
    }

    public static void PadSetAct(CpuContext c, IMemory m)
    {
        if (!IsPort1(c.A0)) { c.V0 = 1; return; }
        uint ptr = c.A1;
        uint len = c.A2;
        if (ptr == 0 || len == 0) { c.V0 = 0; return; }
        byte small = _smallMotorIdx < (int)len ? m.ReadU8(ptr + (uint)_smallMotorIdx) : (byte)0;
        byte large = _largeMotorIdx < (int)len ? m.ReadU8(ptr + (uint)_largeMotorIdx) : (byte)0;
        InputManager.SetRumble(large, small);
        c.V0 = 1;
    }

    public static void Refresh(IMemory m)
    {
        if (_buf1 != 0) WritePad(m, _buf1, Controller.State, true, _analog1,
            Controller.RightX, Controller.RightY, Controller.LeftX, Controller.LeftY);
        if (_buf2 != 0) WritePad(m, _buf2, Controller.State2, Controller.Connected2, _analog2,
            Controller.RightX2, Controller.RightY2, Controller.LeftX2, Controller.LeftY2);
    }

    static bool IsPort1(uint port) => (port & 0x10u) == 0;

    static void WritePad(IMemory m, uint buf, ushort buttons, bool present, bool analog,
        byte rx, byte ry, byte lx, byte ly)
    {
        m.WriteU8(buf + 0, present ? Connected    : Disconnected);
        m.WriteU8(buf + 1, present ? (analog ? AnalogId : DigitalId) : Disconnected);
        m.WriteU8(buf + 2, (byte)(buttons & 0xFF));
        m.WriteU8(buf + 3, (byte)(buttons >> 8));
        m.WriteU8(buf + 4, present ? rx : (byte)0x80);
        m.WriteU8(buf + 5, present ? ry : (byte)0x80);
        m.WriteU8(buf + 6, present ? lx : (byte)0x80);
        m.WriteU8(buf + 7, present ? ly : (byte)0x80);
    }
}
