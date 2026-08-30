namespace RecompOne.Runtime.Assets.Textures;

public sealed class SeenTexture
{
    public ulong IndexHash, ClutHash;
    public int TPage, Clut;
    public int Bpp, W, H, U0, V0;
    public bool IsPage;
    public bool Dynamic;
    public bool Replaced;
    public int Hits;
    public long FirstSeen;
    public long LastSeen;
    public byte[] Thumb = [];
    public uint GlTex;
}

public static class TextureRegistry
{
    private const int MaxEntries = 4096; //size of tabl

    private static readonly Dictionary<ulong, SeenTexture> _seen = [];
    private static readonly HashSet<ulong> _everSeen = [];
    private static readonly HashSet<ulong> _everSeenArt = [];
    private static readonly object _gate = new();
    private static long _clock;

    public static int UniqueKeys
    {
        get
        {
            lock (_gate)
            {
                return _everSeen.Count;
            }
        }
    }

    public static int UniqueArtworks
    {
        get
        {
            lock (_gate)
            {
                return _everSeenArt.Count;
            }
        }
    }

    public static bool Enabled { get; set; }

    public static int Count
    {
        get
        {
            lock (_gate)
            {
                return _seen.Count;
            }
        }
    }

    public static void Clear()
    {
        lock (_gate)
        {
            foreach (var entry in _seen.Values)
                if (entry.GlTex != 0)
                    _orphanTextures.Add(entry.GlTex);
            _seen.Clear();
            _everSeen.Clear();
            _everSeenArt.Clear();
        }
    }

    public static void Note(ushort[] vram, in TileRect rect, ulong indexHash, ulong clutHash,
        int tpage, int clut, bool replaced, bool isPage = false, bool dynamic = false)
    {
        if (!Enabled) return;

        var id = indexHash ^ (clutHash * 1099511628211UL) ^ (isPage ? 0x5BF03635UL : 0);
        lock (_gate)
        {
            if (_seen.TryGetValue(id, out var known))
            {
                known.Hits++;
                known.LastSeen = ++_clock;
                known.Replaced = replaced;
                return;
            }

            if (_seen.Count >= MaxEntries) Evict();
        }

        var entry = new SeenTexture
        {
            IndexHash = indexHash,
            ClutHash = clutHash,
            TPage = tpage,
            Clut = clut,
            Bpp = rect.Bpp,
            W = rect.W,
            H = rect.H,
            U0 = rect.U0,
            V0 = rect.V0,
            IsPage = isPage,
            Dynamic = dynamic,
            Replaced = replaced,
            Hits = 1,
            Thumb = TextureTile.Decode(vram, rect)
        };

        lock (_gate)
        {
            entry.FirstSeen = ++_clock;
            entry.LastSeen = entry.FirstSeen;
            _seen.TryAdd(id, entry);
            _everSeen.Add(id);
            _everSeenArt.Add(indexHash);
        }
    }

    public static SeenTexture[] Snapshot()
    {
        lock (_gate)
        {
            return _seen.Values.ToArray();
        }
    }

    private static readonly List<uint> _orphanTextures = [];

    private static void Evict()
    {
        var drop = Math.Max(1, MaxEntries / 8);
        var oldest = _seen.OrderBy(p => p.Value.LastSeen).Take(drop).ToArray();
        foreach (var (id, entry) in oldest)
        {
            if (entry.GlTex != 0) _orphanTextures.Add(entry.GlTex);
            _seen.Remove(id);
        }
    }

    public static uint[] TakeOrphanTextures()
    {
        lock (_gate)
        {
            if (_orphanTextures.Count == 0) return [];
            var ids = _orphanTextures.ToArray();
            _orphanTextures.Clear();
            return ids;
        }
    }
}