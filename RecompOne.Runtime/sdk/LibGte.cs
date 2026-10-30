using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Sdk;

public static class LibGte
{
    public static void RotTransPers(CpuContext c, IMemory m)
    {
        LoadVector(m, c.A0, 0);
        Gte.Rtps(12, false);
        StoreVertex(m, c.A1, 14);
        StoreResult(m, c.A2, Gte.Read(8));
        StoreResult(m, c.A3, Gte.ReadControl(31));
        ReturnDepth(c);
    }

    public static void RotTransPers3(CpuContext c, IMemory m)
    {
        var xy1 = m.ReadU32(c.SP + 16);
        var xy2 = m.ReadU32(c.SP + 20);
        var p = m.ReadU32(c.SP + 24);
        var flag = m.ReadU32(c.SP + 28);
        LoadVector(m, c.A0, 0);
        LoadVector(m, c.A1, 1);
        LoadVector(m, c.A2, 2);
        Gte.Rtpt(12, false);
        StoreVertex(m, c.A3, 12);
        StoreVertex(m, xy1, 13);
        StoreVertex(m, xy2, 14);
        StoreResult(m, p, Gte.Read(8));
        StoreResult(m, flag, Gte.ReadControl(31));
        ReturnDepth(c);
    }

    public static void RotTransPers4(CpuContext c, IMemory m)
    {
        var xy0 = m.ReadU32(c.SP + 16);
        var xy1 = m.ReadU32(c.SP + 20);
        var xy2 = m.ReadU32(c.SP + 24);
        var xy3 = m.ReadU32(c.SP + 28);
        var p = m.ReadU32(c.SP + 32);
        var flag = m.ReadU32(c.SP + 36);
        LoadVector(m, c.A0, 0);
        LoadVector(m, c.A1, 1);
        LoadVector(m, c.A2, 2);
        Gte.Rtpt(12, false);
        StoreVertex(m, xy0, 12);
        StoreVertex(m, xy1, 13);
        StoreVertex(m, xy2, 14);
        var firstFlag = Gte.ReadControl(31);
        LoadVector(m, c.A3, 0);
        Gte.Rtps(12, false);
        StoreVertex(m, xy3, 14);
        StoreResult(m, p, Gte.Read(8));
        StoreResult(m, flag, firstFlag | Gte.ReadControl(31));
        ReturnDepth(c);
    }

    private static void LoadVector(IMemory m, uint address, int index)
    {
        Gte.Write(index * 2, m.ReadU32(address));
        Gte.Write(index * 2 + 1, m.ReadU32(address + 4));
    }

    private static void StoreVertex(IMemory m, uint address, int register)
    {
        var value = Gte.Read(register);
        m.WriteU32(address, value);
    }

    private static void StoreResult(IMemory m, uint address, uint value)
    {
        m.WriteU32(address, value);
    }

    private static void ReturnDepth(CpuContext c)
    {
        c.V0 = Gte.Read(19) >> 2;
    }
}
