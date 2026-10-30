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
    public string? Group;
    public bool Evicted;
}

public static class TextureRegistry
{
    private const int MaxEntries = 4096; //size of tabl

    private static readonly Dictionary<ulong, SeenTexture> _seen = [];
    private static readonly HashSet<ulong> _everSeen = [];
    private static readonly HashSet<ulong> _everSeenArt = [];
    private static readonly object _gate = new();
    private static readonly HashSet<object> _collectors = [];
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

    public static int Version => Volatile.Read(ref _version);
    private static int _version;

    public static void Collect(object owner, bool on)
    {
        lock (_collectors)
        {
            if (on) _collectors.Add(owner);
            else _collectors.Remove(owner);
            Enabled = _collectors.Count > 0;
        }
    }

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
            {
                entry.Evicted = true;
                if (entry.GlTex != 0) _orphanTextures.Add(entry.GlTex);
            }
            _seen.Clear();
            _everSeen.Clear();
            _everSeenArt.Clear();
            _version++;
        }
    }

    public static SeenTexture? Note(ushort[] vram, in TileRect rect, ulong indexHash, ulong clutHash,
        int tpage, int clut, bool replaced, bool isPage = false, bool dynamic = false, ushort[]? window = null,
        string? group = null)
    {
        if (!Enabled) return null;

        var id = indexHash ^ (clutHash * 1099511628211UL) ^ (isPage ? 0x5BF03635UL : 0);
        lock (_gate)
        {
            if (_seen.TryGetValue(id, out var known))
            {
                known.Hits++;
                known.LastSeen = Interlocked.Increment(ref _clock);
                known.Replaced = replaced;
                return known;
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
            Group = group,
            Thumb = window != null
                ? TextureTile.DecodeFrom(window, TextureTile.CopyClut(vram, rect), rect)
                : TextureTile.Decode(vram, rect)
        };

        lock (_gate)
        {
            entry.FirstSeen = Interlocked.Increment(ref _clock);
            entry.LastSeen = entry.FirstSeen;
            if (!_seen.TryAdd(id, entry)) return _seen[id];
            _version++;
            _everSeen.Add(id);
            _everSeenArt.Add(indexHash);
        }

        return entry;
    }

    public static void Touch(SeenTexture entry)
    {
        if (!Enabled) return;
        entry.LastSeen = Interlocked.Increment(ref _clock);
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
            entry.Evicted = true;
            if (entry.GlTex != 0) _orphanTextures.Add(entry.GlTex);
            _seen.Remove(id);
        }

        _version++;
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