using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RecompOne.Runtime.Assets.Sfx;
using RecompOne.Runtime.Assets.Textures;
using RecompOne.Runtime.Assets.Xa;

namespace RecompOne.Runtime.Assets;

public static class PackAuthoring
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static AssetReplacerManager Manager => AssetReplacerManager.Instance;

    public static string Root
    {
        get
        {
            var root = Manager.Root;
            if (string.IsNullOrEmpty(root)) root = Path.GetFullPath("packs");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    public static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.Trim().ToLowerInvariant())
            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-') sb.Append(c);
            else if (char.IsWhiteSpace(c) && sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        var slug = sb.ToString().Trim('-', '.');
        if (slug.Length > 64) slug = slug[..64];
        return slug.Length == 0 ? "pack" : slug;
    }

    private static string UniquePath(string root, string stem, string extension = "")
    {
        var path = Path.Combine(root, stem + extension);
        for (var n = 2; Directory.Exists(path) || File.Exists(path); n++)
            path = Path.Combine(root, $"{stem}-{n}{extension}");
        return path;
    }

    public static string? Create(string name, string author, string description, out string? error)
    {
        error = null;
        try
        {
            var root = Root;
            var folder = UniquePath(root, Slug(name));
            var id = Path.GetFileName(folder);
            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(Path.Combine(folder, "textures"));
            Directory.CreateDirectory(Path.Combine(folder, "sfx"));
            Directory.CreateDirectory(Path.Combine(folder, "xa"));

            var manifest = new PackManifest
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(name) ? id : name.Trim(),
                Author = string.IsNullOrWhiteSpace(author) ? null : author.Trim(),
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                Version = "1.0"
            };
            WriteManifest(folder, manifest);

            Manager.PutOnTop(id);
            Manager.Reload();
            return id;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    public static void WriteManifest(string folder, PackManifest manifest)
    {
        File.WriteAllText(Path.Combine(folder, "pack.json"), JsonSerializer.Serialize(manifest, Json));
    }

    public static void Save(AssetPack pack)
    {
        if (!pack.Editable) return;
        WriteManifest(pack.Path, pack.Manifest);
        Manager.RequestReload(150);
    }

    private static string Full(AssetPack pack, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(pack.Path, relative));
        if (!full.StartsWith(Path.GetFullPath(pack.Path), StringComparison.Ordinal))
            throw new UnauthorizedAccessException($"path escapes the pack: {relative}");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        return full;
    }

    public static string TexturePath(ulong index, ulong clut, bool paletteSpecific)
    {
        return paletteSpecific ? $"textures/{index:x16}_{clut:x16}.png" : $"textures/{index:x16}.png";
    }

    public static string SetTexture(AssetPack pack, ulong index, ulong clut, bool paletteSpecific, string source)
    {
        var img = StbImageSharp.ImageResult.FromMemory(File.ReadAllBytes(source),
            StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        var relative = TexturePath(index, clut, paletteSpecific);
        PngWriter.WriteRgba(Full(pack, relative), img.Data, img.Width, img.Height);
        Manager.RequestReload(150);
        return relative;
    }

    public static string WriteOriginal(AssetPack pack, SeenTexture texture, bool paletteSpecific)
    {
        var relative = TexturePath(texture.IndexHash, texture.ClutHash, paletteSpecific);
        var full = Full(pack, relative);
        if (!File.Exists(full)) PngWriter.WriteRgba(full, texture.Thumb, texture.W, texture.H);
        Manager.RequestReload(150);
        return full;
    }

    public static void ExportOriginal(SeenTexture texture, string path)
    {
        PngWriter.WriteRgba(path, texture.Thumb, texture.W, texture.H);
    }

    public static void RemoveFile(AssetPack pack, string relative)
    {
        var full = Full(pack, relative);
        if (File.Exists(full)) File.Delete(full);
        pack.Manifest.Textures?.RemoveAll(t => t.File == relative);
        pack.Manifest.Sfx?.RemoveAll(t => t.File == relative);
        pack.Manifest.Xa?.RemoveAll(t => t.Audio == relative);
        Save(pack);
    }

    public static string SetSound(AssetPack pack, ulong hash, string source)
    {
        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension is not (".wav" or ".ogg"))
            throw new NotSupportedException("sounds must be .wav or .ogg");

        foreach (var other in new[] { ".wav", ".ogg" })
        {
            var stale = Full(pack, $"sfx/{hash:x16}{other}");
            if (File.Exists(stale)) File.Delete(stale);
        }

        var relative = $"sfx/{hash:x16}{extension}";
        File.Copy(source, Full(pack, relative), true);
        Manager.RequestReload(150);
        return relative;
    }

    public static SfxEntryDto SoundOptions(AssetPack pack, ulong hash)
    {
        pack.Manifest.Sfx ??= [];
        var key = $"{hash:x16}";
        var entry = pack.Manifest.Sfx.FirstOrDefault(e => AssetHash.TryParseHex(e.Hash, out var h) && h == hash);
        if (entry != null) return entry;
        entry = new SfxEntryDto { Hash = key };
        pack.Manifest.Sfx.Add(entry);
        return entry;
    }

    public static void RemoveSound(AssetPack pack, PackContents.Sound sound)
    {
        var full = Full(pack, sound.File);
        if (File.Exists(full)) File.Delete(full);
        pack.Manifest.Sfx?.RemoveAll(e => AssetHash.TryParseHex(e.Hash, out var h) && h == sound.Hash);
        Save(pack);
    }

    public static void ExportOriginal(SeenSample sample, string path)
    {
        WavWriter.Write(path, SpuAdpcm.Decode(sample.Adpcm), 1, sample.NaturalRate);
    }

    public static XaEntryDto SetMusic(AssetPack pack, SeenStream stream, string source)
    {
        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension is not (".wav" or ".ogg"))
            throw new NotSupportedException("music must be .wav or .ogg");

        var entry = MusicEntry(pack, stream.File, stream.Channel, stream.StartLba);
        if (!string.IsNullOrWhiteSpace(entry.Audio))
        {
            var old = Full(pack, entry.Audio!);
            if (File.Exists(old)) File.Delete(old);
        }

        var relative = $"xa/f{stream.File}_c{stream.Channel}_{stream.StartLba}{extension}";
        File.Copy(source, Full(pack, relative), true);
        entry.Audio = relative;
        Save(pack);
        return entry;
    }

    public static XaEntryDto MusicEntry(AssetPack pack, int file, int channel, int startLba)
    {
        pack.Manifest.Xa ??= [];
        var entry = pack.Manifest.Xa.FirstOrDefault(e =>
            e.FileNumber == file && e.Channel == channel && e.StartLba == startLba);
        if (entry != null) return entry;
        entry = new XaEntryDto { FileNumber = file, Channel = channel, StartLba = startLba };
        pack.Manifest.Xa.Add(entry);
        return entry;
    }

    public static void RemoveMusic(AssetPack pack, XaEntryDto entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Audio))
        {
            var full = Full(pack, entry.Audio!);
            if (File.Exists(full)) File.Delete(full);
        }

        pack.Manifest.Xa?.Remove(entry);
        Save(pack);
    }

    public static void SetIcon(AssetPack pack, string source)
    {
        var img = StbImageSharp.ImageResult.FromMemory(File.ReadAllBytes(source),
            StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        PngWriter.WriteRgba(Full(pack, "pack-icon.png"), img.Data, img.Width, img.Height);
        pack.Manifest.Preview = null;
        Save(pack);
    }

    public static void ExportZip(AssetPack pack, string destination)
    {
        if (File.Exists(destination)) File.Delete(destination);
        if (pack.IsZip) File.Copy(pack.Path, destination);
        else ZipFile.CreateFromDirectory(pack.Path, destination, CompressionLevel.Optimal, false);
    }

    public static string? Import(string source, out string? error)
    {
        error = null;
        try
        {
            var root = Root;
            if (File.Exists(source) && source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var target = UniquePath(root, Path.GetFileNameWithoutExtension(source), ".zip");
                File.Copy(source, target);
                var pack = AssetPack.Open(target, out error);
                if (pack == null)
                {
                    File.Delete(target);
                    return null;
                }

                Manager.PutOnTop(pack.Id);
                Manager.Reload();
                return pack.Id;
            }

            if (Directory.Exists(source) && File.Exists(Path.Combine(source, "pack.json")))
            {
                var target = UniquePath(root, Path.GetFileName(Path.TrimEndingDirectorySeparator(source)));
                CopyDirectory(source, target);
                var pack = AssetPack.Open(target, out error);
                if (pack == null) return null;
                Manager.PutOnTop(pack.Id);
                Manager.Reload();
                return pack.Id;
            }

            error = "pick a .zip pack or a folder that has a pack.json";
            return null;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    public static string? Unpack(AssetPack pack, out string? error)
    {
        error = null;
        if (!pack.IsZip) return pack.Id;
        try
        {
            var target = UniquePath(Root, Slug(pack.Id));
            ZipFile.ExtractToDirectory(pack.Path, target);
            File.Move(pack.Path, UniquePath(Path.GetDirectoryName(pack.Path)!, Path.GetFileName(pack.Path), ".bak"));
            Manager.Reload();
            return pack.Id;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    public static void Delete(AssetPack pack)
    {
        if (pack.IsZip) File.Delete(pack.Path);
        else Directory.Delete(pack.Path, true);
        Manager.Reload();
    }

    public static void OpenExternal(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[assets] cannot open '{path}': {ex.Message}");
        }
    }

    public static string FullPath(AssetPack pack, string relative)
    {
        return Full(pack, relative);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), true);
    }
}
