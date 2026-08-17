using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Hle;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Sdk;

public static class LibGpu
{
    static readonly DrawEnvEvent _drawEnvEvent = new();
    static readonly DispEnvEvent _dispEnvEvent = new();

    public static void DrawOTag(CpuContext c, IMemory m)
    {
        var gpu = Runtime.Gpu;
        if (gpu == null) return;

        uint addr = c.A0 & Runtime.RamWordMask;
        bool custom = GpuPrims.Any && GpuPrims.OtLength > 0;
        uint otBase = GpuPrims.OtBase & Runtime.RamWordMask;
        uint otEnd = otBase + (uint)GpuPrims.OtLength * 4u;

        for (int guard = 0; guard < 0x100000; guard++)
        {
            if (custom && addr >= otBase && addr < otEnd)
                gpu.EmitCustomOrder((int)((addr - otBase) >> 2));

            uint header = m.ReadU32(addr);
            uint count = header >> 24;
            for (uint i = 0; i < count; i++)
                gpu.WriteGp0(m.ReadU32(addr + 4u + i * 4u));
            uint next = header & 0xFFFFFFu;
            if (next == 0xFFFFFFu || (next & 0x800000u) != 0) break;
            addr = next & Runtime.RamWordMask;
        }

        if (custom) GpuPrims.Clear();
    }

    public static void DrawSync(CpuContext c, IMemory m) => c.V0 = 0;

    public static void PutDrawEnv(CpuContext c, IMemory m)
    {
        var gpu = Runtime.Gpu;
        if (gpu == null) { c.V0 = c.A0; return; }

        uint env = c.A0;
        short clipX = S16(m, env + 0x00), clipY = S16(m, env + 0x02);
        short clipW = S16(m, env + 0x04), clipH = S16(m, env + 0x06);
        short ofsX = S16(m, env + 0x08), ofsY = S16(m, env + 0x0A);
        short twX = S16(m, env + 0x0C), twY = S16(m, env + 0x0E);
        short twW = S16(m, env + 0x10), twH = S16(m, env + 0x12);
        ushort tpage = m.ReadU16(env + 0x14);
        byte dtd = m.ReadU8(env + 0x16);
        byte dfe = m.ReadU8(env + 0x17);
        byte isbg = m.ReadU8(env + 0x18);
        byte r0 = m.ReadU8(env + 0x19), g0 = m.ReadU8(env + 0x1A), b0 = m.ReadU8(env + 0x1B);

        gpu.WriteGp0(GetCs(clipX, clipY));
        gpu.WriteGp0(GetCe((short)(clipX + clipW - 1), (short)(clipY + clipH - 1)));
        gpu.WriteGp0(GetOfs(ofsX, ofsY));
        gpu.WriteGp0(GetMode(dfe, dtd, tpage));
        gpu.WriteGp0(GetTw(twX, twY, twW, twH));
        gpu.WriteGp0(0xE6000000u);

        if (isbg != 0)
        {
            int margin = GpuHle.WideMargin(clipW);
            int w = Math.Clamp(clipW + margin * 2, 0, VramShadow.Width - 1);
            int h = Math.Clamp((int)clipH, 0, VramShadow.Height - 1);
            int x = clipX - margin - ofsX, y = clipY - ofsY;
            gpu.WriteGp0(0x60000000u | ((uint)b0 << 16) | ((uint)g0 << 8) | r0);
            gpu.WriteGp0(((uint)(ushort)y << 16) | (ushort)x);
            gpu.WriteGp0(((uint)(ushort)h << 16) | (ushort)w);
        }

        if (Event.HasAnyListeners<DrawEnvEvent>())
        {
            var e = _drawEnvEvent;
            e.Context = c; e.Memory = m;
            e.ClipX = clipX; e.ClipY = clipY; e.ClipW = clipW; e.ClipH = clipH;
            e.OfsX = ofsX; e.OfsY = ofsY; e.IsBackground = isbg != 0;
            Event.Dispatch(e);
        }

        c.V0 = c.A0;
    }

    public static void PutDispEnv(CpuContext c, IMemory m)
    {
        var gpu = Runtime.Gpu;
        if (gpu == null) { c.V0 = c.A0; return; }

        uint env = c.A0;
        short dispX = S16(m, env + 0x00), dispY = S16(m, env + 0x02);
        short dispW = S16(m, env + 0x04), dispH = S16(m, env + 0x06);
        short scrX = S16(m, env + 0x08), scrY = S16(m, env + 0x0A);
        short scrW = S16(m, env + 0x0C), scrH = S16(m, env + 0x0E);
        byte isinter = m.ReadU8(env + 0x10);
        byte isrgb24 = m.ReadU8(env + 0x11);
        bool pal = gpu.Pal;

        gpu.WriteGp1(0x05000000u | (((uint)dispY & 0x3FF) << 10) | ((uint)dispX & 0x3FF));

        int hStart = scrX * 10 + 0x260;
        int vStart = scrY + (pal ? 0x13 : 0x10);
        int hEnd = hStart + (scrW != 0 ? scrW * 10 : 2560);
        int vEnd = vStart + (scrH != 0 ? scrH : 240);
        hStart = Math.Clamp(hStart, 500, 3290);
        hEnd = Math.Clamp(hEnd, hStart + 0x50, 3290);
        vStart = Math.Clamp(vStart, 0x10, pal ? 310 : 256);
        vEnd = Math.Clamp(vEnd, vStart + 2, pal ? 312 : 258);
        gpu.WriteGp1(0x06000000u | (((uint)hEnd & 0xFFF) << 12) | ((uint)hStart & 0xFFF));
        gpu.WriteGp1(0x07000000u | (((uint)vEnd & 0x3FF) << 10) | ((uint)vStart & 0x3FF));

        uint mode = 0x08000000u;
        if (pal) mode |= 0x8;
        if (isrgb24 != 0) mode |= 0x10;
        if (isinter != 0) mode |= 0x20;
        if (dispW <= 280) { }
        else if (dispW <= 352) mode |= 1;
        else if (dispW <= 400) mode |= 0x40;
        else if (dispW <= 560) mode |= 2;
        else mode |= 3;
        if (dispH > (pal ? 288 : 256)) mode |= 0x24;
        gpu.WriteGp1(mode);

        GpuHle.NotifyDisplay(dispX, dispY, dispW, dispH);

        if (Event.HasAnyListeners<DispEnvEvent>())
        {
            var e = _dispEnvEvent;
            e.Context = c; e.Memory = m;
            e.X = dispX; e.Y = dispY; e.W = dispW; e.H = dispH;
            Event.Dispatch(e);
        }

        c.V0 = c.A0;
    }

    static short S16(IMemory m, uint addr) => (short)m.ReadU16(addr);

    static uint GetCs(short x, short y)
    {
        x = short.Clamp(x, 0, VramShadow.Width - 1);
        y = short.Clamp(y, 0, VramShadow.Height - 1);
        return 0xE3000000u | (((uint)y & 0x3FF) << 10) | ((uint)x & 0x3FF);
    }

    static uint GetCe(short x, short y)
    {
        x = short.Clamp(x, 0, VramShadow.Width - 1);
        y = short.Clamp(y, 0, VramShadow.Height - 1);
        return 0xE4000000u | (((uint)y & 0x3FF) << 10) | ((uint)x & 0x3FF);
    }

    public static void LoadImage(CpuContext c, IMemory m)
    {
        var gpu = Runtime.Gpu;
        if (gpu == null) { c.V0 = 0u; return; }

        uint rect = c.A0, src = c.A1;
        ushort x = m.ReadU16(rect), y = m.ReadU16(rect + 2);
        ushort w = m.ReadU16(rect + 4), h = m.ReadU16(rect + 6);
        if (w == 0 || h == 0) { c.V0 = 0u; return; }

        gpu.WriteGp0(0xA0000000u);
        gpu.WriteGp0(((uint)y << 16) | x);
        gpu.WriteGp0(((uint)h << 16) | w);

        uint words = ((uint)w * h + 1u) >> 1;
        for (uint i = 0; i < words; i++)
            gpu.WriteGp0(m.ReadU32(src + i * 4u));

        c.V0 = 0u;
    }

    public static void StoreImage(CpuContext c, IMemory m)
    {
        var gpu = Runtime.Gpu;
        if (gpu == null) { c.V0 = 0u; return; }

        uint rect = c.A0, dst = c.A1;
        ushort x = m.ReadU16(rect), y = m.ReadU16(rect + 2);
        ushort w = m.ReadU16(rect + 4), h = m.ReadU16(rect + 6);
        if (w == 0 || h == 0) { c.V0 = 0u; return; }

        gpu.WriteGp0(0xC0000000u);
        gpu.WriteGp0(((uint)y << 16) | x);
        gpu.WriteGp0(((uint)h << 16) | w);

        uint words = ((uint)w * h + 1u) >> 1;
        for (uint i = 0; i < words; i++)
            m.WriteU32(dst + i * 4u, gpu.ReadData());

        c.V0 = 0u;
    }

    public static void MoveImage(CpuContext c, IMemory m)
    {
        var gpu = Runtime.Gpu;
        if (gpu == null) { c.V0 = 0u; return; }

        uint rect = c.A0;
        ushort sx = m.ReadU16(rect), sy = m.ReadU16(rect + 2);
        ushort w = m.ReadU16(rect + 4), h = m.ReadU16(rect + 6);
        if (w == 0 || h == 0) { c.V0 = 0u; return; }

        gpu.WriteGp0(0x80000000u);
        gpu.WriteGp0(((uint)sy << 16) | sx);
        gpu.WriteGp0(((c.A3 & 0xFFFFu) << 16) | (c.A2 & 0xFFFFu));
        gpu.WriteGp0(((uint)h << 16) | w);

        c.V0 = 0u;
    }

    public static void ClearImage(CpuContext c, IMemory m)
    {
        var gpu = Runtime.Gpu;
        if (gpu == null) { c.V0 = 0u; return; }

        uint rect = c.A0;
        ushort x = m.ReadU16(rect), y = m.ReadU16(rect + 2);
        ushort w = m.ReadU16(rect + 4), h = m.ReadU16(rect + 6);
        if (w == 0 || h == 0) { c.V0 = 0u; return; }

        gpu.WriteGp0(0x02000000u | ((c.A3 & 0xFFu) << 16) | ((c.A2 & 0xFFu) << 8) | (c.A1 & 0xFFu));
        gpu.WriteGp0(((uint)y << 16) | x);
        gpu.WriteGp0(((uint)h << 16) | w);

        c.V0 = 0u;
    }

    static uint GetOfs(short x, short y)
        => 0xE5000000u | (((uint)y & 0x7FF) << 11) | ((uint)x & 0x7FF);

    static uint GetMode(int dfe, int dtd, ushort tpage)
        => (dtd != 0 ? 0xE1000200u : 0xE1000000u) | (dfe != 0 ? 0x400u : 0u) | ((uint)tpage & 0x9FF);

    static uint GetTw(short x, short y, short w, short h)
    {
        uint c0 = ((uint)x & 0xFF) >> 3;
        uint c1 = ((uint)y & 0xFF) >> 3;
        uint c2 = ((uint)(-w) & 0xFF) >> 3;
        uint c3 = ((uint)(-h) & 0xFF) >> 3;
        return 0xE2000000u | (c1 << 15) | (c0 << 10) | (c3 << 5) | c2;
    }
}
