using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Hle;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Sdk;

public static class LibGpu
{
    private static readonly DrawEnvEvent _drawEnvEvent = new();
    private static readonly DispEnvEvent _dispEnvEvent = new();

    public static void DrawOTag(CpuContext c, IMemory m)
    {
        var gpu = Runtime.Gpu;
        if (gpu == null) return;

        var addr = c.A0 & Runtime.RamWordMask;
        var custom = GpuPrims.Any && GpuPrims.OtLength > 0;
        var otBase = GpuPrims.OtBase & Runtime.RamWordMask;
        var otEnd = otBase + (uint)GpuPrims.OtLength * 4u;

        for (var guard = 0; guard < 0x100000; guard++)
        {
            if (custom && addr >= otBase && addr < otEnd)
                gpu.EmitCustomOrder((int)((addr - otBase) >> 2));

            var header = m.ReadU32(addr);
            var count = (int)(header >> 24);

            if (count > 0)
            {
                if (m is PSMemory ram && ram.TryWords(addr + 4u, count, out var words))
                    gpu.WriteGp0Packet(words, addr + 4u);
                else
                    for (var i = 0; i < count; i++)
                        gpu.WriteGp0(m.ReadU32(addr + 4u + (uint)i * 4u));
            }

            var next = header & 0xFFFFFFu;
            if (next == 0xFFFFFFu || (next & 0x800000u) != 0) break;
            addr = next & Runtime.RamWordMask;
        }

        if (custom) GpuPrims.Clear();
    }

    public static void DrawSync(CpuContext c, IMemory m)
    {
        c.V0 = 0;
    }

    public static void PutDrawEnv(CpuContext c, IMemory m)
    {
        var gpu = Runtime.Gpu;
        if (gpu == null)
        {
            c.V0 = c.A0;
            return;
        }

        var env = c.A0;
        short clipX = S16(m, env + 0x00), clipY = S16(m, env + 0x02);
        short clipW = S16(m, env + 0x04), clipH = S16(m, env + 0x06);
        short ofsX = S16(m, env + 0x08), ofsY = S16(m, env + 0x0A);
        short twX = S16(m, env + 0x0C), twY = S16(m, env + 0x0E);
        short twW = S16(m, env + 0x10), twH = S16(m, env + 0x12);
        var tpage = m.ReadU16(env + 0x14);
        var dtd = m.ReadU8(env + 0x16);
        var dfe = m.ReadU8(env + 0x17);
        var isbg = m.ReadU8(env + 0x18);
        byte r0 = m.ReadU8(env + 0x19), g0 = m.ReadU8(env + 0x1A), b0 = m.ReadU8(env + 0x1B);

        _curCs = GetCs(clipX, clipY);
        _curCe = GetCe((short)(clipX + clipW - 1), (short)(clipY + clipH - 1));
        _curOfs = GetOfs(ofsX, ofsY);
        gpu.WriteGp0(_curCs);
        gpu.WriteGp0(_curCe);
        gpu.WriteGp0(_curOfs);
        gpu.WriteGp0(GetMode(dfe, dtd, tpage));
        gpu.WriteGp0(GetTw(twX, twY, twW, twH));
        gpu.WriteGp0(0xE6000000u);

        if (isbg != 0)
        {
            var w = Math.Clamp((int)clipW, 0, VramShadow.Width - 1);
            var h = Math.Clamp((int)clipH, 0, VramShadow.Height - 1);
            int x = clipX - ofsX, y = clipY - ofsY;
            gpu.WriteGp0(0x60000000u | ((uint)b0 << 16) | ((uint)g0 << 8) | r0);
            gpu.WriteGp0(((uint)(ushort)y << 16) | (ushort)x);
            gpu.WriteGp0(((uint)(ushort)h << 16) | (ushort)w);
        }

        if (Event.HasAnyListeners<DrawEnvEvent>())
        {
            var e = _drawEnvEvent;
            e.Context = c;
            e.Memory = m;
            e.ClipX = clipX;
            e.ClipY = clipY;
            e.ClipW = clipW;
            e.ClipH = clipH;
            e.OfsX = ofsX;
            e.OfsY = ofsY;
            e.IsBackground = isbg != 0;
            Event.Dispatch(e);
        }

        c.V0 = c.A0;
    }

    public static void PutDispEnv(CpuContext c, IMemory m)
    {
        var gpu = Runtime.Gpu;
        if (gpu == null)
        {
            c.V0 = c.A0;
            return;
        }

        var env = c.A0;
        short dispX = S16(m, env + 0x00), dispY = S16(m, env + 0x02);
        short dispW = S16(m, env + 0x04), dispH = S16(m, env + 0x06);
        short scrX = S16(m, env + 0x08), scrY = S16(m, env + 0x0A);
        short scrW = S16(m, env + 0x0C), scrH = S16(m, env + 0x0E);
        var isinter = m.ReadU8(env + 0x10);
        var isrgb24 = m.ReadU8(env + 0x11);
        var pal = gpu.Pal;

        gpu.WriteGp1(0x05000000u | (((uint)dispY & 0x3FF) << 10) | ((uint)dispX & 0x3FF));

        var hStart = scrX * 10 + 0x260;
        var vStart = scrY + (pal ? 0x13 : 0x10);
        var hEnd = hStart + (scrW != 0 ? scrW * 10 : 2560);
        var vEnd = vStart + (scrH != 0 ? scrH : 240);
        hStart = Math.Clamp(hStart, 500, 3290);
        hEnd = Math.Clamp(hEnd, hStart + 0x50, 3290);
        vStart = Math.Clamp(vStart, 0x10, pal ? 310 : 256);
        vEnd = Math.Clamp(vEnd, vStart + 2, pal ? 312 : 258);
        gpu.WriteGp1(0x06000000u | (((uint)hEnd & 0xFFF) << 12) | ((uint)hStart & 0xFFF));
        gpu.WriteGp1(0x07000000u | (((uint)vEnd & 0x3FF) << 10) | ((uint)vStart & 0x3FF));

        var mode = 0x08000000u;
        if (pal) mode |= 0x8;
        if (isrgb24 != 0) mode |= 0x10;
        if (isinter != 0) mode |= 0x20;
        if (dispW <= 280)
        {
        }
        else if (dispW <= 352)
        {
            mode |= 1;
        }
        else if (dispW <= 400)
        {
            mode |= 0x40;
        }
        else if (dispW <= 560)
        {
            mode |= 2;
        }
        else
        {
            mode |= 3;
        }

        if (dispH > (pal ? 288 : 256)) mode |= 0x24;
        gpu.WriteGp1(mode);

        GpuHle.NotifyDisplay(dispX, dispY, dispW, dispH);

        if (Event.HasAnyListeners<DispEnvEvent>())
        {
            var e = _dispEnvEvent;
            e.Context = c;
            e.Memory = m;
            e.X = dispX;
            e.Y = dispY;
            e.W = dispW;
            e.H = dispH;
            Event.Dispatch(e);
        }

        c.V0 = c.A0;
    }

    private static short S16(IMemory m, uint addr)
    {
        return (short)m.ReadU16(addr);
    }

    private static uint GetCs(short x, short y)
    {
        x = short.Clamp(x, 0, VramShadow.Width - 1);
        y = short.Clamp(y, 0, VramShadow.Height - 1);
        return 0xE3000000u | (((uint)y & 0x3FF) << 10) | ((uint)x & 0x3FF);
    }

    private static uint GetCe(short x, short y)
    {
        x = short.Clamp(x, 0, VramShadow.Width - 1);
        y = short.Clamp(y, 0, VramShadow.Height - 1);
        return 0xE4000000u | (((uint)y & 0x3FF) << 10) | ((uint)x & 0x3FF);
    }

    private static uint _curCs = 0xE3000000u, _curCe = 0xE4000000u, _curOfs = 0xE5000000u;

    private static (short X, short Y, short W, short H) ReadRect(IMemory m, uint p)
    {
        return (S16(m, p), S16(m, p + 2), S16(m, p + 4), S16(m, p + 6));
    }


    private static short Clamp(short v, int max)
    {
        return (short)Math.Clamp((int)v, 0, max);
    }

    private const int VramW = 1024;
    private const int VramH = 512;

    private static uint Pack(short lo, short hi)
    {
        return ((uint)(ushort)hi << 16) | (ushort)lo;
    }

    public static void LoadImage(CpuContext c, IMemory m)
    {
        var gpu = Runtime.Gpu;
        if (gpu == null)
        {
            c.V0 = 0xFFFFFFFFu;
            return;
        }

        var r = ReadRect(m, c.A0);
        var src = c.A1;
        short w = Clamp(r.W, VramW), h = Clamp(r.H, VramH);
        var words = (w * h + 1) / 2;
        if (words <= 0)
        {
            c.V0 = 0xFFFFFFFFu;
            return;
        }

        gpu.WriteGp0(0x01000000u);
        gpu.WriteGp0(0xA0000000u);
        gpu.WriteGp0(Pack(r.X, r.Y));
        gpu.WriteGp0(Pack(w, h));
        for (var i = 0; i < words; i++)
            gpu.WriteGp0(m.ReadU32(src + (uint)i * 4u));

        c.V0 = 0u;
    }

    public static void StoreImage(CpuContext c, IMemory m)
    {
        var gpu = Runtime.Gpu;
        if (gpu == null)
        {
            c.V0 = 0xFFFFFFFFu;
            return;
        }

        var r = ReadRect(m, c.A0);
        var dst = c.A1;
        short w = Clamp(r.W, VramW), h = Clamp(r.H, VramH);
        var words = (w * h + 1) / 2;
        if (words <= 0)
        {
            c.V0 = 0xFFFFFFFFu;
            return;
        }

        gpu.WriteGp0(0x01000000u);
        gpu.WriteGp0(0xC0000000u);
        gpu.WriteGp0(Pack(r.X, r.Y));
        gpu.WriteGp0(Pack(w, h));
        for (var i = 0; i < words; i++)
            m.WriteU32(dst + (uint)i * 4u, gpu.ReadData());

        c.V0 = 0u;
    }

    public static void MoveImage(CpuContext c, IMemory m)
    {
        var gpu = Runtime.Gpu;
        if (gpu == null)
        {
            c.V0 = 0xFFFFFFFFu;
            return;
        }

        var r = ReadRect(m, c.A0);
        if (r.W == 0 || r.H == 0)
        {
            c.V0 = 0xFFFFFFFFu;
            return;
        }

        gpu.WriteGp0(0x80000000u);
        gpu.WriteGp0(Pack(r.X, r.Y));
        gpu.WriteGp0(Pack((short)c.A1, (short)c.A2));
        gpu.WriteGp0(Pack(r.W, r.H));

        c.V0 = 0u;
    }

    public static void ClearImage(CpuContext c, IMemory m)
    {
        var gpu = Runtime.Gpu;
        if (gpu == null)
        {
            c.V0 = 0xFFFFFFFFu;
            return;
        }

        var r = ReadRect(m, c.A0);
        short w = Clamp(r.W, VramW - 1), h = Clamp(r.H, VramH - 1);
        var color = ((c.A3 & 0xFFu) << 16) | ((c.A2 & 0xFFu) << 8) | (c.A1 & 0xFFu);

        if ((r.X & 0x3F) != 0 || (w & 0x3F) != 0)
        {
            gpu.WriteGp0(0xE3000000u);
            gpu.WriteGp0(0xE4FFFFFFu);
            gpu.WriteGp0(0xE5000000u);
            gpu.WriteGp0(0xE6000000u);
            gpu.WriteGp0(0x60000000u | color);
            gpu.WriteGp0(Pack(r.X, r.Y));
            gpu.WriteGp0(Pack(w, h));
            gpu.WriteGp0(_curCs);
            gpu.WriteGp0(_curCe);
            gpu.WriteGp0(_curOfs);
        }
        else
        {
            gpu.WriteGp0(0xE6000000u);
            gpu.WriteGp0(0x02000000u | color);
            gpu.WriteGp0(Pack(r.X, r.Y));
            gpu.WriteGp0(Pack(w, h));
        }

        c.V0 = 0u;
    }

    private static uint GetOfs(short x, short y)
    {
        return 0xE5000000u | (((uint)y & 0x7FF) << 11) | ((uint)x & 0x7FF);
    }

    private static uint GetMode(int dfe, int dtd, ushort tpage)
    {
        return (dtd != 0 ? 0xE1000200u : 0xE1000000u) | (dfe != 0 ? 0x400u : 0u) | ((uint)tpage & 0x9FF);
    }

    private static uint GetTw(short x, short y, short w, short h)
    {
        var c0 = ((uint)x & 0xFF) >> 3;
        var c1 = ((uint)y & 0xFF) >> 3;
        var c2 = ((uint)-w & 0xFF) >> 3;
        var c3 = ((uint)-h & 0xFF) >> 3;
        return 0xE2000000u | (c1 << 15) | (c0 << 10) | (c3 << 5) | c2;
    }
}