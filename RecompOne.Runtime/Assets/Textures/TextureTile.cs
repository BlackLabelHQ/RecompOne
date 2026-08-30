namespace RecompOne.Runtime.Assets.Textures;

public struct TileRect
{
    public int PageX, PageY;
    public int U0, V0, W, H;
    public int Bpp;
    public int ClutX, ClutY;
    public int ClutCount;

    public readonly int VramX => PageX + Bpp switch { 4 => U0 >> 2, 8 => U0 >> 1, _ => U0 };
    public readonly int VramY => PageY + V0;

    public readonly int VramW => Bpp switch
    {
        4 => ((U0 + W - 1) >> 2) - (U0 >> 2) + 1,
        8 => ((U0 + W - 1) >> 1) - (U0 >> 1) + 1,
        _ => W
    };
}

public static class TextureTile
{
    public const int MaxTexels = 256 * 256;

    public static TileRect Describe(int tpage, int clut, int u0, int v0, int w, int h)
    {
        var depth = (tpage >> 7) & 3;
        var bpp = depth switch { 0 => 4, 1 => 8, _ => 16 };
        var r = new TileRect
        {
            PageX = (tpage & 0xF) * 64,
            PageY = ((tpage >> 4) & 1) * 256,
            U0 = u0,
            V0 = v0,
            W = w,
            H = h,
            Bpp = bpp,
            ClutX = (clut & 0x3F) * 16,
            ClutY = (clut >> 6) & 0x1FF,
            ClutCount = bpp switch { 4 => 16, 8 => 256, _ => 0 }
        };
        return r;
    }

    public static bool Hash(ushort[] vram, in TileRect r, out ulong indexHash, out ulong clutHash)
    {
        indexHash = 0;
        clutHash = 0;
        if (r.W <= 0 || r.H <= 0 || r.W * r.H > MaxTexels) return false;

        Span<ulong> used = stackalloc ulong[4];
        used.Clear();

        var h = 1469598103934665603UL;
        Fnv(ref h, (byte)r.Bpp);
        Fnv(ref h, (byte)(r.W & 0xFF));
        Fnv(ref h, (byte)(r.W >> 8));
        Fnv(ref h, (byte)(r.H & 0xFF));
        Fnv(ref h, (byte)(r.H >> 8));

        var wordX = r.VramX;
        var words = r.VramW;

        for (var y = 0; y < r.H; y++)
        {
            var row = ((r.VramY + y) & 511) * 1024;
            for (var x = 0; x < words; x++)
            {
                var v = vram[row + ((wordX + x) & 1023)];
                Fnv(ref h, (byte)(v & 0xFF));
                Fnv(ref h, (byte)(v >> 8));

                if (r.Bpp == 4)
                {
                    used[0] |= 1UL << ((v >> 0) & 0xF);
                    used[0] |= 1UL << ((v >> 4) & 0xF);
                    used[0] |= 1UL << ((v >> 8) & 0xF);
                    used[0] |= 1UL << ((v >> 12) & 0xF);
                }
                else if (r.Bpp == 8)
                {
                    int a = v & 0xFF, b = (v >> 8) & 0xFF;
                    used[a >> 6] |= 1UL << (a & 63);
                    used[b >> 6] |= 1UL << (b & 63);
                }
            }
        }

        indexHash = h;

        if (r.ClutCount == 0) return true;

        var c = 1469598103934665603UL;
        var clutRow = (r.ClutY & 511) * 1024;
        for (var i = 0; i < r.ClutCount; i++)
        {
            if ((used[i >> 6] & (1UL << (i & 63))) == 0) continue;
            var e = vram[clutRow + ((r.ClutX + i) & 1023)];
            Fnv(ref c, (byte)(i & 0xFF));
            Fnv(ref c, (byte)(e & 0xFF));
            Fnv(ref c, (byte)(e >> 8));
        }

        clutHash = c;
        return true;
    }

    private static void Fnv(ref ulong h, byte b)
    {
        h ^= b;
        h *= 1099511628211UL;
    }

    public static ushort[] CopyWindow(ushort[] vram, in TileRect r)
    {
        var win = new ushort[r.VramW * r.H];
        for (var y = 0; y < r.H; y++)
        {
            var row = ((r.VramY + y) & 511) * 1024;
            for (var x = 0; x < r.VramW; x++)
                win[y * r.VramW + x] = vram[row + ((r.VramX + x) & 1023)];
        }

        return win;
    }

    public static ushort[] CopyClut(ushort[] vram, in TileRect r)
    {
        if (r.ClutCount == 0) return [];
        var entries = new ushort[r.ClutCount];
        var clutRow = (r.ClutY & 511) * 1024;
        for (var i = 0; i < r.ClutCount; i++)
            entries[i] = vram[clutRow + ((r.ClutX + i) & 1023)];
        return entries;
    }

    private static int WordOf(in TileRect r, int x)
    {
        var u = r.U0 + x;
        return r.Bpp switch { 4 => (u >> 2) - (r.U0 >> 2), 8 => (u >> 1) - (r.U0 >> 1), _ => u - r.U0 };
    }

    public static byte[] DecodeFrom(ushort[] win, ushort[] clut, in TileRect r)
    {
        var rgba = new byte[r.W * r.H * 4];
        for (var y = 0; y < r.H; y++)
        {
            var row = y * r.VramW;
            for (var x = 0; x < r.W; x++)
            {
                var u = r.U0 + x;
                var word = win[row + WordOf(r, x)];
                var texel = r.Bpp switch
                {
                    16 => word,
                    8 => clut[(word >> ((u & 1) << 3)) & 0xFF],
                    _ => clut[(word >> ((u & 3) << 2)) & 0xF]
                };
                Expand(texel, rgba, (y * r.W + x) * 4);
            }
        }

        return rgba;
    }

    public static byte[] DecodeIndicesFrom(ushort[] win, in TileRect r)
    {
        var px = new byte[r.W * r.H];
        if (r.Bpp == 16) return px;

        for (var y = 0; y < r.H; y++)
        {
            var row = y * r.VramW;
            for (var x = 0; x < r.W; x++)
            {
                var u = r.U0 + x;
                var word = win[row + WordOf(r, x)];
                px[y * r.W + x] = r.Bpp == 8
                    ? (byte)((word >> ((u & 1) << 3)) & 0xFF)
                    : (byte)((word >> ((u & 3) << 2)) & 0xF);
            }
        }

        return px;
    }

    public static byte[] DecodeClutFrom(ushort[] clut)
    {
        var rgba = new byte[clut.Length * 4];
        for (var i = 0; i < clut.Length; i++) Expand(clut[i], rgba, i * 4);
        return rgba;
    }

    public static byte[] RawBytes(ushort[] words)
    {
        var bytes = new byte[words.Length * 2];
        for (var i = 0; i < words.Length; i++)
        {
            bytes[i * 2] = (byte)(words[i] & 0xFF);
            bytes[i * 2 + 1] = (byte)(words[i] >> 8);
        }

        return bytes;
    }

    public static byte[] Decode(ushort[] vram, in TileRect r)
    {
        var rgba = new byte[r.W * r.H * 4];
        var clutRow = (r.ClutY & 511) * 1024;

        for (var y = 0; y < r.H; y++)
        {
            var row = ((r.VramY + y) & 511) * 1024;
            for (var x = 0; x < r.W; x++)
            {
                var u = r.U0 + x;
                ushort texel;
                if (r.Bpp == 16)
                {
                    texel = vram[row + ((r.PageX + u) & 1023)];
                }
                else if (r.Bpp == 8)
                {
                    var word = vram[row + ((r.PageX + (u >> 1)) & 1023)];
                    var idx = (word >> ((u & 1) << 3)) & 0xFF;
                    texel = vram[clutRow + ((r.ClutX + idx) & 1023)];
                }
                else
                {
                    var word = vram[row + ((r.PageX + (u >> 2)) & 1023)];
                    var idx = (word >> ((u & 3) << 2)) & 0xF;
                    texel = vram[clutRow + ((r.ClutX + idx) & 1023)];
                }

                var o = (y * r.W + x) * 4;
                Expand(texel, rgba, o);
            }
        }

        return rgba;
    }

    public static byte[] DecodeIndices(ushort[] vram, in TileRect r)
    {
        var px = new byte[r.W * r.H];
        if (r.Bpp == 16) return px;

        for (var y = 0; y < r.H; y++)
        {
            var row = ((r.VramY + y) & 511) * 1024;
            for (var x = 0; x < r.W; x++)
            {
                var u = r.U0 + x;
                if (r.Bpp == 8)
                {
                    var word = vram[row + ((r.PageX + (u >> 1)) & 1023)];
                    px[y * r.W + x] = (byte)((word >> ((u & 1) << 3)) & 0xFF);
                }
                else
                {
                    var word = vram[row + ((r.PageX + (u >> 2)) & 1023)];
                    px[y * r.W + x] = (byte)((word >> ((u & 3) << 2)) & 0xF);
                }
            }
        }

        return px;
    }

    public static byte[] DecodeClut(ushort[] vram, in TileRect r)
    {
        if (r.ClutCount == 0) return [];
        var rgba = new byte[r.ClutCount * 4];
        var clutRow = (r.ClutY & 511) * 1024;
        for (var i = 0; i < r.ClutCount; i++)
            Expand(vram[clutRow + ((r.ClutX + i) & 1023)], rgba, i * 4);
        return rgba;
    }

    public static void Expand(ushort texel, byte[] dst, int offset)
    {
        int r5 = texel & 0x1F, g5 = (texel >> 5) & 0x1F, b5 = (texel >> 10) & 0x1F;
        var stp = (texel & 0x8000) != 0;

        dst[offset + 0] = (byte)((r5 << 3) | (r5 >> 2));
        dst[offset + 1] = (byte)((g5 << 3) | (g5 >> 2));
        dst[offset + 2] = (byte)((b5 << 3) | (b5 >> 2));
        dst[offset + 3] = texel == 0 ? (byte)0 : stp ? (byte)128 : (byte)255;
    }
}