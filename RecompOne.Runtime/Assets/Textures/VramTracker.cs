namespace RecompOne.Runtime.Assets.Textures;

public static class VramTracker
{
    public const int BlockW = 16;
    public const int BlockH = 16;
    public const int Cols = 1024 / BlockW;
    public const int Rows = 512 / BlockH;

    private const int Width = 1024;
    private const int Height = 512;
    private const int MaxUploads = 1 << 20;

    private static readonly int[] _gen = new int[Cols * Rows];
    private static readonly bool[] _gpuDirty = new bool[Cols * Rows];
    private static readonly int[] _owner = new int[Width * Height];
    private static readonly List<(int X, int Y, int W, int H)> _uploads = [];
    private static int _clock;

    public static int OwnerVersion { get; private set; }

    public static void Reset()
    {
        Array.Clear(_gen);
        Array.Clear(_gpuDirty);
        Array.Clear(_owner);
        _uploads.Clear();
        _clock = 0;
        OwnerVersion++;
        TextureSources.Reset();
    }

    private static bool Clip(ref int x, ref int y, ref int w, ref int h)
    {
        if (w < 0)
        {
            x += w;
            w = -w;
        }

        if (h < 0)
        {
            y += h;
            h = -h;
        }

        if (x < 0)
        {
            w += x;
            x = 0;
        }

        if (y < 0)
        {
            h += y;
            y = 0;
        }

        w = Math.Min(w, Width - x);
        h = Math.Min(h, Height - y);
        return w > 0 && h > 0;
    }

    public static void MarkUpload(int x, int y, int w, int h)
    {
        MarkCpuWrite(x, y, w, h);
        if (!Clip(ref x, ref y, ref w, ref h)) return;

        if (_uploads.Count >= MaxUploads)
        {
            Array.Clear(_owner);
            _uploads.Clear();
        }

        OwnerVersion++;
        _uploads.Add((x, y, w, h));
        var id = _uploads.Count;
        for (var row = y; row < y + h; row++) _owner.AsSpan(row * Width + x, w).Fill(id);
    }

    public static void MarkFill(int x, int y, int w, int h)
    {
        MarkCpuWrite(x, y, w, h);
        if (!Clip(ref x, ref y, ref w, ref h)) return;
        OwnerVersion++;
        for (var row = y; row < y + h; row++) _owner.AsSpan(row * Width + x, w).Clear();
    }

    public static bool FindUpload(int x, int y, int w, int h, out int ux, out int uy, out int uw, out int uh)
    {
        ux = uy = uw = uh = 0;
        if (!Clip(ref x, ref y, ref w, ref h)) return false;

        var id = _owner[y * Width + x];
        if (id <= 0 || id > _uploads.Count) return false;

        for (var row = y; row < y + h; row++)
            if (_owner.AsSpan(row * Width + x, w).IndexOfAnyExcept(id) >= 0)
                return false;

        (ux, uy, uw, uh) = _uploads[id - 1];
        for (var row = uy; row < uy + uh; row++)
            if (_owner.AsSpan(row * Width + ux, uw).IndexOfAnyExcept(id) >= 0)
                return false;

        return !IsGpuDirty(ux, uy, uw, uh);
    }

    private static void Bounds(int x, int y, int w, int h, out int c0, out int r0, out int c1, out int r1)
    {
        if (w < 0)
        {
            x += w;
            w = -w;
        }

        if (h < 0)
        {
            y += h;
            h = -h;
        }

        c0 = Math.Clamp(x / BlockW, 0, Cols - 1);
        r0 = Math.Clamp(y / BlockH, 0, Rows - 1);
        c1 = Math.Clamp((x + Math.Max(0, w - 1)) / BlockW, 0, Cols - 1);
        r1 = Math.Clamp((y + Math.Max(0, h - 1)) / BlockH, 0, Rows - 1);
    }

    public static void MarkCpuWrite(int x, int y, int w, int h)
    {
        var stamp = ++_clock;
        Bounds(x, y, w, h, out var c0, out var r0, out var c1, out var r1);
        for (var r = r0; r <= r1; r++)
        for (var c = c0; c <= c1; c++)
        {
            var i = r * Cols + c;
            _gen[i] = stamp;
            _gpuDirty[i] = false;
        }
    }

    public static void MarkGpuWrite(int x, int y, int w, int h)
    {
        var stamp = ++_clock;
        Bounds(x, y, w, h, out var c0, out var r0, out var c1, out var r1);
        for (var r = r0; r <= r1; r++)
        for (var c = c0; c <= c1; c++)
        {
            var i = r * Cols + c;
            _gen[i] = stamp;
            _gpuDirty[i] = true;
        }
    }

    public static int Generation(int x, int y, int w, int h)
    {
        Bounds(x, y, w, h, out var c0, out var r0, out var c1, out var r1);
        var acc = 0;
        for (var r = r0; r <= r1; r++)
        for (var c = c0; c <= c1; c++)
            acc = acc * 31 + _gen[r * Cols + c];
        return acc;
    }

    public static bool IsGpuDirty(int x, int y, int w, int h)
    {
        Bounds(x, y, w, h, out var c0, out var r0, out var c1, out var r1);
        for (var r = r0; r <= r1; r++)
        for (var c = c0; c <= c1; c++)
            if (_gpuDirty[r * Cols + c])
                return true;
        return false;
    }
}