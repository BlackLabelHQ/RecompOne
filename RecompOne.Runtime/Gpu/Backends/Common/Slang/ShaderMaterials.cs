namespace RecompOne.Runtime.Hle;

public static class ShaderMaterials
{
    public const byte None = 0;
    public const byte Water = 1;
    public const byte Lava = 2;
    public const byte Mud = 3;
    public const byte Metal = 4;
    public const byte Emissive = 5;
    public const byte Glass = 6;
    public const byte Ice = 7;
    public const byte Snow = 8;
    public const byte Sand = 9;
    public const byte Stone = 10;
    public const byte Wood = 11;
    public const byte Foliage = 12;
    public const byte Cloth = 13;
    public const byte Skin = 14;
    public const byte Wet = 15;
    public const byte Translucent = 255;

    private const int PageMask = 0x19F;

    private readonly record struct Region(int U0, int V0, int U1, int V1, byte Material);

    private static readonly Lock Gate = new();
    private static readonly Dictionary<uint, List<Region>> Tags = new();
    private static volatile Dictionary<uint, Region[]> _current = new();

    public static bool Any { get; private set; }

    public static bool IsSurface(byte material) => material == Water;

    public static void Tag(int tpage, int clut, int u0, int v0, int u1, int v1, byte material)
    {
        lock (Gate)
        {
            var key = Key(tpage, clut);
            if (!Tags.TryGetValue(key, out var list)) Tags[key] = list = [];
            var region = new Region(Math.Min(u0, u1), Math.Min(v0, v1), Math.Max(u0, u1), Math.Max(v0, v1), material);
            if (list.Contains(region)) return;
            list.Add(region);
            Publish();
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            if (Tags.Count == 0) return;
            Tags.Clear();
            Publish();
        }
    }

    public static void Replace(IEnumerable<(int TPage, int Clut, int U0, int V0, int U1, int V1, byte Material)> tags)
    {
        lock (Gate)
        {
            Tags.Clear();
            foreach (var tag in tags)
            {
                var key = Key(tag.TPage, tag.Clut);
                if (!Tags.TryGetValue(key, out var list)) Tags[key] = list = [];
                list.Add(new Region(Math.Min(tag.U0, tag.U1), Math.Min(tag.V0, tag.V1), Math.Max(tag.U0, tag.U1),
                    Math.Max(tag.V0, tag.V1), tag.Material));
            }

            Publish();
        }
    }

    internal static byte Lookup(int tpage, int clut, float u, float v)
    {
        if (!_current.TryGetValue(Key(tpage, clut), out var regions)) return None;
        foreach (var region in regions)
            if (u >= region.U0 && u <= region.U1 && v >= region.V0 && v <= region.V1)
                return region.Material;
        return None;
    }

    private static uint Key(int tpage, int clut)
    {
        return (uint)(tpage & PageMask) | (uint)(clut & 0xFFFF) << 16;
    }

    private static void Publish()
    {
        var next = new Dictionary<uint, Region[]>(Tags.Count);
        foreach (var (key, list) in Tags) next[key] = list.ToArray();
        _current = next;
        Any = next.Count > 0;
    }
}
