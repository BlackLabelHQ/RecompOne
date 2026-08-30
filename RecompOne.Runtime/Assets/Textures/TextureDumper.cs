using System.Collections.Concurrent;
using System.Text;

namespace RecompOne.Runtime.Assets.Textures;

public static class TextureDumper
{
    private sealed class Job
    {
        public ulong Id;
        public ushort[] Window = [];
        public ushort[] Clut = [];
        public TileRect Rect;
        public ulong IndexHash, ClutHash;
        public int TPage, ClutId;
        public bool IsPage;
    }

    private const int QueueCapacity = 8192;

    private static readonly HashSet<ulong> _seen = [];
    private static readonly HashSet<ulong> _idxWritten = [];
    private static readonly HashSet<ulong> _clutWritten = [];
    private static readonly object _gate = new();
    private static BlockingCollection<Job> _queue = NewQueue();
    private static Thread? _worker;
    private static string _root = "";
    private static string _clutRoot = "";
    private static string _pageRoot = "";
    private static int _written, _dropped, _failed, _offered;

    public static bool Tiles { get; private set; }
    public static bool Pages { get; private set; }
    public static bool Enabled => Tiles || Pages;

    public static int UniqueSeen
    {
        get
        {
            lock (_gate)
            {
                return _seen.Count;
            }
        }
    }

    public static int Offered => Volatile.Read(ref _offered);
    public static int Written => Volatile.Read(ref _written);
    public static int Dropped => Volatile.Read(ref _dropped);
    public static int Failed => Volatile.Read(ref _failed);
    public static int Pending => _queue.Count;
    public static string Root => _root;

    private static BlockingCollection<Job> NewQueue()
    {
        return new BlockingCollection<Job>(new ConcurrentQueue<Job>(), QueueCapacity);
    }

    public static void SetTiles(bool on)
    {
        if (on == Tiles) return;
        var was = Enabled;
        Tiles = on;
        Apply(was);
    }

    public static void SetPages(bool on)
    {
        if (on == Pages) return;
        var was = Enabled;
        Pages = on;
        Apply(was);
    }

    public static void SetEnabled(bool on)
    {
        var was = Enabled;
        Tiles = Pages = on;
        Apply(was);
    }

    private static void Apply(bool was)
    {
        if (Enabled == was) return;
        if (Enabled) Start();
        else Stop();
    }

    private static void Start()
    {
        {
            var game = Sanitize(AssetReplacerManager.Instance.GameId);
            _root = Path.GetFullPath(Path.Combine("dump", game, "textures"));
            _clutRoot = Path.GetFullPath(Path.Combine("dump", game, "cluts"));
            _pageRoot = Path.GetFullPath(Path.Combine("dump", game, "pages"));
            try
            {
                Directory.CreateDirectory(_root);
                Directory.CreateDirectory(_clutRoot);
                Directory.CreateDirectory(_pageRoot);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[assets] texture dump: cannot create '{_root}': {ex.Message}");
                return;
            }

            lock (_gate)
            {
                _seen.Clear();
                _idxWritten.Clear();
                _clutWritten.Clear();
            }

            Volatile.Write(ref _written, 0);
            Volatile.Write(ref _dropped, 0);
            Volatile.Write(ref _failed, 0);
            Volatile.Write(ref _offered, 0);

            if (_queue.IsAddingCompleted) _queue = NewQueue();
            EnsureWorker();
            TextureResolver.ResetStats();
            // Console.WriteLine($"[assets] texture dump ON (tiles={Tiles} pages={Pages}) -> {_root}");
        }
    }

    private static void Stop()
    {
        // Console.WriteLine($"[assets] texture dump OFF: seen={UniqueSeen} written={Written} " + $"pending={Pending} dropped={Dropped} failed={Failed}");
        // Console.WriteLine($"[assets] resolver: {TextureResolver.StatsLine()}");
    }

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        return sb.Length == 0 ? "UNKNOWN" : sb.ToString();
    }

    private static void EnsureWorker()
    {
        if (_worker is { IsAlive: true }) return;
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "tex-dump" };
        _worker.Start();
    }

    public static void Offer(ushort[] vram, in TileRect rect, ulong indexHash, ulong clutHash, int tpage, int clut)
    {
        if (!Enabled) return;
        if (!Tiles)
        {
            OfferPage(vram, rect, clutHash, tpage, clut);
            return;
        }

        var id = indexHash ^ (clutHash * 1099511628211UL);
        lock (_gate)
        {
            if (!_seen.Add(id)) return;
        }

        Interlocked.Increment(ref _offered);

        var job = new Job
        {
            Id = id,
            Rect = rect,
            IndexHash = indexHash,
            ClutHash = clutHash,
            TPage = tpage,
            ClutId = clut,
            Window = TextureTile.CopyWindow(vram, rect),
            Clut = TextureTile.CopyClut(vram, rect)
        };

        if (!_queue.TryAdd(job))
        {
            lock (_gate)
            {
                _seen.Remove(id);
            }

            Interlocked.Increment(ref _dropped);
            return;
        }

        OfferPage(vram, rect, clutHash, tpage, clut);
    }

    private static void OfferPage(ushort[] vram, in TileRect tile, ulong clutHash, int tpage, int clut)
    {
        if (!Pages || tile.Bpp == 16) return;

        var widthTexels = tile.Bpp == 4 ? 256 : 128;
        var pageRect = TextureTile.Describe(tpage, clut, 0, 0, widthTexels, 256);
        if (VramTracker.IsGpuDirty(pageRect.VramX, pageRect.VramY, pageRect.VramW, pageRect.H)) return;
        if (!TextureTile.Hash(vram, pageRect, out var pageIndexHash, out var pageClutHash)) return;

        var pageId = 0x9E3779B97F4A7C15UL ^ pageIndexHash ^ (pageClutHash * 1099511628211UL);
        lock (_gate)
        {
            if (!_seen.Add(pageId)) return;
        }

        var job = new Job
        {
            Id = pageId,
            Rect = pageRect,
            IndexHash = pageIndexHash,
            ClutHash = pageClutHash,
            TPage = tpage,
            ClutId = clut,
            IsPage = true,
            Window = TextureTile.CopyWindow(vram, pageRect),
            Clut = TextureTile.CopyClut(vram, pageRect)
        };

        if (_queue.TryAdd(job)) return;
        lock (_gate)
        {
            _seen.Remove(pageId);
        }

        Interlocked.Increment(ref _dropped);
    }

    private static void WorkerLoop()
    {
        foreach (var job in _queue.GetConsumingEnumerable())
            try
            {
                WriteJob(job);
                Interlocked.Increment(ref _written);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failed);
                Console.Error.WriteLine($"[assets] texture dump failed for {job.IndexHash:x16} " +
                                        $"({job.Rect.W}x{job.Rect.H} {job.Rect.Bpp}bpp u0={job.Rect.U0}): {ex.Message}");
            }
    }

    private static void WriteJob(Job job)
    {
        var r = job.Rect;

        if (job.IsPage)
        {
            var page = Path.Combine(_pageRoot, $"{job.IndexHash:x16}_{job.ClutHash:x16}");
            PngWriter.WriteRgba(page + ".png", TextureTile.DecodeFrom(job.Window, job.Clut, r), r.W, r.H);
            File.WriteAllText(page + ".json", $$"""
                                                {
                                                  "kind": "page",
                                                  "indexHash": "{{job.IndexHash:x16}}",
                                                  "clutHash": "{{job.ClutHash:x16}}",
                                                  "bpp": {{r.Bpp}},
                                                  "width": {{r.W}},
                                                  "height": {{r.H}},
                                                  "texpage": {{job.TPage}},
                                                  "clut": {{job.ClutId}}
                                                }
                                                """);
            return;
        }

        var baseName = Path.Combine(_root, $"{job.IndexHash:x16}_{job.ClutHash:x16}");

        PngWriter.WriteRgba(baseName + ".png", TextureTile.DecodeFrom(job.Window, job.Clut, r), r.W, r.H);
        File.WriteAllBytes(baseName + ".idx.bin", TextureTile.RawBytes(job.Window));

        if (r.Bpp != 16)
        {
            bool writeIdx, writeClut;
            lock (_gate)
            {
                writeIdx = _idxWritten.Add(job.IndexHash);
                writeClut = _clutWritten.Add(job.ClutHash);
            }

            if (writeIdx)
            {
                var indices = TextureTile.DecodeIndicesFrom(job.Window, r);
                var max = r.Bpp == 4 ? 15 : 255;
                var scaled = new byte[indices.Length];
                for (var i = 0; i < indices.Length; i++) scaled[i] = (byte)(indices[i] * 255 / max);
                PngWriter.WriteGray(Path.Combine(_root, $"{job.IndexHash:x16}.idx.png"), scaled, r.W, r.H);
            }

            if (writeClut)
            {
                var clutBase = Path.Combine(_clutRoot, $"{job.ClutHash:x16}");
                PngWriter.WriteRgba(clutBase + ".clut.png", TextureTile.DecodeClutFrom(job.Clut), job.Clut.Length, 1);
                File.WriteAllBytes(clutBase + ".clut.bin", TextureTile.RawBytes(job.Clut));
            }
        }

        var json = $$"""
                     {
                       "indexHash": "{{job.IndexHash:x16}}",
                       "clutHash": "{{job.ClutHash:x16}}",
                       "bpp": {{r.Bpp}},
                       "width": {{r.W}},
                       "height": {{r.H}},
                       "texpage": {{job.TPage}},
                       "clut": {{job.ClutId}},
                       "u0": {{r.U0}},
                       "v0": {{r.V0}}
                     }
                     """;
        File.WriteAllText(baseName + ".json", json);
    }
}