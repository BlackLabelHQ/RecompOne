using RecompOne.Runtime.Events;

namespace RecompOne.Runtime;

//old soft raster
public sealed partial class Gpu
{
    private struct Vert
    {
        public int X, Y, R, G, B, U, V;
        public float Px, Py, Pw;
        public bool Precise;
        public bool PreciseW;
    }

    private static readonly RenderPrimEvent _primEvent = new();

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

            var slot = idx;
            var vw = _fifo[idx++];
            v[i].X = _drawOffsetX + CoordX(vw);
            v[i].Y = _drawOffsetY + CoordY(vw);
            v[i].Precise = false;
            v[i].PreciseW = false;

            if (Pgxp.Pgxp.Enabled)
            {
                float px = 0f, py = 0f, pw = 1f;
                var validW = false;
                var found = _fifoBase != 0u &&
                            Pgxp.PgxpMemory.TryLoad(_fifoBase + (uint)slot * 4u, vw, out px, out py, out pw,
                                out validW);

                if (!found) found = Pgxp.PgxpGpu.TryGetVertex(vw, out px, out py, out pw, out validW);

                if (found)
                {
                    v[i].Px = _drawOffsetX + px;
                    v[i].Py = _drawOffsetY + py;
                    v[i].Pw = pw;
                    v[i].Precise = true;
                    v[i].PreciseW = validW;
                }
            }

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

        HleTri(v[0], v[1], v[2], tex, gouraud, semi, raw, clut);
        if (quad) HleTri(v[1], v[2], v[3], tex, gouraud, semi, raw, clut);
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

        HleRect(x, y, w, h, u0, v0, clut, cr, cg, cb, tex, semi, raw);
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
        HleLine(x0, y0, r0, g0, b0, x1, y1, r1, g1, b1, semi, gouraud);
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

}