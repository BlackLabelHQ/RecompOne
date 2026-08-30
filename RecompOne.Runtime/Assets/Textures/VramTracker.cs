namespace RecompOne.Runtime.Assets.Textures;

public static class VramTracker
{
    public const int BlockW = 16;
    public const int BlockH = 16;
    public const int Cols = 1024 / BlockW;
    public const int Rows = 512 / BlockH;

    private static readonly int[] _gen = new int[Cols * Rows];
    private static readonly bool[] _gpuDirty = new bool[Cols * Rows];
    private static int _clock;

    public static void Reset()
    {
        Array.Clear(_gen);
        Array.Clear(_gpuDirty);
        _clock = 0;
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