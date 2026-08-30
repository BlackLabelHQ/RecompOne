namespace RecompOne.Runtime.Hle;

//prim
public struct PrimVertex
{
    public float X, Y;
    public float U, V;
    public byte R, G, B;

    public PrimVertex(float x, float y)
    {
        X = x;
        Y = y;
        U = 0f;
        V = 0f;
        R = 128;
        G = 128;
        B = 128;
    }

    public PrimVertex(float x, float y, float u, float v)
    {
        X = x;
        Y = y;
        U = u;
        V = v;
        R = 128;
        G = 128;
        B = 128;
    }

    public PrimVertex(float x, float y, byte r, byte g, byte b)
    {
        X = x;
        Y = y;
        U = 0f;
        V = 0f;
        R = r;
        G = g;
        B = b;
    }

    public PrimVertex(float x, float y, float u, float v, byte r, byte g, byte b)
    {
        X = x;
        Y = y;
        U = u;
        V = v;
        R = r;
        G = g;
        B = b;
    }
}

public static class GpuPrims
{
    public const int MaxOrder = 0x800;
    private const int MaxEntries = 0x4000;

    private struct Entry
    {
        public int Next;
        public int Count;
        public bool UseImage, SemiTrans, Raw, Gouraud;
        public int Image, Blend;
        public PrimVertex V0, V1, V2, V3;
    }

    private static Entry[] _entries = new Entry[256];
    private static int _n;

    private static readonly int[] _heads = NewLinks();
    private static readonly int[] _tails = NewLinks();
    private static readonly List<int> _used = [];

    private static int[] NewLinks()
    {
        var a = new int[MaxOrder];
        Array.Fill(a, -1);
        return a;
    }

    public static uint OtBase { get; private set; }
    public static int OtLength { get; private set; }

    public static bool Any => _n != 0;

    public static void SetOrderingTable(uint baseAddr, int length)
    {
        OtBase = baseAddr;
        OtLength = length;
    }

    public static int RegisterImage(ReadOnlySpan<byte> rgba, int width, int height)
    {
        return GpuHle.Backend?.RegisterImage(rgba, width, height) ?? -1;
    }

    public static void Clear()
    {
        for (var i = 0; i < _used.Count; i++)
        {
            _heads[_used[i]] = -1;
            _tails[_used[i]] = -1;
        }

        _used.Clear();
        _n = 0;
    }

    public static void Tri(int order, in PrimVertex a, in PrimVertex b, in PrimVertex c,
        int image = -1, bool semiTrans = false, int blend = 0, bool raw = true, bool gouraud = false)
    {
        var i = Alloc(order, image, semiTrans, blend, raw, gouraud);
        if (i < 0) return;
        ref var e = ref _entries[i];
        e.Count = 3;
        e.V0 = a;
        e.V1 = b;
        e.V2 = c;
    }

    public static void Quad(int order, in PrimVertex a, in PrimVertex b, in PrimVertex c, in PrimVertex d,
        int image = -1, bool semiTrans = false, int blend = 0, bool raw = true, bool gouraud = false)
    {
        var i = Alloc(order, image, semiTrans, blend, raw, gouraud);
        if (i < 0) return;
        ref var e = ref _entries[i];
        e.Count = 4;
        e.V0 = a;
        e.V1 = b;
        e.V2 = c;
        e.V3 = d;
    }

    public static void Sprite(int order, int image, float x, float y, float w, float h,
        bool semiTrans = false, int blend = 0,
        float u0 = 0f, float v0 = 0f, float u1 = 1f, float v1 = 1f)
    {
        Quad(order,
            new PrimVertex(x, y, u0, v0),
            new PrimVertex(x + w, y, u1, v0),
            new PrimVertex(x, y + h, u0, v1),
            new PrimVertex(x + w, y + h, u1, v1),
            image, semiTrans, blend);
    }

    private static int Alloc(int order, int image, bool semiTrans, int blend, bool raw, bool gouraud)
    {
        if ((uint)order >= MaxOrder || _n >= MaxEntries) return -1;

        if (_n == _entries.Length) Array.Resize(ref _entries, _entries.Length * 2);

        var idx = _n++;
        ref var e = ref _entries[idx];
        e.Next = -1;
        e.UseImage = image >= 0;
        e.Image = image;
        e.SemiTrans = semiTrans;
        e.Blend = blend & 3;
        e.Raw = raw;
        e.Gouraud = gouraud;

        if (_heads[order] < 0)
        {
            _heads[order] = idx;
            _used.Add(order);
        }
        else
        {
            _entries[_tails[order]].Next = idx;
        }

        _tails[order] = idx;

        return idx;
    }

    internal static void Emit(int order, Gpu gpu)
    {
        if ((uint)order >= MaxOrder) return;
        for (var i = _heads[order]; i >= 0; i = _entries[i].Next)
        {
            ref var e = ref _entries[i];
            gpu.EmitPrim(e.Count, in e.V0, in e.V1, in e.V2, in e.V3,
                e.UseImage, e.Image, e.SemiTrans, e.Raw, e.Gouraud, e.Blend);
        }
    }
}