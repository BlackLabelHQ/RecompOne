using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Bios;

public static class BiosC
{
    public static void Dispatch(CpuContext c, IMemory m, uint fn)
    {
        Log.Bios($"C({fn:X2}) {BiosNames.C(fn)}");
        switch (fn)
        {
            case 0x00: break;
            case 0x01: break;
            case 0x02: BiosB.SysEnqIntRP(c, m); break;
            case 0x03: BiosB.SysDeqIntRP(c, m); break;
            case 0x04: c.V0 = BiosB.GetFreeEvSlot(); break;
            case 0x05: c.V0 = 0u; break;
            case 0x06: break;
            case 0x07: break;
            case 0x08: break;
            case 0x09: break;
            case 0x0A: break;
            case 0x0B: break;
            case 0x0C: break;
            case 0x0D: break;
            case 0x0E: break;
            case 0x0F: break;
            case 0x10: break;
            case 0x11: c.V0 = 0u; break;
            case 0x12: break;
            case 0x13: break;
            case 0x14: break;
            case 0x15: break;
            case 0x16: break;
            case 0x17: break;
            case 0x18: c.V0 = 0u; break;
            case 0x19: c.V0 = 0u; break;
            case 0x1A: break;
            case 0x1B: break;
            case 0x1C: break;
            case 0x1D: c.V0 = 0u; break;
            default: break;
        }
    }
}