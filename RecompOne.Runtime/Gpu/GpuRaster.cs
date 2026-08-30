using RecompOne.Runtime.Events;

namespace RecompOne.Runtime;

//old soft raster
public sealed partial class Gpu
{
    private struct Vert
    {
        public int X, Y, R, G, B, U, V;
    }

    private static readonly RenderPrimEvent _primEvent = new();

    private static readonly int[,] Dither =
    {
        { -4, 0, -3, 1 },
        { 2, -2, 3, -1 },
        { -3, 1, -4, 0 },
        { 3, -1, 2, -2 }
    };

    private void DrawPolygon()
    {
        var cmd = _fifo[0];
        var gouraud = (cmd & (1u << 28)) != 0;
        var quad = (cmd & (1u << 27)) != 0;
        var tex = (cmd & (1u << 26)) != 0;
        var semi = (cmd & (1u << 25)) != 0;
        var raw = (cmd & (1u << 24)) != 0;
        var n = quad ? 4 : 3;

        Span<Vert> v = stackalloc Vert[4];
        var idx = 1;
        var clut = 0;
        int cr = (int)(cmd & 0xFF), cg = (int)((cmd >> 8) & 0xFF), cb = (int)((cmd >> 16) & 0xFF);

        for (var i = 0; i < n; i++)
        {
            if (gouraud && i > 0)
            {
                var cw = _fifo[idx++];
                cr = (int)(cw & 0xFF);
                cg = (int)((cw >> 8) & 0xFF);
                cb = (int)((cw >> 16) & 0xFF);
            }

            v[i].R = cr;
            v[i].G = cg;
            v[i].B = cb;

            var vw = _fifo[idx++];
            v[i].X = _drawOffsetX + CoordX(vw);
            v[i].Y = _drawOffsetY + CoordY(vw);

            if (tex)
            {
                var uvw = _fifo[idx++];
                v[i].U = (int)(uvw & 0xFF);
                v[i].V = (int)((uvw >> 8) & 0xFF);
                if (i == 0) clut = (int)((uvw >> 16) & 0xFFFF);
                else if (i == 1) SetTexpageFromWord((uvw >> 16) & 0xFFFF);
            }
        }

        //dispatch the render event for prims
        if (Event.HasAnyListeners<RenderPrimEvent>())
        {
            var e = _primEvent;
            e.Context = Runtime.Cpu!;
            e.Memory = Runtime.Mem!;
            e.Count = n;
            for (var i = 0; i < n; i++)
            {
                e.X[i] = v[i].X;
                e.Y[i] = v[i].Y;
            }

            e.DrawLeft = _drawAreaLeft;
            e.DrawRight = _drawAreaRight;
            e.DrawTop = _drawAreaTop;
            e.DrawBottom = _drawAreaBottom;
            e.Textured = tex;
            e.SemiTransparent = semi;
            e.Gouraud = gouraud;
            e.Raw = raw;
            e.Clut = clut;
            e.TexPage = 0;
            e.Skip = false;
            Event.Dispatch(e);
            if (e.Skip) return;
            for (var i = 0; i < n; i++)
            {
                v[i].X = e.X[i];
                v[i].Y = e.Y[i];
            }
        }

        if (HleOn)
        {
            HleTri(v[0], v[1], v[2], tex, gouraud, semi, raw, clut);
            if (quad) HleTri(v[1], v[2], v[3], tex, gouraud, semi, raw, clut);
        }
        else
        {
            RasterTriangle(v[0], v[1], v[2], tex, gouraud, semi, raw, clut);
            if (quad) RasterTriangle(v[1], v[2], v[3], tex, gouraud, semi, raw, clut);
        }
    }

    private void RasterTriangle(Vert a, Vert b, Vert c, bool tex, bool gouraud, bool semi, bool raw, int clut)
    {
        var spanX = Math.Max(a.X, Math.Max(b.X, c.X)) - Math.Min(a.X, Math.Min(b.X, c.X));
        var spanY = Math.Max(a.Y, Math.Max(b.Y, c.Y)) - Math.Min(a.Y, Math.Min(b.Y, c.Y));
        if (spanX > 1023 || spanY > 511) return;

        var area = (long)(b.X - a.X) * (c.Y - a.Y) - (long)(b.Y - a.Y) * (c.X - a.X);
        if (area == 0) return;
        if (area < 0)
        {
            (b, c) = (c, b);
            area = -area;
        }

        var minX = Math.Max(_drawAreaLeft, Math.Min(a.X, Math.Min(b.X, c.X)));
        var maxX = Math.Min(_drawAreaRight, Math.Max(a.X, Math.Max(b.X, c.X)));
        var minY = Math.Max(_drawAreaTop, Math.Min(a.Y, Math.Min(b.Y, c.Y)));
        var maxY = Math.Min(_drawAreaBottom, Math.Max(a.Y, Math.Max(b.Y, c.Y)));
        if (minX > maxX || minY > maxY) return;

        var bias0 = IsTopLeft(b, c) ? 0 : -1;
        var bias1 = IsTopLeft(c, a) ? 0 : -1;
        var bias2 = IsTopLeft(a, b) ? 0 : -1;
        var ditherTex = _dither && !raw;

        int sx0 = b.Y - c.Y, sy0 = c.X - b.X;
        int sx1 = c.Y - a.Y, sy1 = a.X - c.X;
        int sx2 = a.Y - b.Y, sy2 = b.X - a.X;

        var w0Row = (long)(c.X - b.X) * (minY - b.Y) - (long)(c.Y - b.Y) * (minX - b.X);
        var w1Row = (long)(a.X - c.X) * (minY - c.Y) - (long)(a.Y - c.Y) * (minX - c.X);
        var w2Row = (long)(b.X - a.X) * (minY - a.Y) - (long)(b.Y - a.Y) * (minX - a.X);

        for (var y = minY; y <= maxY; y++, w0Row += sy0, w1Row += sy1, w2Row += sy2)
        {
            long w0 = w0Row, w1 = w1Row, w2 = w2Row;
            for (var x = minX; x <= maxX; x++, w0 += sx0, w1 += sx1, w2 += sx2)
            {
                if (w0 + bias0 < 0 || w1 + bias1 < 0 || w2 + bias2 < 0) continue;

                int r, g, bl;
                if (gouraud)
                {
                    r = (int)((w0 * a.R + w1 * b.R + w2 * c.R) / area);
                    g = (int)((w0 * a.G + w1 * b.G + w2 * c.G) / area);
                    bl = (int)((w0 * a.B + w1 * b.B + w2 * c.B) / area);
                }
                else
                {
                    r = a.R;
                    g = a.G;
                    bl = a.B;
                }

                if (tex)
                {
                    var u = (int)((w0 * a.U + w1 * b.U + w2 * c.U) / area);
                    var tv = (int)((w0 * a.V + w1 * b.V + w2 * c.V) / area);
                    var texel = FetchTexel(u, tv, clut);
                    if (texel == 0) continue;
                    var stp = (texel & 0x8000) != 0;
                    int tr = (texel & 0x1F) << 3, tg = ((texel >> 5) & 0x1F) << 3, tb = ((texel >> 10) & 0x1F) << 3;
                    if (!raw)
                    {
                        tr = (tr * r) >> 7;
                        tg = (tg * g) >> 7;
                        tb = (tb * bl) >> 7;
                    }

                    Plot(x, y, tr, tg, tb, semi && stp, ditherTex, stp);
                }
                else
                {
                    Plot(x, y, r, g, bl, semi, _dither && gouraud);
                }
            }
        }
    }

    private static bool IsTopLeft(in Vert p0, in Vert p1)
    {
        int dy = p1.Y - p0.Y, dx = p1.X - p0.X;
        return dy < 0 || (dy == 0 && dx > 0);
    }

    private void DrawRectangle()
    {
        var cmd = _fifo[0];
        var sz = (int)((cmd >> 27) & 3);
        var tex = (cmd & (1u << 26)) != 0;
        var semi = (cmd & (1u << 25)) != 0;
        var raw = (cmd & (1u << 24)) != 0;
        int cr = (int)(cmd & 0xFF), cg = (int)((cmd >> 8) & 0xFF), cb = (int)((cmd >> 16) & 0xFF);

        var idx = 1;
        var vw = _fifo[idx++];
        var x = _drawOffsetX + CoordX(vw);
        var y = _drawOffsetY + CoordY(vw);

        int u0 = 0, v0 = 0, clut = 0;
        if (tex)
        {
            var uvw = _fifo[idx++];
            u0 = (int)(uvw & 0xFF);
            v0 = (int)((uvw >> 8) & 0xFF);
            clut = (int)((uvw >> 16) & 0xFFFF);
        }

        int w, h;
        if (sz == 0)
        {
            var wh = _fifo[idx];
            w = (int)(wh & 0xFFFF);
            h = (int)((wh >> 16) & 0xFFFF);
        }
        else
        {
            w = h = sz == 1 ? 1 : sz == 2 ? 8 : 16;
        }

        //dispatch event
        if (Event.HasAnyListeners<RenderPrimEvent>())
        {
            var e = _primEvent;
            e.Context = Runtime.Cpu!;
            e.Memory = Runtime.Mem!;
            e.Count = 2;
            e.X[0] = x;
            e.X[1] = x + w;
            e.Y[0] = y;
            e.Y[1] = y + h;
            e.DrawLeft = _drawAreaLeft;
            e.DrawRight = _drawAreaRight;
            e.DrawTop = _drawAreaTop;
            e.DrawBottom = _drawAreaBottom;
            e.Textured = tex;
            e.SemiTransparent = semi;
            e.Gouraud = false;
            e.Raw = raw;
            e.Clut = clut;
            e.TexPage = 0;
            e.Skip = false;
            Event.Dispatch(e);
            if (e.Skip) return;
            x = e.X[0];
            w = e.X[1] - e.X[0];
        }

        if (HleOn)
        {
            HleRect(x, y, w, h, u0, v0, clut, cr, cg, cb, tex, semi, raw);
            return;
        }

        for (var dy = 0; dy < h; dy++)
        for (var dx = 0; dx < w; dx++)
        {
            int px = x + dx, py = y + dy;
            if (px < _drawAreaLeft || px > _drawAreaRight || py < _drawAreaTop || py > _drawAreaBottom) continue;
            if (tex)
            {
                var texel = FetchTexel((u0 + dx) & 0xFF, (v0 + dy) & 0xFF, clut);
                if (texel == 0) continue;
                var stp = (texel & 0x8000) != 0;
                int tr = (texel & 0x1F) << 3, tg = ((texel >> 5) & 0x1F) << 3, tb = ((texel >> 10) & 0x1F) << 3;
                if (!raw)
                {
                    tr = (tr * cr) >> 7;
                    tg = (tg * cg) >> 7;
                    tb = (tb * cb) >> 7;
                }

                Plot(px, py, tr, tg, tb, semi && stp, false, stp);
            }
            else
            {
                Plot(px, py, cr, cg, cb, semi, false);
            }
        }
    }

    private void DrawLine()
    {
        var cmd = _fifo[0];
        var gouraud = (cmd & (1u << 28)) != 0;
        var semi = (cmd & (1u << 25)) != 0;
        var idx = 1;

        int r0 = (int)(cmd & 0xFF), g0 = (int)((cmd >> 8) & 0xFF), b0 = (int)((cmd >> 16) & 0xFF);
        var v0w = _fifo[idx++];
        int r1 = r0, g1 = g0, b1 = b0;
        if (gouraud)
        {
            var cw = _fifo[idx++];
            r1 = (int)(cw & 0xFF);
            g1 = (int)((cw >> 8) & 0xFF);
            b1 = (int)((cw >> 16) & 0xFF);
        }

        var v1w = _fifo[idx++];

        LineSegment(CoordX(v0w), CoordY(v0w), r0, g0, b0, CoordX(v1w), CoordY(v1w), r1, g1, b1, semi, gouraud);
    }

    private void ExecutePolyline()
    {
        var cmd = _fifo[0];
        var gouraud = (cmd & (1u << 28)) != 0;
        var semi = (cmd & (1u << 25)) != 0;

        var pts = new List<(int X, int Y, int R, int G, int B)>();
        var idx = 1;
        int r = (int)(cmd & 0xFF), g = (int)((cmd >> 8) & 0xFF), b = (int)((cmd >> 16) & 0xFF);
        var first = true;
        while (idx < _fifoCount)
        {
            if (gouraud && !first)
            {
                var cw = _fifo[idx++];
                r = (int)(cw & 0xFF);
                g = (int)((cw >> 8) & 0xFF);
                b = (int)((cw >> 16) & 0xFF);
            }

            if (idx >= _fifoCount) break;
            var vw = _fifo[idx++];
            pts.Add((CoordX(vw), CoordY(vw), r, g, b));
            first = false;
        }

        for (var i = 0; i + 1 < pts.Count; i++)
            LineSegment(pts[i].X, pts[i].Y, pts[i].R, pts[i].G, pts[i].B,
                pts[i + 1].X, pts[i + 1].Y, pts[i + 1].R, pts[i + 1].G, pts[i + 1].B, semi, gouraud);
    }

    private void LineSegment(int x0, int y0, int r0, int g0, int b0, int x1, int y1, int r1, int g1, int b1, bool semi,
        bool gouraud)
    {
        x0 += _drawOffsetX;
        y0 += _drawOffsetY;
        x1 += _drawOffsetX;
        y1 += _drawOffsetY;
        if (HleOn)
        {
            HleLine(x0, y0, r0, g0, b0, x1, y1, r1, g1, b1, semi, gouraud);
            return;
        }

        int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
        var steps = Math.Max(dx, dy);
        if (steps == 0)
        {
            Plot(x0, y0, r0, g0, b0, semi, _dither);
            return;
        }

        for (var i = 0; i <= steps; i++)
        {
            var t = (double)i / steps;
            var x = (int)Math.Round(x0 + (x1 - x0) * t);
            var y = (int)Math.Round(y0 + (y1 - y0) * t);
            var r = (int)(r0 + (r1 - r0) * t);
            var g = (int)(g0 + (g1 - g0) * t);
            var b = (int)(b0 + (b1 - b0) * t);
            if (x < _drawAreaLeft || x > _drawAreaRight || y < _drawAreaTop || y > _drawAreaBottom) continue;
            Plot(x, y, r, g, b, semi, _dither);
        }
    }

    private ushort FetchTexel(int u, int v, int clut)
    {
        u = (u & ~(_texWinMaskX * 8)) | ((_texWinOffX & _texWinMaskX) * 8);
        v = (v & ~(_texWinMaskY * 8)) | ((_texWinOffY & _texWinMaskY) * 8);
        u &= 0xFF;
        v &= 0xFF;

        var row = (_texPageY + v) & (VramHeight - 1);
        if (_texDepth == 2 || _texDepth == 3)
            return Vram[row * VramWidth + ((_texPageX + u) & (VramWidth - 1))];

        var clutX = (clut & 0x3F) * 16;
        var clutY = (clut >> 6) & 0x1FF;
        int index;
        if (_texDepth == 0)
        {
            var block = Vram[row * VramWidth + ((_texPageX + (u >> 2)) & (VramWidth - 1))];
            index = (block >> ((u & 3) * 4)) & 0xF;
        }
        else
        {
            var block = Vram[row * VramWidth + ((_texPageX + (u >> 1)) & (VramWidth - 1))];
            index = (block >> ((u & 1) * 8)) & 0xFF;
        }

        return Vram[(clutY & (VramHeight - 1)) * VramWidth + ((clutX + index) & (VramWidth - 1))];
    }

    private void Plot(int x, int y, int r, int g, int b, bool semi, bool dither, bool maskBit = false)
    {
        if (x < _drawAreaLeft || x > _drawAreaRight || y < _drawAreaTop || y > _drawAreaBottom) return;
        if (x < 0 || x >= VramWidth || y < 0 || y >= VramHeight) return;

        var idx = y * VramWidth + x;
        var bg = Vram[idx];
        if (_checkMask && (bg & 0x8000) != 0) return;

        if (dither)
        {
            var d = Dither[y & 3, x & 3];
            r = Clamp255(r + d);
            g = Clamp255(g + d);
            b = Clamp255(b + d);
        }

        int fr = Math.Min(31, r >> 3), fg = Math.Min(31, g >> 3), fb = Math.Min(31, b >> 3);

        if (semi)
        {
            int br = bg & 0x1F, bgn = (bg >> 5) & 0x1F, bbl = (bg >> 10) & 0x1F;
            switch (_blendMode)
            {
                case 0:
                    fr = (br + fr) >> 1;
                    fg = (bgn + fg) >> 1;
                    fb = (bbl + fb) >> 1;
                    break;
                case 1:
                    fr = Math.Min(31, br + fr);
                    fg = Math.Min(31, bgn + fg);
                    fb = Math.Min(31, bbl + fb);
                    break;
                case 2:
                    fr = Math.Max(0, br - fr);
                    fg = Math.Max(0, bgn - fg);
                    fb = Math.Max(0, bbl - fb);
                    break;
                default:
                    fr = Math.Min(31, br + (fr >> 2));
                    fg = Math.Min(31, bgn + (fg >> 2));
                    fb = Math.Min(31, bbl + (fb >> 2));
                    break;
            }
        }

        var outp = (ushort)(fr | (fg << 5) | (fb << 10));
        if (_setMask || maskBit) outp |= 0x8000;
        Vram[idx] = outp;
    }

    private void SetTexpageFromWord(uint tp)
    {
        _texPageX = (int)(tp & 0xF) * 64;
        _texPageY = (int)((tp >> 4) & 1) * 256;
        _blendMode = (int)((tp >> 5) & 3);
        _texDepth = (int)((tp >> 7) & 3);
        _texDisable = (tp & (1u << 11)) != 0;
    }

    private static int CoordX(uint w)
    {
        var x = (int)(w & 0x7FF);
        return (x & 0x400) != 0 ? x - 0x800 : x;
    }

    private static int CoordY(uint w)
    {
        var y = (int)((w >> 16) & 0x7FF);
        return (y & 0x400) != 0 ? y - 0x800 : y;
    }

    private static ushort To15(int r, int g, int b)
    {
        return (ushort)(((r >> 3) & 0x1F) | (((g >> 3) & 0x1F) << 5) | (((b >> 3) & 0x1F) << 10));
    }

    private static int Clamp255(int v)
    {
        return v < 0 ? 0 : v > 255 ? 255 : v;
    }
}