using System.Collections.Concurrent;
using System.Text.Json;
using RecompOne.Runtime.Assets;
using RecompOne.Runtime.Modding;

namespace RecompOne.Runtime.Hub;

public sealed class HubJob
{
    public volatile float Progress;
    public volatile string? Error;
    public bool Removing { get; init; }
}

public static class ModHub
{
    public const string ModType = "mod";
    public const string AssetPackType = "assetpack";
    public const string ShaderType = "shader";
    internal const string MarkerName = ".modhub";

    private sealed record Marker(string Id, string Type, string Commit, string Repository);

    private static readonly List<HubType> _types = [];
    private static readonly ConcurrentDictionary<string, HubJob> _jobs = new();
    private static readonly ConcurrentQueue<Action> _pending = new();
    private static Dictionary<string, HubInstallState> _states = new();
    private static IReadOnlyList<HubEntry> _entries = [];
    private static IReadOnlyList<string> _problems = [];
    private static Task? _refresh;
    private static DateTime _fetched;
    private static GitRemote? _hub;

    public static string Repository { get; private set; } = "";
    public static string Branch { get; private set; } = "main";
    public static bool Configured => _hub != null;
    public static string RepositoryUrl => _hub?.WebUrl ?? "";
    public static bool Loading => _refresh is { IsCompleted: false };
    public static string? Error { get; private set; }
    public static IReadOnlyList<HubEntry> Entries => _entries;
    public static IReadOnlyList<string> Problems => _problems;
    public static IReadOnlyList<HubType> Types => _types;

    static ModHub()
    {
        RegisterType(new HubType(ModType, "modhub.type.mod",
            static () => string.IsNullOrEmpty(ModLoader.Root) ? Path.GetFullPath("mods") : ModLoader.Root,
            static () => ModLoader.Rescan()));
        RegisterType(new HubType(AssetPackType, "modhub.type.assetpack",
            static () => string.IsNullOrEmpty(AssetReplacerManager.Instance.Root)
                ? Path.GetFullPath("packs")
                : AssetReplacerManager.Instance.Root,
            static () => AssetReplacerManager.Instance.RequestReload()));
        RegisterType(new HubType(ShaderType, "modhub.type.shader", static () => Path.GetFullPath("shaders")));
    }

    public static void Configure(string repository, string branch = "main")
    {
        Repository = repository.Trim().Trim('/');
        Branch = branch;
        _hub = Repository.Length > 0 ? GitRemote.FromShorthand(Repository) : null;
        _fetched = default;
    }

    public static void RegisterType(HubType type)
    {
        _types.RemoveAll(t => t.Id == type.Id);
        _types.Add(type);
    }

    public static HubType? FindType(string id)
    {
        return _types.FirstOrDefault(t => t.Id == id);
    }

    public static string DirectoryFor(string type)
    {
        return (FindType(type) ?? FindType(ModType)!).Directory();
    }

    public static void Refresh(bool force = false)
    {
        if (_hub == null || Loading) return;
        if (!force && _entries.Count > 0 && DateTime.UtcNow - _fetched < TimeSpan.FromMinutes(10)) return;
        _fetched = DateTime.UtcNow;
        _refresh = Task.Run(Load);
    }

    private static async Task Load()
    {
        try
        {
            Error = null;
            var files = await HubClient.FetchIndex(_hub!, Branch);
            var entries = new List<HubEntry>();
            var problems = new List<string>();
            foreach (var (id, text) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                if (!HubEntry.ValidId(id))
                {
                    problems.Add($"{id}: invalid entry name");
                    continue;
                }

                if (HubEntry.Parse(id, text, out var error) is { } entry) entries.Add(entry);
                else problems.Add($"{id}: {error}");
            }

            foreach (var problem in problems) Console.Error.WriteLine($"[modhub] {problem}");
            _problems = problems;
            _entries = entries;
            _pending.Enqueue(Scan);
            await Task.WhenAll(HubClient.LoadIcons(entries), HubClient.LoadStars(entries));
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Console.Error.WriteLine($"[modhub] cannot load {Repository}: {ex.Message}");
        }
    }

    public static HubInstallState StateOf(HubEntry entry)
    {
        return _states.TryGetValue(entry.Id, out var state) ? state : HubInstallState.Missing;
    }

    public static HubJob? JobOf(HubEntry entry)
    {
        return _jobs.TryGetValue(entry.Id, out var job) ? job : null;
    }

    public static void Install(HubEntry entry)
    {
        Run(entry, new HubJob(), job => HubClient.Install(entry, DirectoryFor(entry.Type), job));
    }

    public static void Uninstall(HubEntry entry)
    {
        Run(entry, new HubJob { Removing = true }, _ =>
        {
            var target = Path.Combine(DirectoryFor(entry.Type), entry.Id);
            if (!File.Exists(Path.Combine(target, MarkerName)))
                throw new IOException($"\"{target}\" was not installed by the mod hub");
            Directory.Delete(target, true);
            return Task.CompletedTask;
        });
    }

    private static void Run(HubEntry entry, HubJob job, Func<HubJob, Task> work)
    {
        if (_jobs.TryGetValue(entry.Id, out var running) && running.Error == null) return;
        _jobs[entry.Id] = job;
        Task.Run(async () =>
        {
            try
            {
                await work(job);
                _pending.Enqueue(() =>
                {
                    _jobs.TryRemove(entry.Id, out _);
                    FindType(entry.Type)?.Installed?.Invoke();
                    Scan();
                });
            }
            catch (Exception ex)
            {
                job.Error = ex.Message;
                Console.Error.WriteLine($"[modhub] {entry.Id}: {ex.Message}");
            }
        });
    }

    public static void DismissError(HubEntry entry)
    {
        if (_jobs.TryGetValue(entry.Id, out var job) && job.Error != null) _jobs.TryRemove(entry.Id, out _);
    }

    public static void Update()
    {
        while (_pending.TryDequeue(out var action))
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[modhub] {ex.Message}");
            }
    }

    public static void Scan()
    {
        var markers = new Dictionary<string, Marker>();
        foreach (var directory in _types.Select(t => t.Directory()).Distinct())
        {
            if (!Directory.Exists(directory)) continue;
            foreach (var folder in Directory.EnumerateDirectories(directory))
            {
                var file = Path.Combine(folder, MarkerName);
                if (!File.Exists(file)) continue;
                try
                {
                    if (JsonSerializer.Deserialize<Marker>(File.ReadAllText(file)) is { } marker) markers[marker.Id] = marker;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[modhub] bad marker {file}: {ex.Message}");
                }
            }
        }

        var states = new Dictionary<string, HubInstallState>();
        foreach (var entry in _entries)
            states[entry.Id] = markers.TryGetValue(entry.Id, out var marker)
                ? marker.Commit == entry.Commit ? HubInstallState.Installed : HubInstallState.Outdated
                : Directory.Exists(Path.Combine(DirectoryFor(entry.Type), entry.Id))
                    ? HubInstallState.Conflict
                    : HubInstallState.Missing;
        _states = states;
    }

    internal static void WriteMarker(string folder, HubEntry entry)
    {
        var marker = new Marker(entry.Id, entry.Type, entry.Commit, entry.Repository);
        File.WriteAllText(Path.Combine(folder, MarkerName), JsonSerializer.Serialize(marker));
    }
}
