using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RecompOne.Runtime.Host;
using RecompOne.Runtime.Host.Window;

namespace RecompOne.Runtime.Modding;

public sealed class ModEntry
{
    public ModInfo Info = null!;
    public bool Enabled;
    public bool Loaded;
    public int HookCount;
    public byte[]? IconData;
    public bool HasSettings { get; internal set; }
    public string? LoadError { get; internal set; }
    internal bool IsZip;
    internal List<(string Path, string Text)> Sources = [];
    internal AssemblyLoadContext? Alc;
    internal IMod[] Instances = [];

    public void DrawSettings()
    {
        foreach (var inst in Instances)
            try
            {
                inst.DrawSettings();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Mods] {Info.Id}: DrawSettings threw: {ex.Message}");
            }
    }
}

public static class ModLoader
{
    private static readonly List<ModEntry> _mods = [];
    private static string _cacheDir = "";

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static IReadOnlyList<ModEntry> Mods
    {
        get
        {
            lock (_mods)
            {
                return _mods.ToArray();
            }
        }
    }

    public static IReadOnlyList<ModInfo> LoadedMods
    {
        get
        {
            lock (_mods)
            {
                return _mods.Where(m => m.Loaded).Select(m => m.Info).ToArray();
            }
        }
    }

    public static string Root { get; private set; } = "";

    private static bool _loaded;
    private static FileSystemWatcher? _watcher;
    private static int _dirty;
    private static DateTime _dirtyAt;

    public static void LoadAll(string? root = null)
    {
        if (_loaded) return;
        _loaded = true;

        root ??= Path.GetFullPath("mods");
        Directory.CreateDirectory(root);
        Root = root;
        _cacheDir = Path.Combine(root, ".cache");

        var discovered = Order(Discover(root));
        lock (_mods)
        {
            _mods.Clear();
            _mods.AddRange(discovered);
        }

        foreach (var e in discovered) e.Enabled = IsEnabled(e.Info.Id);

        StartWatching();

        var toLoad = discovered.Where(e => e.Enabled).ToList();
        if (toLoad.Count == 0) return;

        ModLoadingPopup.Begin(toLoad.Count);

        var work = Task.Run(() =>
        {
            for (var i = 0; i < toLoad.Count; i++)
            {
                ModLoadingPopup.Update(i, toLoad[i].Info.Name);
                LoadEntry(toLoad[i]);
            }

            ModLoadingPopup.Update(toLoad.Count, "");
            try
            {
                HookManager.Commit();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Mods] hook install failed: {ex.Message}");
            }
        });

        while (!work.IsCompleted)
        {
            HostWindow.Pump();
            Thread.Sleep(16);
        }

        ModLoadingPopup.End();

        Console.WriteLine(
            $"[Mods] loaded {toLoad.Count(e => e.Loaded)}/{toLoad.Count} mod(s), {HookManager.HookedFunctionCount} function(s) hooked");
    }

    private static void StartWatching()
    {
        if (_watcher != null || string.IsNullOrEmpty(Root)) return;

        try
        {
            _watcher = new FileSystemWatcher(Root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
            };
            _watcher.Created += OnFolderChanged;
            _watcher.Deleted += OnFolderChanged;
            _watcher.Renamed += OnFolderChanged;
            _watcher.Changed += OnFolderChanged;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            _watcher = null;
            Console.Error.WriteLine($"[mods] could not watch mod s folder: {ex.Message}");
        }
    }

    private static void OnFolderChanged(object sender, FileSystemEventArgs e)
    {
        if (!string.IsNullOrEmpty(_cacheDir) && e.FullPath.StartsWith(_cacheDir, StringComparison.OrdinalIgnoreCase))
            return;

        Interlocked.Exchange(ref _dirty, 1);
        _dirtyAt = DateTime.UtcNow;
    }

    public static bool PollChanges()
    {
        if (Volatile.Read(ref _dirty) == 0) return false;
        if (DateTime.UtcNow - _dirtyAt < TimeSpan.FromMilliseconds(750)) return false;

        Interlocked.Exchange(ref _dirty, 0);
        return Rescan();
    }

    public static bool Rescan()
    {
        if (!_loaded || string.IsNullOrEmpty(Root)) return false;

        List<ModEntry> found;
        try
        {
            found = Order(Discover(Root));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Mods] rescan failed: {ex.Message}");
            return false;
        }

        var changed = false;

        lock (_mods)
        {
            var known = new HashSet<string>(_mods.Select(m => m.Info.Id), StringComparer.OrdinalIgnoreCase);
            var present = new HashSet<string>(found.Select(m => m.Info.Id), StringComparer.OrdinalIgnoreCase);

            foreach (var entry in found)
            {
                if (known.Contains(entry.Info.Id)) continue;
                entry.Enabled = IsEnabled(entry.Info.Id);
                _mods.Add(entry);
                changed = true;
                Console.WriteLine($"[Mods] got {entry.Info.Name}");
            }

            for (var i = _mods.Count - 1; i >= 0; i--)
            {
                if (present.Contains(_mods[i].Info.Id)) continue;
                var gone = _mods[i];
                if (gone.Loaded) UnloadEntry(gone); //unload it also
                _mods.RemoveAt(i);
                changed = true;
                Console.WriteLine($"[Mods] removed {gone.Info.Name}");
            }
        }

        return changed;
    }

    public static void SetEnabled(string id, bool enabled)
    {
        var entry = Find(id);
        if (entry == null) return;
        entry.Enabled = enabled;
        SaveEnabled(id, enabled);

        if (enabled && !entry.Loaded)
        {
            LoadEntry(entry);
            SafeCommit();
        }
        else if (!enabled && entry.Loaded)
        {
            UnloadEntry(entry);
        }
    }

    public static void Reload(string id)
    {
        var entry = Find(id);
        if (entry == null || !entry.Enabled) return;
        if (entry.Loaded) UnloadEntry(entry);
        entry.Sources = ReadSources(entry.Info.SourcePath, entry.IsZip);
        LoadEntry(entry);
        SafeCommit();
    }

    private static void SafeCommit()
    {
        try
        {
            HookManager.Commit();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Mods] hook install failed: {ex.Message}");
        }
    }

    private static ModEntry? Find(string id)
    {
        lock (_mods)
        {
            return _mods.FirstOrDefault(m => string.Equals(m.Info.Id, id, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static bool IsEnabled(string id)
    {
        return Runtime.View.GetBool($"mods.{id}.enabled", false);
    }

    private static void SaveEnabled(string id, bool enabled)
    {
        Runtime.View.SetBool($"mods.{id}.enabled", enabled);
        Runtime.SaveView();
    }

    private const int MaxDepth = 4;

    private static List<ModEntry> Discover(string root)
    {
        var list = new List<ModEntry>();
        DiscoverFolders(root, list, 0);

        foreach (var zipPath in Directory.EnumerateFiles(root, "*.zip", SearchOption.AllDirectories))
            try
            {
                string? json;
                using (var zip = ZipFile.OpenRead(zipPath))
                {
                    var entry = zip.GetEntry("mod.json");
                    if (entry == null)
                    {
                        Console.Error.WriteLine($"[Mods] mod.json not found for {Path.GetFileName(zipPath)}, skipping");
                        continue;
                    }

                    using var reader = new StreamReader(entry.Open());
                    json = reader.ReadToEnd();
                }

                var info = ParseInfo(json, zipPath);
                if (info == null) continue;
                list.Add(new ModEntry
                {
                    Info = info,
                    IsZip = true,
                    Sources = ReadSources(zipPath, true),
                    IconData = LoadIcon(zipPath, true)
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Mods] failed to read {Path.GetFileName(zipPath)}: {ex.Message}");
            }

        return list;
    }

    //recursive folders, mod cna have its dependencies inside it if needed
    private static void DiscoverFolders(string dir, List<ModEntry> list, int depth)
    {
        if (depth > MaxDepth) return;

        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            var name = Path.GetFileName(sub);
            if (name.StartsWith('.')) continue;

            var jsonPath = Path.Combine(sub, "mod.json");
            if (!File.Exists(jsonPath))
            {
                DiscoverFolders(sub, list, depth + 1);
                continue;
            }

            var info = ParseInfo(File.ReadAllText(jsonPath), sub);
            if (info == null) continue;
            list.Add(new ModEntry
            {
                Info = info,
                IsZip = false,
                Sources = ReadSources(sub, false),
                IconData = LoadIcon(sub, false)
            });
        }
    }

    private static List<(string, string)> ReadSources(string sourcePath, bool isZip)
    {
        var sources = new List<(string, string)>();
        try
        {
            if (isZip)
            {
                using var zip = ZipFile.OpenRead(sourcePath);
                foreach (var e in zip.Entries)
                {
                    if (!e.FullName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
                    if (e.FullName.Split('/').Any(p => p is "obj" or "bin" || p.StartsWith('.'))) continue;
                    using var sr = new StreamReader(e.Open());
                    sources.Add((e.FullName, sr.ReadToEnd()));
                }
            }
            else
            {
                foreach (var file in Directory.EnumerateFiles(sourcePath, "*.cs", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(sourcePath, file);
                    if (rel.Split(Path.DirectorySeparatorChar)
                        .Any(p => p is "obj" or "bin" || p.StartsWith('.'))) continue;
                    sources.Add((file, File.ReadAllText(file)));
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Mods] failed to read sources for {Path.GetFileName(sourcePath)}: {ex.Message}");
        }

        return sources;
    }

    private const string IconName = "mod-icon.png";

    private static byte[]? LoadIcon(string sourcePath, bool isZip)
    {
        try
        {
            if (isZip)
            {
                using var zip = ZipFile.OpenRead(sourcePath);
                var e = zip.GetEntry(IconName);
                if (e == null) return null;
                using var s = e.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }

            var path = Path.Combine(sourcePath, IconName);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch
        {
            return null;
        }
    }

    private static ModInfo? ParseInfo(string json, string sourcePath)
    {
        try
        {
            var info = JsonSerializer.Deserialize<ModInfo>(json, _json);
            if (info == null || string.IsNullOrWhiteSpace(info.Id))
            {
                Console.Error.WriteLine(
                    $"[Mods] malformed mod.json for {Path.GetFileName(sourcePath)}: missing id, skipping");
                return null;
            }

            if (string.IsNullOrWhiteSpace(info.Name)) info.Name = info.Id;
            info.SourcePath = sourcePath;
            return info;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Mods] malformed mod.json for {Path.GetFileName(sourcePath)}: {ex.Message}");
            return null;
        }
    }

    private static List<ModEntry> Order(List<ModEntry> mods)
    {
        var byId = new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
            if (!byId.TryAdd(mod.Info.Id, mod))
                Console.Error.WriteLine($"[Mods] duplicate mod id {mod.Info.Id} at {mod.Info.SourcePath}, skipping");

        var queue = byId.Values.OrderBy(m => m.Info.Id, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var mod in queue.ToList())
        {
            var missing = mod.Info.Dependencies.FirstOrDefault(d => !byId.ContainsKey(d));
            if (missing != null)
            {
                Console.Error.WriteLine($"[Mods] {mod.Info.Id}: missing dependency {missing}, skipping");
                queue.Remove(mod);
            }
        }

        var result = new List<ModEntry>();
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (queue.Count > 0)
        {
            var next = queue.FirstOrDefault(m => m.Info.Dependencies.All(placed.Contains));
            if (next == null)
            {
                foreach (var mod in queue)
                    Console.Error.WriteLine($"[Mods] {mod.Info.Id}: dependency cycle, skipping");
                break;
            }

            queue.Remove(next);
            placed.Add(next.Info.Id);
            result.Add(next);
        }

        return result;
    }

    private static void LoadEntry(ModEntry mod)
    {
        try
        {
            if (mod.Loaded) return;
            if (mod.Sources.Count == 0)
            {
                Console.Error.WriteLine($"[Mods] {mod.Info.Id}: no source files, skipping");
                return;
            }

            var cachePath = Path.Combine(_cacheDir, $"{mod.Info.Id}-{CacheKey(mod.Info, mod.Sources)}.dll");
            byte[]? bytes;
            if (File.Exists(cachePath))
            {
                bytes = File.ReadAllBytes(cachePath);
            }
            else
            {
                Console.WriteLine($"[Mods] building {mod.Info.Id}...");
                bytes = ModCompiler.Compile(mod.Info.Id, mod.Sources);
                if (bytes == null)
                {
                    mod.LoadError = "compilation failed, check the console";
                    return;
                }

                try
                {
                    Directory.CreateDirectory(_cacheDir);
                    foreach (var stale in Directory.EnumerateFiles(_cacheDir, $"{mod.Info.Id}-*.dll"))
                        File.Delete(stale);
                    File.WriteAllBytes(cachePath, bytes);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Mods] failed to cache {mod.Info.Id}: {ex.Message}");
                }
            }

            var alc = new AssemblyLoadContext($"mod-{mod.Info.Id}-{Guid.NewGuid():N}", true);
            Assembly asm;
            using (var ms = new MemoryStream(bytes))
            {
                asm = alc.LoadFromStream(ms);
            }

            mod.HookCount = RegisterHooks(mod.Info, asm);
            mod.Instances = CreateInstances(mod.Info, asm);
            mod.HasSettings = mod.Instances.Any(OverridesDrawSettings);
            mod.LoadError = null;
            mod.Alc = alc;
            mod.Loaded = true;
            foreach (var inst in mod.Instances) inst.OnLoad();
            Console.WriteLine($"[Mods] {mod.Info.Id} v{mod.Info.Version}: {mod.HookCount} hook(s)");
        }
        catch (Exception ex)
        {
            mod.LoadError = ex.Message;
            Console.Error.WriteLine($"[Mods] failed to load {mod.Info.Id}: {ex.Message}");
        }
    }

    private static bool OverridesDrawSettings(IMod inst)
    {
        var map = inst.GetType().GetInterfaceMap(typeof(IMod));
        for (var i = 0; i < map.InterfaceMethods.Length; i++)
            if (map.InterfaceMethods[i].Name == nameof(IMod.DrawSettings))
                return map.TargetMethods[i].DeclaringType != typeof(IMod);
        return false;
    }

    private static void UnloadEntry(ModEntry mod)
    {
        try
        {
            foreach (var inst in mod.Instances)
                try
                {
                    inst.OnUnload();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Mods] {mod.Info.Id}: OnUnload failed: {ex.Message}");
                }

            HookManager.RemoveMod(mod.Info);
            mod.Instances = [];
            mod.HookCount = 0;
            mod.HasSettings = false;
            mod.Loaded = false;
            mod.Alc?.Unload();
            mod.Alc = null;
            Console.WriteLine($"[Mods] {mod.Info.Id}: unloaded");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Mods] failed to unload {mod.Info.Id}: {ex.Message}");
        }
    }

    private static int RegisterHooks(ModInfo info, Assembly asm)
    {
        var count = 0;
        foreach (var type in asm.GetTypes())
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        foreach (var attr in method.GetCustomAttributes<FunctionHookAttribute>())
        {
            var target = SymbolRegistry.Resolve(attr.Overlay, attr.Function, attr.Address);
            if (target == null)
            {
                var what = attr.Function ?? $"0x{attr.Address:X8}";
                Console.Error.WriteLine($"[Mods] {info.Id}: function not found: {attr.Overlay}/{what}");
                continue;
            }

            var ok = attr switch
            {
                ReplaceAttribute => HookManager.AddReplace(info, target, method),
                PreHookAttribute => HookManager.AddPre(info, target, method),
                PostHookAttribute => HookManager.AddPost(info, target, method),
                _ => false
            };
            if (ok) count++;
        }

        return count;
    }

    private static string CacheKey(ModInfo info, List<(string Path, string Text)> sources)
    {
        var sb = new StringBuilder();
        sb.Append(typeof(ModLoader).Assembly.ManifestModule.ModuleVersionId);
        var entry = Assembly.GetEntryAssembly();
        if (entry != null) sb.Append(entry.ManifestModule.ModuleVersionId);
        foreach (var (path, text) in sources.OrderBy(s => s.Path, StringComparer.Ordinal))
        {
            sb.Append(Path.GetFileName(path));
            sb.Append(text);
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static IMod[] CreateInstances(ModInfo info, Assembly asm)
    {
        var instances = new List<IMod>();
        foreach (var type in asm.GetTypes())
        {
            if (type.IsAbstract || !typeof(IMod).IsAssignableFrom(type)) continue;
            try
            {
                if (Activator.CreateInstance(type) is IMod mod) instances.Add(mod);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Mods] {info.Id}: failed to create {type.Name}: {ex.Message}");
            }
        }

        return instances.ToArray();
    }
}