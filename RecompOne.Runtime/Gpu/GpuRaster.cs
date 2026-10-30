using System.Numerics;
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
        public bool Native;
        public bool IgnoreDepth;
        public uint Prim;
        public Vector3 Camera; //camera space, for interp
        public int Transform;
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
        Span<uint> words = stackalloc uint[4];
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
            words[i] = vw;
            v[i].X = _drawOffsetX + CoordX(vw);
            v[i].Y = _drawOffsetY + CoordY(vw);
            v[i].Precise = false;
            v[i].PreciseW = false;
            v[i].Native = false;
            v[i].IgnoreDepth = false;
            v[i].Prim = 0;
            
            if (_fifoBase != 0u && Hle.NativeGeometry.TryTake(_fifoBase + (uint)slot * 4u, vw, out var native))
            {
                v[i].Px = _drawOffsetX + native.Screen.X;
                v[i].Py = _drawOffsetY + native.Screen.Y;
                v[i].Pw = native.Camera.Z;
                v[i].Camera = native.Camera;
                v[i].Transform = native.Transform;
                v[i].Native = v[i].Precise = v[i].PreciseW = true;
                v[i].IgnoreDepth = native.IgnoreDepth;
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

        if (quad && Hle.NativeGeometry.HasAnchors && AnchorBillboard(v, words) && tex && semi && _blendMode == 1)
            EmitLight(v, clut, raw);

        if (_fifoBase != 0u && Hle.PrimIdentity.Any && !v[0].Native && Hle.PrimIdentity.TryResolve(_fifoBase, out var prim))
            for (var i = 0; i < n; i++)
                v[i].Prim = prim;

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
                var dx = e.X[i] - v[i].X;
                var dy = e.Y[i] - v[i].Y;
                v[i].X = e.X[i];
                v[i].Y = e.Y[i];
                if (v[i].Precise)
                {
                    v[i].Px += dx;
                    v[i].Py += dy;
                }
            }
        }

        HleTri(v[0], v[1], v[2], HasInvalidW(v[0], v[1], v[2]), tex, gouraud, semi, raw, clut);
        if (quad) HleTri(v[1], v[2], v[3], HasInvalidW(v[1], v[2], v[3]), tex, gouraud, semi, raw, clut);
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
        var a = LineVert(idx++, r0, g0, b0);
        int r1 = r0, g1 = g0, b1 = b0;
        if (gouraud)
        {
            var cw = _fifo[idx++];
            r1 = (int)(cw & 0xFF);
            g1 = (int)((cw >> 8) & 0xFF);
            b1 = (int)((cw >> 16) & 0xFF);
        }

        var b = LineVert(idx, r1, g1, b1);
        HleLine(in a, in b, semi, gouraud);
    }

    private void ExecutePolyline()
    {
        var cmd = _fifo[0];
        var gouraud = (cmd & (1u << 28)) != 0;
        var semi = (cmd & (1u << 25)) != 0;

        var pts = new List<Vert>();
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
            pts.Add(LineVert(idx++, r, g, b));
            first = false;
        }

        for (var i = 0; i + 1 < pts.Count; i++)
        {
            var a = pts[i];
            var c = pts[i + 1];
            HleLine(in a, in c, semi, gouraud);
        }
    }

    private Vert LineVert(int slot, int r, int g, int b)
    {
        var vw = _fifo[slot];
        var v = new Vert
        {
            X = _drawOffsetX + CoordX(vw),
            Y = _drawOffsetY + CoordY(vw),
            R = r, G = g, B = b
        };

        if (_fifoBase != 0u && Hle.NativeGeometry.TryTake(_fifoBase + (uint)slot * 4u, vw, out var native))
        {
            v.Px = _drawOffsetX + native.Screen.X;
            v.Py = _drawOffsetY + native.Screen.Y;
            v.Pw = native.Camera.Z;
            v.Camera = native.Camera;
            v.Transform = native.Transform;
            v.Native = v.Precise = v.PreciseW = true;
            v.IgnoreDepth = native.IgnoreDepth;
        }

        return v;
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

    private static bool AnchorBillboard(Span<Vert> v, ReadOnlySpan<uint> words)
    {
        for (var i = 0; i < 4; i++)
            if (v[i].Precise) return false;

        int x0 = CoordX(words[0]), x1 = CoordX(words[1]), y0 = CoordY(words[0]), y2 = CoordY(words[2]);
        if (CoordX(words[2]) != x0 || CoordX(words[3]) != x1 || CoordY(words[1]) != y0 || CoordY(words[3]) != y2) return false;
        if (((x0 + x1) & 1) != 0 || ((y0 + y2) & 1) != 0) return false;

        var cx = (x0 + x1) >> 1;
        var cy = (y0 + y2) >> 1;
        if (!Hle.NativeGeometry.TryAnchor(cx, cy, out var anchor)) return false;

        var dx = anchor.Screen.X - cx;
        var dy = anchor.Screen.Y - cy;
        if (MathF.Abs(dx) > 1f || MathF.Abs(dy) > 1f) return false;

        for (var i = 0; i < 4; i++)
        {
            v[i].Px = v[i].X + dx;
            v[i].Py = v[i].Y + dy;
            v[i].Pw = anchor.Camera.Z;
            v[i].Precise = v[i].PreciseW = true;
            v[i].Transform = anchor.Transform;
            v[i].IgnoreDepth = anchor.IgnoreDepth;
            v[i].Camera = anchor.Camera;
        }

        return true;
    }

    private const int LightSamples = 6;
    private const float LightMinimum = 0.3f;
    private const float LightReach = 8f;
    private readonly Dictionary<ulong, Vector3> _textureAverages = new();

    private void EmitLight(ReadOnlySpan<Vert> v, int clut, bool raw)
    {
        var h = (short)Gte.ReadControl(26);
        if (h <= 0 || v[0].Pw <= 0f) return;

        int u0 = v[0].U, v0 = v[0].V, u1 = v[0].U, v1 = v[0].V;
        for (var i = 1; i < 4; i++)
        {
            u0 = Math.Min(u0, v[i].U);
            v0 = Math.Min(v0, v[i].V);
            u1 = Math.Max(u1, v[i].U);
            v1 = Math.Max(v1, v[i].V);
        }

        var texture = TextureAverage(u0, v0, u1, v1, clut);
        var tint = raw ? Vector3.One : new Vector3(v[0].R, v[0].G, v[0].B) / 128f;
        var color = texture * tint;
        if (Math.Max(color.X, Math.Max(color.Y, color.Z)) < LightMinimum) return;

        var half = Math.Max(Math.Abs(v[1].X - v[0].X), Math.Abs(v[2].Y - v[0].Y)) * 0.5f;
        var radius = half * v[0].Pw / h * LightReach;
        Hle.NativeGeometry.Light(new Hle.SceneLight(v[0].Camera, color, radius));
    }

    private Vector3 TextureAverage(int u0, int v0, int u1, int v1, int clut)
    {
        var page = CurTPage() & 0x19F;
        var key = (ulong)(uint)page | (ulong)(uint)clut << 16 | (ulong)(byte)u0 << 32 | (ulong)(byte)v0 << 40 |
                  (ulong)(byte)u1 << 48 | (ulong)(byte)v1 << 56;
        if (_textureAverages.TryGetValue(key, out var cached)) return cached;
        if (_textureAverages.Count > 4096) _textureAverages.Clear();

        var sum = Vector3.Zero;
        var count = 0;
        for (var y = 0; y < LightSamples; y++)
        for (var x = 0; x < LightSamples; x++)
        {
            var u = u0 + (u1 - u0) * (x + 0.5f) / LightSamples;
            var t = v0 + (v1 - v0) * (y + 0.5f) / LightSamples;
            var texel = Texel((int)u & 0xFF, (int)t & 0xFF, clut);
            if (texel == 0) continue;
            sum += new Vector3(texel & 0x1F, (texel >> 5) & 0x1F, (texel >> 10) & 0x1F) / 31f;
            count++;
        }

        var average = count > 0 ? sum / (LightSamples * LightSamples) : Vector3.Zero;
        _textureAverages[key] = average;
        return average;
    }

    private ushort Texel(int u, int v, int clut)
    {
        var x = _texPageX;
        var y = _texPageY + v;
        switch (_texDepth)
        {
            case 0:
            {
                var word = Shadow[(x + (u >> 2)) & 1023, y & 511];
                var index = (word >> ((u & 3) << 2)) & 0xF;
                return Shadow[((clut & 0x3F) * 16 + index) & 1023, (clut >> 6) & 511];
            }
            case 1:
            {
                var word = Shadow[(x + (u >> 1)) & 1023, y & 511];
                var index = (word >> ((u & 1) << 3)) & 0xFF;
                return Shadow[((clut & 0x3F) * 16 + index) & 1023, (clut >> 6) & 511];
            }
            default:
                return Shadow[(x + u) & 1023, y & 511];
        }
    }

    private static bool HasInvalidW(in Vert a, in Vert b, in Vert c)
    {
        return !a.PreciseW || !b.PreciseW || !c.PreciseW || !float.IsFinite(a.Pw) || !float.IsFinite(b.Pw) || !float.IsFinite(c.Pw) || a.Pw <= 0f || b.Pw <= 0f || c.Pw <= 0f;
    }
}
