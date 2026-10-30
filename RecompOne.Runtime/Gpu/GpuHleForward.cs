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

    private static HleVertex HV(in Vert v, bool invalidW)
    {
        var x = v.Precise ? v.Px : v.X;
        var y = v.Precise ? v.Py : v.Y;
        
        return new HleVertex
        {
            X = x,
            Y = y,
            Z = invalidW ? 1f : v.Pw,
            HasGteZ = !invalidW,
            Depth = v.Precise && v.PreciseW && v.Transform > 0 && float.IsFinite(v.Pw) && v.Pw > 0f ? v.Pw : 0f,
            R = (byte)v.R, G = (byte)v.G, B = (byte)v.B, U = (short)v.U, V = (short)v.V,
            Camera = v.Camera,
            Native = v.Native,
            IgnoreDepth = v.IgnoreDepth,
            Prim = v.Prim,
            Transform = v.Transform
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

    private void HleTri(in Vert a, in Vert b, in Vert c, bool invalidW, bool tex, bool gouraud, bool semi, bool raw, int clut)
    {
        var spanX = Math.Max(a.X, Math.Max(b.X, c.X)) - Math.Min(a.X, Math.Min(b.X, c.X));
        var spanY = Math.Max(a.Y, Math.Max(b.Y, c.Y)) - Math.Min(a.Y, Math.Min(b.Y, c.Y));
        if (!(a.Native && b.Native && c.Native) && (spanX > 1023 || spanY > 511)) return;
        
        var be = GpuHle.Backend!;
        be.SetDrawEnv(CurEnv());
        be.DrawTri(HV(a, invalidW), HV(b, invalidW), HV(c, invalidW), PrimOf(tex, semi, raw, clut, gouraud));
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

    private void HleLine(in Vert a, in Vert b, bool semi, bool gouraud)
    {
        if (Math.Abs(b.X - a.X) > 1023 || Math.Abs(b.Y - a.Y) > 511) return;

        var be = GpuHle.Backend!;
        be.SetDrawEnv(CurEnv());
        var invalidW = !a.PreciseW || !b.PreciseW;
        be.DrawLine(HV(a, invalidW), HV(b, invalidW), PrimOf(false, semi, false, 0, gouraud));
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
        var buf = _readBuf;

        if (Host.GpuJobs.Claimed && !Host.GpuJobs.IsOwner)
        {
            Host.GpuJobs.Run(() => GpuHle.Backend!.ReadVram(x, y, w, h, buf));
        }
        else
        {
            GpuHle.Backend!.ReadVram(x, y, w, h, buf);
        }

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