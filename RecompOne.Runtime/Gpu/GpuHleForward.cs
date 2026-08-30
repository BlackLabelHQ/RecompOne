using RecompOne.Runtime.Hle;

namespace RecompOne.Runtime;

public sealed partial class Gpu
{
    private static bool HleOn => GpuHle.Active && GpuHle.Backend is { Ready: true };

    private int CurTPage()
    {
        return ((_texPageX / 64) & 0xf) | (((_texPageY / 256) & 1) << 4)
                                        | ((_blendMode & 3) << 5) | ((_texDepth & 3) << 7);
    }

    private HleDrawEnv CurEnv()
    {
        return new HleDrawEnv
        {
            ClipX0 = _drawAreaLeft, ClipY0 = _drawAreaTop, ClipX1 = _drawAreaRight, ClipY1 = _drawAreaBottom,
            TwMaskX = _texWinMaskX, TwMaskY = _texWinMaskY, TwOffX = _texWinOffX, TwOffY = _texWinOffY,
            SetMask = _setMask, CheckMask = _checkMask, Dither = _dither
        };
    }

    private static HleVertex HV(in Vert v)
    {
        return new HleVertex
        {
            X = v.X, Y = v.Y, R = (byte)v.R, G = (byte)v.G, B = (byte)v.B, U = (short)v.U, V = (short)v.V
        };
    }

    private PrimFlags PrimOf(bool tex, bool semi, bool raw, int clut, bool gouraud = false)
    {
        return new PrimFlags
        {
            Textured = tex, SemiTrans = semi, RawTexture = raw, Gouraud = gouraud, TPage = (ushort)CurTPage(),
            Clut = (ushort)clut
        };
    }

    private void HleTri(in Vert a, in Vert b, in Vert c, bool tex, bool gouraud, bool semi, bool raw, int clut)
    {
        var spanX = Math.Max(a.X, Math.Max(b.X, c.X)) - Math.Min(a.X, Math.Min(b.X, c.X));
        var spanY = Math.Max(a.Y, Math.Max(b.Y, c.Y)) - Math.Min(a.Y, Math.Min(b.Y, c.Y));
        if (spanX > 1023 || spanY > 511) return;

        var be = GpuHle.Backend!;
        be.SetDrawEnv(CurEnv());
        be.DrawTri(HV(a), HV(b), HV(c), PrimOf(tex, semi, raw, clut, gouraud));
    }

    private void HleRect(int x, int y, int w, int h, int u, int v, int clut, int r, int g, int b, bool tex, bool semi,
        bool raw)
    {
        var be = GpuHle.Backend!;
        be.SetDrawEnv(CurEnv());
        be.DrawRect(
            new HleRect
            {
                X = x, Y = y, W = w, H = h, U = (short)u, V = (short)v, R = (byte)r, G = (byte)g, B = (byte)b
            },
            PrimOf(tex, semi, raw, clut));
    }

    private void HleLine(int x0, int y0, int r0, int g0, int b0, int x1, int y1, int r1, int g1, int b1, bool semi,
        bool gouraud)
    {
        if (Math.Abs(x1 - x0) > 1023 || Math.Abs(y1 - y0) > 511) return;

        var be = GpuHle.Backend!;
        be.SetDrawEnv(CurEnv());
        be.DrawLine(
            new HleVertex { X = x0, Y = y0, R = (byte)r0, G = (byte)g0, B = (byte)b0 },
            new HleVertex { X = x1, Y = y1, R = (byte)r1, G = (byte)g1, B = (byte)b1 },
            PrimOf(false, semi, false, 0, gouraud));
    }

    private void HleFill(int x, int y, int w, int h, ushort color)
    {
        GpuHle.Backend!.FillRect(x, y, w, h, color);
    }

    private void HleCopy(int sx, int sy, int dx, int dy, int w, int h)
    {
        GpuHle.Backend!.CopyVram(sx, sy, dx, dy, w, h);
    }

    private ushort[] _readBuf = Array.Empty<ushort>();

    private void HleReadback(int x, int y, int w, int h)
    {
        var n = w * h;
        if (_readBuf.Length < n) _readBuf = new ushort[n];
        GpuHle.Backend!.ReadVram(x, y, w, h, _readBuf);

        for (var row = 0; row < h; row++)
        {
            var dst = ((y + row) & (VramHeight - 1)) * VramWidth;
            for (var col = 0; col < w; col++)
                Vram[dst + ((x + col) & (VramWidth - 1))] = _readBuf[row * w + col];
        }

        Assets.Textures.VramTracker.MarkCpuWrite(x, y, w, h);
    }

    //img load
    private ushort[] _hleLoad = Array.Empty<ushort>();
    private bool _hleLoadActive;
    private int _hleLoadPos;

    private void HleLoadBegin()
    {
        _hleLoadActive = HleOn;
        if (!_hleLoadActive) return;
        var n = _loadW * _loadH;
        if (_hleLoad.Length < n) _hleLoad = new ushort[n];
        _hleLoadPos = 0;
    }

    private void HleLoadPut(ushort value)
    {
        if (_hleLoadActive && _hleLoadPos < _hleLoad.Length) _hleLoad[_hleLoadPos++] = value;
    }

    private void HleLoadFlush()
    {
        if (!_hleLoadActive) return;
        GpuHle.Backend!.WriteVram(_loadX, _loadY, _loadW, _loadH, _hleLoad.AsSpan(0, _loadW * _loadH));
        _hleLoadActive = false;
    }
}