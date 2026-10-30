namespace RecompOne.Runtime.Assets.Textures;

public enum SourceMatch
{
    Outside,
    Discard,
    Found
}

public readonly struct SourceTexture
{
    public readonly TileRect Rect;
    public readonly long Identity;
    public readonly string Group;

    internal SourceTexture(in TileRect rect, long identity, string group)
    {
        Rect = rect;
        Identity = identity;
        Group = group;
    }
}

public static class TextureSources
{
    private sealed class Area(string name, int x, int y, int w, int h, int cellW, int cellH, bool exclusive, bool snap)
    {
        public readonly string Name = name;
        public readonly int X = x, Y = y, W = w, H = h, CellW = cellW, CellH = cellH;
        public readonly bool Exclusive = exclusive, Snap = snap;
        public int Version;

        public bool Overlaps(int x, int y, int w, int h)
        {
            return X < x + w && x < X + W && Y < y + h && y < Y + H;
        }
    }

    private readonly struct Cached(Area area, int version, SourceMatch match, SourceTexture found)
    {
        public readonly Area Area = area;
        public readonly int Version = version;
        public readonly SourceMatch Match = match;
        public readonly SourceTexture Found = found;
    }

    private sealed class Upload
    {
        public required Area Area;
        public required int Id, X, Y, W, H;
        public required ushort[] Pixels;
    }

    private static readonly List<Area> _areas = [];
    private static readonly List<Upload> _uploads = [];
    private static readonly Dictionary<long, Cached> _cache = [];
    private static readonly object _gate = new();
    private static int _nextId;

    public static bool Active { get; private set; }

    public static void Declare(string name, int x, int y, int w, int h, int cellW = 0, int cellH = 0,
        bool exclusive = false, bool snap = false)
    {
        lock (_gate)
        {
            _areas.Add(new Area(name, x, y, w, h, cellW, cellH, exclusive, snap));
            Active = true;
        }
    }

    public static void ClearDeclarations()
    {
        lock (_gate)
        {
            _areas.Clear();
            _uploads.Clear();
            _cache.Clear();
            Active = false;
        }
    }

    public static void Reset()
    {
        lock (_gate)
        {
            _uploads.Clear();
            _cache.Clear();
            foreach (var a in _areas) a.Version++;
        }
    }

    internal static void OnUpload(ushort[] vram, int x, int y, int w, int h)
    {
        if (!Active) return;
        lock (_gate)
        {
            Forget(x, y, w, h);
            var area = _areas.FirstOrDefault(a => x >= a.X && y >= a.Y && x + w <= a.X + a.W && y + h <= a.Y + a.H);
            if (area == null) return;

            var pixels = new ushort[w * h];
            for (var row = 0; row < h; row++)
                vram.AsSpan(((y + row) & 511) * 1024 + x, w).CopyTo(pixels.AsSpan(row * w, w));
            _uploads.Add(new Upload { Area = area, Id = ++_nextId, X = x, Y = y, W = w, H = h, Pixels = pixels });
        }
    }

    internal static void OnClear(int x, int y, int w, int h)
    {
        if (!Active) return;
        lock (_gate)
        {
            Forget(x, y, w, h);
        }
    }

    private static void Forget(int x, int y, int w, int h)
    {
        _uploads.RemoveAll(u => u.X < x + w && x < u.X + u.W && u.Y < y + h && y < u.Y + u.H);
        Touch(x, y, w, h);
    }

    private static void Touch(int x, int y, int w, int h)
    {
        foreach (var a in _areas)
            if (a.Overlaps(x, y, w, h))
                a.Version++;
    }

    private static Area? AreaAt(int x, int y)
    {
        foreach (var a in _areas)
            if (x >= a.X && y >= a.Y && x < a.X + a.W && y < a.Y + a.H)
                return a;
        return null;
    }

    internal static SourceMatch Find(int tpage, int clut, int u0, int v0, int w, int h, int edgeU, int edgeV,
        out SourceTexture found)
    {
        found = default;
        if (edgeU != 0 && w > 1)
        {
            if (edgeU < 0) u0++;
            w--;
        }

        if (edgeV != 0 && h > 1)
        {
            if (edgeV < 0) v0++;
            h--;
        }

        var probe = TextureTile.Describe(tpage, clut, u0, v0, w, h);
        int cx = probe.VramX + probe.VramW / 2, cy = probe.VramY + probe.H / 2;

        var area = AreaAt(cx, cy);
        if (area == null) return SourceMatch.Outside;

        var key = (long)(tpage & 0x1FF)
                  | ((long)(clut & 0x7FFF) << 9)
                  | ((long)(u0 & 0x1FF) << 24)
                  | ((long)(v0 & 0x1FF) << 33)
                  | ((long)(w & 0x1FF) << 42)
                  | ((long)(h & 0x1FF) << 51);

        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var hit) && hit.Area == area && hit.Version == area.Version)
            {
                found = hit.Found;
                return hit.Match;
            }

            var version = area.Version;
            var match = Locate(area, probe, tpage, clut, u0, v0, w, h, out found);
            var inside = probe.VramX >= area.X && probe.VramY >= area.Y && probe.VramX + probe.VramW <= area.X + area.W &&
                         probe.VramY + probe.H <= area.Y + area.H;
            if (inside)
            {
                if (_cache.Count > 65536) _cache.Clear();
                _cache[key] = new Cached(area, version, match, found);
            }

            return match;
        }
    }

    private static SourceMatch Locate(Area area, in TileRect probe, int tpage, int clut, int u0, int v0, int w, int h,
        out SourceTexture found)
    {
        found = default;
        int cx = probe.VramX + probe.VramW / 2, cy = probe.VramY + probe.H / 2;
        var texels = probe.Bpp switch { 4 => 4, 8 => 2, _ => 1 };
        var missing = area.Exclusive ? SourceMatch.Discard : SourceMatch.Outside;

        var home = _uploads.FindLast(u => u.Area == area && cx >= u.X && cy >= u.Y && cx < u.X + u.W && cy < u.Y + u.H);
        if (home == null) return missing;

        var su0 = (home.X - probe.PageX) * texels;
        var sv0 = home.Y - probe.PageY;
        var su1 = su0 + home.W * texels;
        var sv1 = sv0 + home.H;

        if (area.Snap && u0 >= su0 - 1 && v0 >= sv0 - 1 && u0 + w <= su1 + 1 && v0 + h <= sv1 + 1)
        {
            (u0, v0, w, h) = (su0, sv0, su1 - su0, sv1 - sv0);
        }
        else if (area.CellW > 0 && w == area.CellW && h == area.CellH &&
                 (u0 - su0) % area.CellW == 0 && (v0 - sv0) % area.CellH == 0)
        {
        }
        else
        {
            var a0 = Math.Max(u0, 0);
            var b0 = Math.Max(v0, 0);
            (u0, v0, w, h) = (a0, b0, Math.Min(u0 + w, 256) - a0, Math.Min(v0 + h, 256) - b0);
        }

        if (w <= 0 || h <= 0 || u0 + w > 256 || v0 + h > 256) return missing;

        var rect = TextureTile.Describe(tpage, clut, u0, v0, w, h);
        long covered = 0;
        var identity = unchecked((long)1469598103934665603UL);
        foreach (var u in _uploads)
        {
            var ix0 = Math.Max(u.X, rect.VramX);
            var iy0 = Math.Max(u.Y, rect.VramY);
            var ix1 = Math.Min(u.X + u.W, rect.VramX + rect.VramW);
            var iy1 = Math.Min(u.Y + u.H, rect.VramY + rect.H);
            if (ix1 <= ix0 || iy1 <= iy0) continue;
            covered += (long)(ix1 - ix0) * (iy1 - iy0);
            identity = unchecked((identity ^ u.Id) * 1099511628211L);
        }

        if (covered < (long)rect.VramW * rect.H) return missing;

        identity = unchecked((identity ^ ((long)rect.VramX << 40 | (long)rect.VramY << 30 | (long)rect.U0 << 20 |
                                          (long)rect.V0 << 10 | (long)rect.Bpp)) * 1099511628211L);
        identity = unchecked((identity ^ ((long)rect.W << 16 | (long)rect.H)) * 1099511628211L);
        found = new SourceTexture(rect, identity, area.Name);
        return SourceMatch.Found;
    }

    internal static ushort[] Window(in SourceTexture source)
    {
        var rect = source.Rect;
        var window = new ushort[rect.VramW * rect.H];
        lock (_gate)
        {
            foreach (var u in _uploads)
            {
                var ix0 = Math.Max(u.X, rect.VramX);
                var iy0 = Math.Max(u.Y, rect.VramY);
                var ix1 = Math.Min(u.X + u.W, rect.VramX + rect.VramW);
                var iy1 = Math.Min(u.Y + u.H, rect.VramY + rect.H);
                if (ix1 <= ix0 || iy1 <= iy0) continue;
                for (var y = iy0; y < iy1; y++)
                    u.Pixels.AsSpan((y - u.Y) * u.W + ix0 - u.X, ix1 - ix0)
                        .CopyTo(window.AsSpan((y - rect.VramY) * rect.VramW + ix0 - rect.VramX, ix1 - ix0));
            }
        }

        if (rect.Bpp == 16) return window;

        var texels = rect.Bpp == 4 ? 4 : 2;
        var bits = 16 / texels;
        var first = rect.U0 % texels;
        var last = (rect.U0 + rect.W - 1) % texels;
        var keepFirst = (ushort)(0xFFFF << (first * bits));
        var keepLast = (ushort)(0xFFFF >> ((texels - 1 - last) * bits));
        for (var y = 0; y < rect.H; y++)
        {
            var row = y * rect.VramW;
            window[row] &= keepFirst;
            window[row + rect.VramW - 1] &= keepLast;
        }

        return window;
    }
}
