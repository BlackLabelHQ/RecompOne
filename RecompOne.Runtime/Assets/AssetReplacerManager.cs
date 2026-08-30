using RecompOne.Runtime.Assets.Decoders;

namespace RecompOne.Runtime.Assets;

public sealed class XaEntry
{
    public byte? FileNumber;
    public byte? Channel;
    public int StartLba;
    public ulong PayloadHash;
    public string AudioName = "";
    public Func<byte[]?> Open = () => null;
    public XaOptions Options = new();
    public string PackId = "";
    public string? Note;
    public double Interleave = 1;
    public int TimesPlayed;

    public bool Matches(byte file, byte channel, int lba)
    {
        if (FileNumber.HasValue && FileNumber.Value != file) return false;
        if (Channel.HasValue && Channel.Value != channel) return false;
        return StartLba <= lba;
    }

    public override string ToString()
    {
        return $"{PackId}: f{FileNumber?.ToString() ?? "*"} c{Channel?.ToString() ?? "*"} @{StartLba} -> {AudioName}";
    }
}

public sealed class TextureAsset
{
    public ulong IndexHash;
    public ulong ClutHash;
    public TextureMode Mode = TextureMode.Rgba;
    public string FileName = "";
    public string PackId = "";
    public Func<byte[]?> Open = () => null;
    public ReplacementTexture? Loaded;
    public bool AspectChecked;
}

public sealed class TextureRule
{
    public int[] Texpages = [];
    public int Bpp;
    public int MinWidth, MinHeight;
    public int MaxWidth = int.MaxValue, MaxHeight = int.MaxValue;
    public TextureAsset Asset = null!;

    public bool Matches(int tpage, int bpp, int w, int h)
    {
        if (Bpp != 0 && Bpp != bpp) return false;
        if (w < MinWidth || h < MinHeight || w > MaxWidth || h > MaxHeight) return false;
        if (Texpages.Length == 0) return true;
        foreach (var tp in Texpages)
            if (tp == tpage)
                return true;
        return false;
    }
}

public sealed class ClutAsset
{
    public ulong ClutHash;
    public string FileName = "";
    public string PackId = "";
    public Func<byte[]?> Open = () => null;
    public ReplacementClut? Loaded;
}

public sealed class AssetStats
{
    public int XaRuns;
    public int XaReplaced;
    public int XaPassthrough;
    public int XaOpenFailures;
    public int TextureHits;
    public int TextureMisses;
    public int TextureLoadFailures;
}

public sealed class AssetReplacerManager
{
    private static readonly Lazy<AssetReplacerManager> _instance = new(() => new AssetReplacerManager());
    public static AssetReplacerManager Instance => _instance.Value;

    private readonly object _gate = new();
    private readonly List<AssetPack> _packs = [];
    private readonly List<XaEntry> _xa = [];
    private readonly List<XaEntry> _runtimeXa = [];
    private readonly Dictionary<ulong, TextureAsset> _texExact = [];
    private readonly Dictionary<ulong, TextureAsset> _texAny = [];
    private readonly Dictionary<ulong, ClutAsset> _cluts = [];
    private readonly List<TextureRule> _rules = [];

    private string _gameId = "UNKNOWN";
    private bool _gameIdResolved;

    public bool Enabled { get; set; } = true;
    public string Root { get; private set; } = "";
    public AssetStats Stats { get; } = new();

    public IReadOnlyList<AssetPack> Packs
    {
        get
        {
            lock (_gate)
            {
                return _packs.ToArray();
            }
        }
    }

    public IReadOnlyList<XaEntry> XaEntries
    {
        get
        {
            lock (_gate)
            {
                return _xa.Concat(_runtimeXa).ToArray();
            }
        }
    }

    public string GameId
    {
        get
        {
            if (!_gameIdResolved) ResolveGameId();
            return _gameId;
        }
    }

    public void ResolveGameId()
    {
        _gameIdResolved = true;
        _gameId = "UNKNOWN";
        try
        {
            var cd = Runtime.Cd;
            if (cd == null) return;
            var bytes = cd.Fs.ReadFile("SYSTEM.CNF");
            var text = System.Text.Encoding.ASCII.GetString(bytes);
            var i = text.IndexOf("cdrom", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return;
            var end = text.IndexOfAny(['\r', '\n'], i);
            var boot = end < 0 ? text[i..] : text[i..end];
            var slash = boot.LastIndexOfAny(['\\', '/', ':']);
            var name = slash >= 0 ? boot[(slash + 1)..] : boot;
            var semi = name.IndexOf(';');
            if (semi >= 0) name = name[..semi];
            name = name.Trim().ToUpperInvariant().Replace("_", "-").Replace(".", "");
            if (name.Length > 0) _gameId = name;
        }
        catch
        {
        }
    }

    public void LoadAll(string? root = null)
    {
        root ??= Path.GetFullPath("packs");
        Root = root;
        try
        {
            Directory.CreateDirectory(root);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[assets] cannot create '{root}': {ex.Message}");
        }

        ResolveGameId();

        var found = new List<AssetPack>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                if (Path.GetFileName(dir).StartsWith('.')) continue;
                var pack = AssetPack.Open(dir, out var err);
                if (pack != null) found.Add(pack);
                else if (err != "pack.json not found")
                    Console.Error.WriteLine($"[assets] {Path.GetFileName(dir)}: {err}");
            }

            foreach (var zip in Directory.EnumerateFiles(root, "*.zip"))
            {
                var pack = AssetPack.Open(zip, out var err);
                if (pack != null) found.Add(pack);
                else Console.Error.WriteLine($"[assets] {Path.GetFileName(zip)}: {err}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[assets] discovery failed: {ex.Message}");
        }

        found.Sort((a, b) =>
        {
            var p = a.Manifest.Priority.CompareTo(b.Manifest.Priority);
            return p != 0 ? p : string.CompareOrdinal(a.Id, b.Id);
        });

        lock (_gate)
        {
            _packs.Clear();
            _xa.Clear();
            foreach (var pack in found)
            {
                if (!pack.TargetsGame(_gameId))
                {
                    pack.LoadError = $"targets another game (disc is {_gameId})";
                    pack.Enabled = false;
                    _packs.Add(pack);
                    Console.WriteLine($"[assets] '{pack.Id}' disabled: {pack.LoadError}");
                    continue;
                }

                _packs.Add(pack);
                IndexXa(pack);
                IndexTextures(pack);
            }

            RefreshHasTextures();
        }

        Console.WriteLine($"[assets] game={_gameId} packs={found.Count} xa={_xa.Count} " +
                          $"tex={_texExact.Count + _texAny.Count} cluts={_cluts.Count} rules={_rules.Count} root={root}");
    }

    public void Reload()
    {
        lock (_gate)
        {
            _packs.Clear();
            _xa.Clear();
            _texExact.Clear();
            _texAny.Clear();
            _cluts.Clear();
            _rules.Clear();
            RefreshHasTextures();
        }

        Textures.TextureResolver.Invalidate();
        Xa.XaRouter.Reset();
        LoadAll(string.IsNullOrEmpty(Root) ? null : Root);
    }

    private void IndexXa(AssetPack pack)
    {
        var list = pack.Manifest.Xa;
        if (list == null) return;

        var defaults = pack.Manifest.Defaults?.Xa;
        foreach (var dto in list)
        {
            if (string.IsNullOrWhiteSpace(dto.Audio))
            {
                Console.Error.WriteLine($"[assets] {pack.Id}: xa entry without 'audio' ignored");
                continue;
            }

            var entry = new XaEntry
            {
                PackId = pack.Id,
                AudioName = dto.Audio!,
                Note = dto.Note,
                Options = BuildOptions(dto, defaults)
            };

            if (dto.FileNumber.HasValue) entry.FileNumber = (byte)dto.FileNumber.Value;
            if (dto.Channel.HasValue) entry.Channel = (byte)dto.Channel.Value;
            entry.StartLba = dto.StartLba ?? 0;

            if (entry.StartLba == 0 && !string.IsNullOrWhiteSpace(dto.File))
                try
                {
                    if (Runtime.Cd?.Fs.Locate(dto.File!, out var lba, out _) == true)
                        entry.StartLba = lba;
                }
                catch
                {
                }

            if (!string.IsNullOrWhiteSpace(dto.Hash) && AssetHash.TryParseHex(dto.Hash, out var h))
                entry.PayloadHash = h;

            var audio = dto.Audio!;
            var owner = pack;
            entry.Open = () => owner.ReadAsset(audio);

            _xa.Add(entry);
        }

        _xa.Sort((a, b) => b.StartLba.CompareTo(a.StartLba));
    }

    private void IndexTextures(AssetPack pack)
    {
        foreach (var file in pack.ListAssets("textures"))
        {
            var name = Path.GetFileName(file);
            if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;

            var stem = name[..^4];
            var mode = TextureMode.Rgba;
            if (stem.EndsWith(".idx", StringComparison.OrdinalIgnoreCase))
            {
                stem = stem[..^4];
                mode = TextureMode.Indexed;
            }

            var parts = stem.Split('_');
            if (!AssetHash.TryParseHex(parts[0], out var index)) continue;
            ulong clutHash = 0;
            if (parts.Length > 1 && !AssetHash.TryParseHex(parts[1], out clutHash)) continue;

            AddTexture(pack, file, index, clutHash, mode);
        }

        foreach (var file in pack.ListAssets("cluts"))
        {
            var name = Path.GetFileName(file);
            if (!name.EndsWith(".clut.png", StringComparison.OrdinalIgnoreCase)) continue;
            if (!AssetHash.TryParseHex(name[..^9], out var clutHash)) continue;
            AddClut(pack, file, clutHash);
        }

        if (pack.Manifest.Textures != null)
            foreach (var dto in pack.Manifest.Textures)
            {
                if (string.IsNullOrWhiteSpace(dto.File)) continue;
                if (!AssetHash.TryParseHex(dto.Index ?? dto.Hash, out var index)) continue;

                ulong clutHash = 0;
                if (!string.IsNullOrWhiteSpace(dto.Clut) &&
                    !dto.Clut!.Equals("any", StringComparison.OrdinalIgnoreCase))
                    AssetHash.TryParseHex(dto.Clut, out clutHash);

                var mode = dto.Mode?.ToLowerInvariant() == "indexed" ? TextureMode.Indexed : TextureMode.Rgba;
                AddTexture(pack, dto.File!, index, clutHash, mode);
            }

        if (pack.Manifest.TextureRules != null)
            foreach (var dto in pack.Manifest.TextureRules)
            {
                if (string.IsNullOrWhiteSpace(dto.File)) continue;
                var file = dto.File!;
                _rules.Add(new TextureRule
                {
                    Texpages = dto.Texpages?.ToArray() ?? [],
                    Bpp = dto.Bpp ?? 0,
                    MinWidth = dto.MinWidth ?? 0,
                    MinHeight = dto.MinHeight ?? 0,
                    MaxWidth = dto.MaxWidth ?? int.MaxValue,
                    MaxHeight = dto.MaxHeight ?? int.MaxValue,
                    Asset = new TextureAsset
                    {
                        Mode = TextureMode.Rgba,
                        FileName = file,
                        PackId = pack.Id,
                        Open = () => pack.ReadAsset(file)
                    }
                });
            }

        if (pack.Manifest.Cluts != null)
            foreach (var dto in pack.Manifest.Cluts)
            {
                if (string.IsNullOrWhiteSpace(dto.File)) continue;
                if (!AssetHash.TryParseHex(dto.Clut ?? dto.Hash, out var clutHash)) continue;
                AddClut(pack, dto.File!, clutHash);
            }
    }

    private void AddTexture(AssetPack pack, string file, ulong index, ulong clutHash, TextureMode mode)
    {
        var asset = new TextureAsset
        {
            IndexHash = index,
            ClutHash = clutHash,
            Mode = mode,
            FileName = file,
            PackId = pack.Id,
            Open = () => pack.ReadAsset(file)
        };

        if (clutHash == 0) _texAny[index] = asset;
        else _texExact[Combine(index, clutHash)] = asset;
    }

    private void AddClut(AssetPack pack, string file, ulong clutHash)
    {
        _cluts[clutHash] = new ClutAsset
        {
            ClutHash = clutHash,
            FileName = file,
            PackId = pack.Id,
            Open = () => pack.ReadAsset(file)
        };
    }

    private static ulong Combine(ulong a, ulong b)
    {
        return (a * 1099511628211UL) ^ b;
    }

    private volatile bool _hasTextures;
    public bool HasTextures => _hasTextures;

    private void RefreshHasTextures()
    {
        _hasTextures = _texExact.Count > 0 || _texAny.Count > 0 || _cluts.Count > 0 || _rules.Count > 0;
    }

    public bool HasRules
    {
        get
        {
            lock (_gate)
            {
                return _rules.Count > 0;
            }
        }
    }

    public TextureAsset? MatchRule(int tpage, int bpp, int w, int h)
    {
        if (!Enabled) return null;
        lock (_gate)
        {
            foreach (var rule in _rules)
                if (rule.Matches(tpage, bpp, w, h))
                    return rule.Asset;
            return null;
        }
    }

    public TextureAsset? ResolveTexture(ulong indexHash, ulong clutHash)
    {
        if (!Enabled) return null;
        lock (_gate)
        {
            if (_texExact.TryGetValue(Combine(indexHash, clutHash), out var exact)) return exact;
            return _texAny.GetValueOrDefault(indexHash);
        }
    }

    public ClutAsset? ResolveClut(ulong clutHash)
    {
        if (!Enabled || clutHash == 0) return null;
        lock (_gate)
        {
            return _cluts.GetValueOrDefault(clutHash);
        }
    }

    public ReplacementTexture? LoadTexture(TextureAsset asset)
    {
        if (asset.Loaded != null) return asset.Loaded.Failed ? null : asset.Loaded;

        var loaded = new ReplacementTexture { Mode = asset.Mode };
        asset.Loaded = loaded;
        try
        {
            var data = asset.Open();
            if (data == null) throw new FileNotFoundException(asset.FileName);

            var img = StbImageSharp.ImageResult.FromMemory(data, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            if (img.Width <= 0 || img.Height <= 0) throw new InvalidDataException("empty image");

            loaded.Width = img.Width;
            loaded.Height = img.Height;
            loaded.Rgba = img.Data;
            return loaded;
        }
        catch (Exception ex)
        {
            loaded.Failed = true;
            Stats.TextureLoadFailures++;
            Console.Error.WriteLine($"[assets] {asset.PackId}: cannot load '{asset.FileName}': {ex.Message}");
            return null;
        }
    }

    public ReplacementClut? LoadClut(ClutAsset asset)
    {
        if (asset.Loaded != null) return asset.Loaded.Failed ? null : asset.Loaded;

        var loaded = new ReplacementClut();
        asset.Loaded = loaded;
        try
        {
            var data = asset.Open();
            if (data == null) throw new FileNotFoundException(asset.FileName);

            var img = StbImageSharp.ImageResult.FromMemory(data, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            var count = img.Width * img.Height;
            if (count != 16 && count != 256)
                throw new InvalidDataException($"palette must hold 16 or 256 entries, got {count}");

            loaded.Count = count;
            loaded.Rgba = img.Data;
            return loaded;
        }
        catch (Exception ex)
        {
            loaded.Failed = true;
            Stats.TextureLoadFailures++;
            Console.Error.WriteLine($"[assets] {asset.PackId}: cannot load palette '{asset.FileName}': {ex.Message}");
            return null;
        }
    }

    private static XaOptions BuildOptions(XaEntryDto dto, XaEntryDto? defaults)
    {
        var o = new XaOptions();
        Apply(defaults);
        Apply(dto);
        return o;

        void Apply(XaEntryDto? d)
        {
            if (d == null) return;
            if (d.Gain.HasValue) o.Gain = d.Gain.Value;
            if (d.Loop.HasValue) o.Loop = d.Loop.Value;
            if (d.LoopStart.HasValue) o.LoopStart = d.LoopStart.Value;
            if (d.LoopEnd.HasValue) o.LoopEnd = d.LoopEnd.Value;
            if (d.Extend.HasValue) o.Extend = d.Extend.Value;
            if (d.ExtendMaxMs.HasValue) o.ExtendMaxMs = d.ExtendMaxMs.Value;
            if (d.TailMs.HasValue) o.TailMs = d.TailMs.Value;
            if (d.AllowStr.HasValue) o.AllowStr = d.AllowStr.Value;
            if (!string.IsNullOrWhiteSpace(d.OnShorter))
                o.OnShorter = d.OnShorter!.ToLowerInvariant() switch
                {
                    "loop" => ShortPolicy.Loop,
                    "endstream" or "end" => ShortPolicy.EndStream,
                    _ => ShortPolicy.Silence
                };
        }
    }

    public XaEntry? ResolveXa(byte file, byte channel, int lba)
    {
        if (!Enabled) return null;
        lock (_gate)
        {
            XaEntry? best = null;
            foreach (var e in _runtimeXa)
                if (e.Matches(file, channel, lba) && (best == null || e.StartLba > best.StartLba))
                    best = e;
            if (best != null) return best;
            foreach (var e in _xa)
                if (e.Matches(file, channel, lba) && (best == null || e.StartLba > best.StartLba))
                    best = e;
            return best;
        }
    }

    public XaEntry? ResolveXaByPayload(ulong payloadHash)
    {
        if (!Enabled || payloadHash == 0) return null;
        lock (_gate)
        {
            foreach (var e in _runtimeXa)
                if (e.PayloadHash == payloadHash)
                    return e;
            foreach (var e in _xa)
                if (e.PayloadHash == payloadHash)
                    return e;
            return null;
        }
    }

    public bool TryGetXaStream(in XaKey key, out ReplacementStream stream)
    {
        stream = null!;
        var entry = ResolveXa(key.File, key.Channel, key.StartLba);
        if (entry == null) return false;
        var opened = OpenStream(entry);
        if (opened == null) return false;
        stream = opened;
        return true;
    }

    public ReplacementStream? OpenStream(XaEntry entry)
    {
        try
        {
            var data = entry.Open();
            if (data == null || data.Length == 0)
            {
                Stats.XaOpenFailures++;
                Console.Error.WriteLine($"[assets] {entry.PackId}: '{entry.AudioName}' is missing or empty");
                return null;
            }

            var decoder = PcmDecoderFactory.Open(entry.AudioName, data);
            return new ReplacementStream(entry.AudioName, decoder, entry.Options.Clone());
        }
        catch (Exception ex)
        {
            Stats.XaOpenFailures++;
            Console.Error.WriteLine($"[assets] {entry.PackId}: cannot decode '{entry.AudioName}': {ex.Message}");
            return null;
        }
    }

    public XaEntry RegisterXa(byte? file, byte? channel, int startLba, string audioPath, XaOptions? options = null,
        string owner = "mod")
    {
        var entry = new XaEntry
        {
            FileNumber = file,
            Channel = channel,
            StartLba = startLba,
            AudioName = audioPath,
            Options = options ?? new XaOptions(),
            PackId = owner,
            Open = () => File.Exists(audioPath) ? File.ReadAllBytes(audioPath) : null
        };
        lock (_gate)
        {
            _runtimeXa.Add(entry);
        }

        return entry;
    }

    public XaEntry RegisterXa(byte? file, byte? channel, int startLba, string name, Func<byte[]?> open,
        XaOptions? options = null, string owner = "mod")
    {
        var entry = new XaEntry
        {
            FileNumber = file,
            Channel = channel,
            StartLba = startLba,
            AudioName = name,
            Options = options ?? new XaOptions(),
            PackId = owner,
            Open = open
        };
        lock (_gate)
        {
            _runtimeXa.Add(entry);
        }

        return entry;
    }

    public void UnregisterXa(XaEntry entry)
    {
        lock (_gate)
        {
            _runtimeXa.Remove(entry);
        }
    }

    public void ClearRuntimeRegistrations(string owner)
    {
        lock (_gate)
        {
            _runtimeXa.RemoveAll(e => e.PackId == owner);
        }
    }
}