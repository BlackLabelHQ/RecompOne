using System.IO.Compression;
using System.Text.Json;

namespace RecompOne.Runtime.Assets;

public sealed class AssetPack : IDisposable
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly string _root;
    private readonly bool _isZip;

    public PackManifest Manifest { get; }
    public string Path => _root;
    public string Id => Manifest.Id;
    public string DisplayName => string.IsNullOrWhiteSpace(Manifest.Name) ? Manifest.Id : Manifest.Name!;
    public bool Enabled { get; set; } = true;
    public string? LoadError { get; internal set; }

    private AssetPack(string root, bool isZip, PackManifest manifest)
    {
        _root = root;
        _isZip = isZip;
        Manifest = manifest;
    }

    public static AssetPack? Open(string path, out string? error)
    {
        error = null;
        try
        {
            var isZip = File.Exists(path) &&
                        System.IO.Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase);

            var manifestBytes = isZip ? ReadFromZip(path, "pack.json") : ReadFromFolder(path, "pack.json");
            if (manifestBytes == null)
            {
                error = "pack.json not found";
                return null;
            }

            var manifest = JsonSerializer.Deserialize<PackManifest>(manifestBytes, Json);
            if (manifest == null)
            {
                error = "pack.json is empty or invalid";
                return null;
            }

            if (string.IsNullOrWhiteSpace(manifest.Id))
            {
                error = "pack.json has no 'id'";
                return null;
            }

            if (manifest.FormatVersion > 1)
            {
                error = $"pack format {manifest.FormatVersion} is newer than this runtime supports (1)";
                return null;
            }

            return new AssetPack(path, isZip, manifest);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static byte[]? ReadFromFolder(string root, string rel)
    {
        var full = SafeCombine(root, rel);
        return File.Exists(full) ? File.ReadAllBytes(full) : null;
    }

    private static byte[]? ReadFromZip(string zipPath, string rel)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry(rel) ?? zip.Entries.FirstOrDefault(e =>
            e.FullName.Replace('\\', '/').Equals(rel, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return null;
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static string SafeCombine(string root, string rel)
    {
        rel = rel.Replace('\\', '/').TrimStart('/');
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, rel));
        var rootFull = System.IO.Path.GetFullPath(root);
        if (!full.StartsWith(rootFull, StringComparison.Ordinal))
            throw new UnauthorizedAccessException($"path escapes the pack: {rel}");
        return full;
    }

    public byte[]? ReadAsset(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var rel = relativePath.Replace('\\', '/').TrimStart('/');
        if (rel.Contains("..", StringComparison.Ordinal)) return null;
        try
        {
            return _isZip ? ReadFromZip(_root, rel) : ReadFromFolder(_root, rel);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[assets] {Id}: cant read '{rel}': {ex.Message}");
            return null;
        }
    }

    public IEnumerable<string> ListAssets(string directory)
    {
        var prefix = directory.Replace('\\', '/').Trim('/') + "/";
        if (_isZip)
        {
            ZipArchive zip;
            try
            {
                zip = ZipFile.OpenRead(_root);
            }
            catch
            {
                yield break;
            }

            using (zip)
            {
                foreach (var e in zip.Entries)
                {
                    var name = e.FullName.Replace('\\', '/');
                    if (e.Length > 0 && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        yield return name;
                }
            }

            yield break;
        }

        string full;
        try
        {
            full = SafeCombine(_root, directory);
        }
        catch
        {
            yield break;
        }

        if (!Directory.Exists(full)) yield break;

        foreach (var file in Directory.EnumerateFiles(full))
            yield return prefix + System.IO.Path.GetFileName(file);
    }

    public bool TargetsGame(string gameId)
    {
        var g = Manifest.Game;
        if (g == null) return true;
        var ids = g.All().ToArray();
        if (ids.Length == 0) return true;
        foreach (var id in ids)
            if (id.Equals(gameId, StringComparison.OrdinalIgnoreCase))
                return true;
        return !g.Strict;
    }

    public void Dispose()
    {
    }
}