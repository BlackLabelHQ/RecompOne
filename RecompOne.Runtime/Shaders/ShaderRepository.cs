using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using RecompOne.Runtime.Hle;

namespace RecompOne.Runtime.Shaders;

public sealed record CatalogEntry(string Path, string Name, string Category, string Folder);

public sealed record ShaderInstallResult(string Name, string? Error);

public sealed class ShaderRepository
{
    private const int Downloads = 6;
    private const string CatalogFolder = ".catalog";

    private static readonly HttpClient Http = CreateClient();
    private static readonly string[] Binary = [".png", ".jpg", ".jpeg", ".bmp", ".tga", ".gif"];
    private static readonly string[] Text = [".slangp", ".params", ".slang", ".h", ".inc", ".glsl", ".hlsl", ".fx", ".fxh"];

    private readonly ShaderLibrary library;
    private readonly ConcurrentDictionary<string, string> installing = new(StringComparer.Ordinal);
    private CancellationTokenSource cancel = new();
    private IReadOnlyList<CatalogEntry> entries = [];
    private IReadOnlyList<string> categories = [];
    private volatile bool busy;
    private int generation;

    public ShaderRepository(ShaderLibrary library)
    {
        this.library = library;
    }

    public string Owner { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string Branch { get; private set; } = "";
    public bool Valid { get; private set; }
    public bool Busy => busy;
    public bool Truncated { get; private set; }
    public DateTime? Fetched { get; private set; }
    public string? Error { get; private set; }
    public ShaderInstallResult? LastInstall { get; private set; }
    public int Version { get; private set; }
    public IReadOnlyList<CatalogEntry> Entries => entries;
    public IReadOnlyList<string> Categories => categories;

    public string Label => Valid ? $"{Owner}/{Name}" : "";

    public static bool TryParse(string text, out string owner, out string name)
    {
        owner = name = "";
        var value = text.Trim();

        foreach (var prefix in new[] { "https://", "http://" })
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))

                value = value[prefix.Length..];
        if (value.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) value = value[4..];
        if (value.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase)) value = value[11..];
        else if (value.Contains('.')) return false;

        var parts = value.Trim('/').Split('/');

        if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0) return false;
        
        owner = parts[0];
        name = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
        return name.Length > 0;
    }

    public void Configure(string url, string branch)
    {
        Cancel();
        Valid = TryParse(url, out var owner, out var name);
        Owner = owner;
        Name = name;
        Branch = branch;
        Error = null;
        LastInstall = null;
        Fetched = null;
        Truncated = false;
        Publish([]);
        if (Valid) LoadCache();
    }

    public void Cancel()
    {
        cancel.Cancel();
        cancel = new CancellationTokenSource();
        installing.Clear();
        busy = false;
        generation++;
    }

    public void EnsureLoaded()
    {
        if (Valid && entries.Count == 0 && !busy && Error == null) Refresh();
    }

    public void Refresh()
    {
        if (!Valid || busy) return;
        busy = true;
        Error = null;
        var token = cancel.Token;
        var expected = generation;
        Task.Run(async () =>
        {
            try
            {
                var found = await FetchCatalog(token);
                if (expected != generation) return;
                Publish(found);
                Fetched = DateTime.Now;
                SaveCache(found);
            }
            catch (Exception e) when (!token.IsCancellationRequested)
            {
                if (expected == generation) Error = e.Message;
            }
            finally
            {
                if (expected == generation) busy = false;
            }
        }, token);
    }

    public string LocalRelative(CatalogEntry entry)
    {
        return $"{Owner}/{Name}/{entry.Path}";
    }

    public bool IsInstalled(CatalogEntry entry)
    {
        return library.Contains(LocalRelative(entry));
    }

    public string? Progress(CatalogEntry entry)
    {
        return installing.TryGetValue(entry.Path, out var progress) ? progress : null;
    }

    public void Install(CatalogEntry entry)
    {
        if (!Valid || !Allowed(entry.Path) || !installing.TryAdd(entry.Path, "0")) return;
        var token = cancel.Token;
        Task.Run(async () =>
        {
            try
            {
                await InstallAsync(entry, token);
                library.Refresh();
                LastInstall = new ShaderInstallResult(entry.Name, null);
            }
            catch (Exception e) when (!token.IsCancellationRequested)
            {
                LastInstall = new ShaderInstallResult(entry.Name, e.Message);
                Console.Error.WriteLine($"[Shaders] {entry.Name}: {e.Message}");
            }
            finally
            {
                installing.TryRemove(entry.Path, out _);
            }
        }, token);
    }

    private async Task InstallAsync(CatalogEntry entry, CancellationToken token)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var requested = new HashSet<string>(StringComparer.Ordinal);
        var pending = new List<string> { entry.Path };
        using var gate = new SemaphoreSlim(Downloads);

        while (pending.Count > 0)
        {
            var batch = pending.Where(path => !files.ContainsKey(path) && requested.Add(path)).Distinct(StringComparer.Ordinal).ToList();
            pending = [];

            var results = await Task.WhenAll(batch.Select(async path =>
            {
                await gate.WaitAsync(token);
                try
                {
                    return (Path: path, Data: await Download(path, path == entry.Path, token));
                }
                finally
                {
                    gate.Release();
                }
            }));

            foreach (var (path, data) in results)
            {
                if (data == null) continue;
                files[path] = data;
                installing[entry.Path] = files.Count.ToString();
                if (Binary.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant())) continue;

                var dir = path.Contains('/') ? path[..path.LastIndexOf('/')] : "";
                foreach (var reference in PostFx.References(path, Encoding.UTF8.GetString(data)))
                    if (Combine(dir, reference) is { } resolved && !files.ContainsKey(resolved) && Allowed(resolved))
                        pending.Add(resolved);
            }
        }

        var root = System.IO.Path.Combine(library.Root, Owner, Name);
        foreach (var (path, data) in files.OrderBy(f => f.Key == entry.Path))
        {
            token.ThrowIfCancellationRequested();
            var local = System.IO.Path.Combine(root, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(local)!);
            await File.WriteAllBytesAsync(local, data, token);
        }
    }

    private static bool Allowed(string path)
    {
        var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (Binary.Contains(extension) || Text.Contains(extension)) return true;
        Console.Error.WriteLine($"[Shaders] {path} is not a shader file, skipping");
        return false;
    }

    private static string? Combine(string dir, string reference)
    {
        var parts = new List<string>();
        if (dir.Length > 0) parts.AddRange(dir.Split('/'));
        foreach (var part in reference.Replace('\\', '/').Split('/'))
        {
            if (part.Length == 0 || part == ".") continue;
            if (part == "..")
            {
                if (parts.Count == 0) return null;
                parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(part);
        }

        return parts.Count == 0 ? null : string.Join('/', parts);
    }

    private async Task<byte[]?> Download(string path, bool required, CancellationToken token)
    {
        var escaped = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        var url = $"https://raw.githubusercontent.com/{Owner}/{Name}/{Ref()}/{escaped}";
        using var response = await Http.GetAsync(url, token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound && !required)
        {
            Console.Error.WriteLine($"[Shaders] {path} not found, skipping");
            return null;
        }

        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"{path}: {(int)response.StatusCode}");
        return await response.Content.ReadAsByteArrayAsync(token);
    }

    private async Task<List<CatalogEntry>> FetchCatalog(CancellationToken token)
    {
        var url = $"https://api.github.com/repos/{Owner}/{Name}/git/trees/{Uri.EscapeDataString(Ref())}?recursive=1";
        using var response = await Http.GetAsync(url, token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{Owner}/{Name}: {(int)response.StatusCode} {response.ReasonPhrase}");

        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        var root = document.RootElement;
        Truncated = root.TryGetProperty("truncated", out var truncated) && truncated.ValueKind == JsonValueKind.True;

        var found = new List<CatalogEntry>();
        foreach (var item in root.GetProperty("tree").EnumerateArray())
        {
            if (item.GetProperty("type").GetString() != "blob") continue;
            if (item.GetProperty("path").GetString() is not { } path) continue;
            if (!path.EndsWith(".slangp", StringComparison.OrdinalIgnoreCase)) continue;
            found.Add(Entry(path));
        }

        return found;
    }

    private static CatalogEntry Entry(string path)
    {
        var slash = path.LastIndexOf('/');
        var folder = slash < 0 ? "" : path[..slash];
        var category = slash < 0 ? "" : path[..path.IndexOf('/')];
        return new CatalogEntry(path, System.IO.Path.GetFileNameWithoutExtension(path), category, folder);
    }

    private void Publish(List<CatalogEntry> found)
    {
        found.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
        entries = found;
        categories = found.Select(e => e.Category).Where(c => c.Length > 0).Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
        Version++;
    }

    private string Ref()
    {
        return string.IsNullOrWhiteSpace(Branch) ? "HEAD" : Branch;
    }

    private string CachePath()
    {
        var safe = string.Join('_', new[] { Owner, Name, Ref() }.Select(part =>
            new string(part.Select(c => char.IsLetterOrDigit(c) || c is '-' or '.' ? c : '_').ToArray())));
        return System.IO.Path.Combine(library.Root, CatalogFolder, safe + ".json");
    }

    private void LoadCache()
    {
        try
        {
            var path = CachePath();
            if (!File.Exists(path)) return;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var found = root.GetProperty("presets").EnumerateArray().Select(p => p.GetString())
                .Where(p => !string.IsNullOrEmpty(p)).Select(p => Entry(p!)).ToList();
            Truncated = root.TryGetProperty("truncated", out var truncated) && truncated.GetBoolean();
            if (root.TryGetProperty("fetched", out var fetched) && fetched.TryGetDateTime(out var when)) Fetched = when;
            Publish(found);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[Shaders] failed to read the catalog cache: {e.Message}");
        }
    }

    private void SaveCache(List<CatalogEntry> found)
    {
        try
        {
            var path = CachePath();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                fetched = DateTime.Now,
                truncated = Truncated,
                presets = found.Select(e => e.Path)
            }));
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[Shaders] failed to save the catalog cache: {e.Message}");
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RecompOne-Shaders/1.0");
        return client;
    }
}
