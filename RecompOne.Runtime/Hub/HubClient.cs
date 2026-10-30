using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RecompOne.Runtime.Hub;

internal static class HubClient
{
    private static readonly HttpClient Http = Create();
    private static readonly TimeSpan StarsTtl = TimeSpan.FromHours(6);
    private static readonly string[] IconNames = ["mod-icon.png", "pack-icon.png", "icon.png", "icon.jpg", "icon.jpeg", "icon.ico"];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly object _starsGate = new();
    private static readonly HashSet<GitHost> _starsBlocked = [];
    private static JsonObject? _stars;

    public static string CacheRoot => Path.GetFullPath(".modhub");

    private static HttpClient Create()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RecompOne-ModHub/1.0");
        return client;
    }

    public static async Task<Dictionary<string, string>> FetchIndex(GitRemote hub, string branch)
    {
        var listing = Path.Combine(CacheRoot, "hub.json");
        var staging = Path.Combine(CacheRoot, "index");
        try
        {
            await Fetch(hub, branch, true, "mods", staging, null);
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in Directory.EnumerateFiles(staging))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith('_') || name.StartsWith('.')) continue;
                files[name] = await File.ReadAllTextAsync(file);
            }

            await File.WriteAllTextAsync(listing, JsonSerializer.Serialize(files));
            return files;
        }
        catch when (File.Exists(listing))
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(listing)) ?? [];
        }
        finally
        {
            Delete(staging);
        }
    }

    public static async Task Install(HubEntry entry, string directory, HubJob job)
    {
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, entry.Id);
        var staging = Path.Combine(directory, $".{entry.Id}.installing");
        try
        {
            await Fetch(entry.Remote, entry.Commit, false, entry.Path, staging, job);
            if (Directory.Exists(target))
            {
                if (!File.Exists(Path.Combine(target, ModHub.MarkerName)))
                    throw new IOException($"\"{target}\" already exists and was not installed by the mod hub");
                Delete(target);
            }

            Directory.Move(staging, target);
            ModHub.WriteMarker(target, entry);
            job.Progress = 1f;
        }
        finally
        {
            Delete(staging);
        }
    }

    private static async Task Fetch(GitRemote remote, string reference, bool branch, string path, string staging,
        HubJob? job)
    {
        Delete(staging);
        Directory.CreateDirectory(Path.GetDirectoryName(staging)!);
        var archive = remote.ArchiveUrl(reference, branch);
        if (archive != null)
        {
            using var data = await Download(archive, job);
            Directory.CreateDirectory(staging);
            Extract(data, path, staging);
            return;
        }

        var checkout = staging + ".git";
        try
        {
            await Checkout(remote, reference, branch, checkout);
            if (job != null) job.Progress = 0.9f;
            var source = path.Length > 0 ? Path.Combine(checkout, path) : checkout;
            if (!Directory.Exists(source)) throw new IOException($"\"{path}\" was not found in the repository");
            Copy(source, staging);
        }
        finally
        {
            Delete(checkout);
        }
    }

    private static async Task<MemoryStream> Download(string url, HubJob? job)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? 0;
        var data = new MemoryStream();
        await using var stream = await response.Content.ReadAsStreamAsync();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            data.Write(buffer, 0, read);
            if (job != null && total > 0) job.Progress = MathF.Min(0.95f, data.Length / (float)total);
        }

        data.Position = 0;
        return data;
    }

    private static void Extract(Stream data, string path, string staging)
    {
        var root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
        var extracted = 0;
        using var zip = new ZipArchive(data);
        foreach (var file in zip.Entries)
        {
            var slash = file.FullName.IndexOf('/');
            if (slash < 0 || file.FullName.EndsWith('/')) continue;
            var relative = file.FullName[(slash + 1)..];
            if (path.Length > 0)
            {
                if (!relative.StartsWith(path + "/", StringComparison.Ordinal)) continue;
                relative = relative[(path.Length + 1)..];
            }

            var destination = Path.GetFullPath(Path.Combine(staging, relative));
            if (!destination.StartsWith(root, StringComparison.Ordinal)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            file.ExtractToFile(destination, true);
            extracted++;
        }

        if (extracted == 0) throw new IOException(path.Length > 0 ? $"\"{path}\" was not found in the repository" : "the repository is empty");
    }

    private static async Task Checkout(GitRemote remote, string reference, bool branch, string target)
    {
        Delete(target);
        Directory.CreateDirectory(target);
        await Git(target, remote, "init", "-q");
        if (branch)
            await Git(target, remote, "fetch", "-q", "--depth", "1", remote.CloneUrl, reference);
        else if (!await TryGit(target, "fetch", "-q", "--depth", "1", remote.CloneUrl, reference))
            await Git(target, remote, "fetch", "-q", remote.CloneUrl, "+refs/heads/*:refs/remotes/hub/*");
        await Git(target, remote, "-c", "advice.detachedHead=false", "checkout", "-q", branch ? "FETCH_HEAD" : reference);
    }

    private static async Task Git(string directory, GitRemote remote, params string[] args)
    {
        var (code, error) = await RunGit(directory, args, remote);
        if (code != 0) throw new IOException($"git failed for {remote.WebUrl}: {error.Trim()}");
    }

    private static async Task<bool> TryGit(string directory, params string[] args)
    {
        return (await RunGit(directory, args, null)).Code == 0;
    }

    private static async Task<(int Code, string Error)> RunGit(string directory, string[] args, GitRemote? remote)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in args) info.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(info)!;
            var error = process.StandardError.ReadToEndAsync();
            await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (process.ExitCode, await error);
        }
        catch (Win32Exception)
        {
            throw new IOException($"git is needed to install from {remote?.Server ?? "this server"}, install it and try again");
        }
    }

    private static void Copy(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (relative.Split(Path.DirectorySeparatorChar)[0] == ".git") continue;
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
        }

        Directory.CreateDirectory(target);
    }

    private static void Delete(string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(directory, true);
    }

    public static async Task LoadStars(IReadOnlyList<HubEntry> entries)
    {
        foreach (var entry in entries)
        {
            var remote = entry.Remote;
            if (remote.StarsUrl is not { } url) continue;
            if (Cached(remote.Key, out var stars))
            {
                entry.Stars = stars;
                continue;
            }

            lock (_starsGate)
            {
                if (_starsBlocked.Contains(remote.Host)) continue;
            }

            try
            {
                using var response = await Http.GetAsync(url);
                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                {
                    lock (_starsGate) _starsBlocked.Add(remote.Host);
                    continue;
                }

                if (!response.IsSuccessStatusCode) continue;
                if (remote.ReadStars(JsonNode.Parse(await response.Content.ReadAsStringAsync())) is not { } count) continue;
                entry.Stars = count;
                Remember(remote.Key, count);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[modhub] stars for {remote.Key} failed: {ex.Message}");
            }
        }

        SaveStars();
    }

    private static string StarsPath => Path.Combine(CacheRoot, "stars.json");

    private static JsonObject Stars()
    {
        if (_stars != null) return _stars;
        try
        {
            _stars = File.Exists(StarsPath) ? JsonNode.Parse(File.ReadAllText(StarsPath)) as JsonObject : null;
        }
        catch
        {
            _stars = null;
        }

        return _stars ??= new JsonObject();
    }

    private static bool Cached(string key, out int stars)
    {
        stars = 0;
        lock (_starsGate)
        {
            if (Stars()[key] is not JsonObject hit) return false;
            var at = hit["at"]?.GetValue<long>() ?? 0;
            stars = hit["stars"]?.GetValue<int>() ?? 0;
            return DateTime.UtcNow - new DateTime(at, DateTimeKind.Utc) < StarsTtl;
        }
    }

    private static void Remember(string key, int stars)
    {
        lock (_starsGate)
        {
            Stars()[key] = new JsonObject { ["stars"] = stars, ["at"] = DateTime.UtcNow.Ticks };
        }
    }

    private static void SaveStars()
    {
        try
        {
            lock (_starsGate)
            {
                Directory.CreateDirectory(CacheRoot);
                File.WriteAllText(StarsPath, Stars().ToJsonString());
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[modhub] cannot save star cache: {ex.Message}");
        }
    }

    public static async Task LoadIcons(IReadOnlyList<HubEntry> entries)
    {
        var icons = Path.Combine(CacheRoot, "icons");
        Directory.CreateDirectory(icons);
        using var gate = new SemaphoreSlim(6);
        await Task.WhenAll(entries.Select(async entry =>
        {
            await gate.WaitAsync();
            try
            {
                var cache = Path.Combine(icons, $"{entry.Id}-{entry.Commit[..12]}.img");
                if (File.Exists(cache))
                {
                    var data = await File.ReadAllBytesAsync(cache);
                    entry.IconData = data.Length > 0 ? data : null;
                }
                else
                {
                    entry.IconData = await FetchIcon(entry);
                    await File.WriteAllBytesAsync(cache, entry.IconData ?? []);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[modhub] icon for {entry.Id} failes: {ex.Message}");
            }
            finally
            {
                entry.IconLoaded = true;
                gate.Release();
            }
        }));
    }

    private static async Task<byte[]?> FetchIcon(HubEntry entry)
    {
        IEnumerable<string> names = entry.Icon.Length > 0
            ? [entry.Icon]
            : IconNames.Select(n => entry.Path.Length > 0 ? $"{entry.Path}/{n}" : n);
        foreach (var name in names)
        {
            if (entry.Remote.RawUrl(entry.Commit, name) is not { } url) return null;
            using var response = await Http.GetAsync(url);
            if (!response.IsSuccessStatusCode) continue;
            var data = await response.Content.ReadAsByteArrayAsync();
            return name.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ? PngFromIco(data) : data;
        }

        return null;
    }

    private static byte[]? PngFromIco(byte[] ico)
    {
        if (ico.Length < 6 || BitConverter.ToUInt16(ico, 0) != 0 || BitConverter.ToUInt16(ico, 2) != 1) return null;
        var count = BitConverter.ToUInt16(ico, 4);
        byte[]? best = null;
        var bestSize = -1;
        for (var i = 0; i < count && 6 + i * 16 + 16 <= ico.Length; i++)
        {
            var at = 6 + i * 16;
            var size = ico[at] == 0 ? 256 : ico[at];
            var length = BitConverter.ToInt32(ico, at + 8);
            var offset = BitConverter.ToInt32(ico, at + 12);
            if (offset < 0 || length <= PngSignature.Length || offset + length > ico.Length) continue;
            if (!ico.AsSpan(offset, PngSignature.Length).SequenceEqual(PngSignature) || size <= bestSize) continue;
            best = ico.AsSpan(offset, length).ToArray();
            bestSize = size;
        }

        return best;
    }
}
