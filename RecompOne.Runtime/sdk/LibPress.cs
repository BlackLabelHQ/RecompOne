using RecompOne.Runtime.Context;
using RecompOne.Runtime.Dispatch;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Sdk;

public static class LibPress
{
    private static uint _outCb;
    private static bool _inOutCb;
    private static bool _outPending;

    public static void Reset()
    {
        _outCb = 0;
        _inOutCb = false;
        _outPending = false;
    }

    public static void DecDCTin(CpuContext c, IMemory m)
    {
        var mdec = Runtime.Mdec;
        if (mdec == null) return;

        var p = c.A0;
        var mode = c.A1;

        var head = m.ReadU32(p);
        head = (mode & 1u) != 0 ? head & 0xF7FFFFFFu : head | 0x08000000u;
        head = (mode & 2u) != 0 ? head | 0x02000000u : head & 0xFDFFFFFFu;
        m.WriteU32(p, head);

        var words = head & 0xFFFFu;
        mdec.Write0(head);
        for (var i = 0u; i < words; i++) mdec.Write0(m.ReadU32(p + 4u + i * 4u));

        if (m is PSMemory psm) psm.CompleteMdecTransfer(0);

    }

    public static void DecDCTout(CpuContext c, IMemory m)
    {
        var mdec = Runtime.Mdec;
        if (mdec == null)
        {
            c.V0 = 0u;
            return;
        }

        var dst = c.A0;
        var words = (c.A1 >> 5) << 5;
        for (var i = 0u; i < words; i++) m.WriteU32(dst + i * 4u, mdec.ReadData());

        c.V0 = 0u;

        if (m is PSMemory psm) psm.CompleteMdecTransfer(1);

        if (_outCb == 0) return;

        if (_inOutCb)
        {
            _outPending = true;
            return;
        }

        _inOutCb = true;
        var snap = c.Snapshot();
        try
        {
            do
            {
                _outPending = false;
                Dispatcher.Call(c, m, _outCb);
            } while (_outPending && _outCb != 0);
        }
        finally
        {
            c.Restore(snap);
            _inOutCb = false;
            _outPending = false;
        }
    }

    public static void DecDCTinSync(CpuContext c, IMemory m)
    {
        c.V0 = 0u;
    }

    public static void DecDCToutSync(CpuContext c, IMemory m)
    {
        c.V0 = 0u;
    }

    public static void DecDCToutCallback(CpuContext c, IMemory m)
    {
        c.V0 = _outCb;
        _outCb = c.A0;
    }
}
