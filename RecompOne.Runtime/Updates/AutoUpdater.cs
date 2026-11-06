using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using RecompOne.Runtime.Config;

namespace RecompOne.Runtime.Updates;

public enum UpdatePhase { Idle, Checking, Available, Downloading, Applying, Failed }

public static class AutoUpdater
{
    const string ApplyArg = "--apply-update";
    const string DisableArg = "--disable-updated"; //for launcher, launch the game with this if the launcher already managed updates!
    static readonly Option<string> SkippedTag = Options.String("updates.skipped_tag");

    static string _repo = "";
    static volatile UpdatePhase _phase = UpdatePhase.Idle;
    static volatile string _latestTag = "";
    static volatile string _releaseNotes = "";
    static volatile string _assetUrl = "";
    static volatile string _error = "";
    static volatile bool _dismissed;
    static CancellationTokenSource? _cancel;
    static long _downloaded;
    static long _total;

    public static bool Enabled { get; set; } = true;
    
    public static Option<bool> CheckOnStartup { get; } = Options.Bool("updates.check_on_startup", true);

    public static bool Configured => Enabled && _repo.Length > 0;
    public static string Repository => _repo;
    public static string ReleasesUrl => $"https://github.com/{_repo}/releases/latest";
    public static UpdatePhase Phase => _phase;
    public static string LatestTag => _latestTag;
    public static string ReleaseNotes => _releaseNotes;
    public static string Error => _error;
    public static long DownloadedBytes => Interlocked.Read(ref _downloaded);
    public static long TotalBytes => Interlocked.Read(ref _total);

    public static bool PromptVisible =>
        Enabled && !_dismissed && _phase is UpdatePhase.Available or UpdatePhase.Downloading or UpdatePhase.Applying or UpdatePhase.Failed;

    static string ApiUrl => $"https://api.github.com/repos/{_repo}/releases/latest";
    static string ProjectName => _repo[(_repo.LastIndexOf('/') + 1)..];
    static string UserAgent => $"{ProjectName}-AutoUpdater";

    public static string? CurrentTag
    {
        get
        {
            var v = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrEmpty(v)) return null;
            int plus = v.IndexOf('+');
            if (plus >= 0) v = v[..plus];
            return v.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? v : null;
        }
    }

    static string InstallDir => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    static string ExePath => Environment.ProcessPath ?? "";
    static string WorkDir => Path.Combine(Path.GetTempPath(), $"{ProjectName}-update");
    static string StagingDir => Path.Combine(WorkDir, "staging");

    public static string[] ConsumeArguments(string[] args)
    {
        if (!args.Contains(DisableArg)) return args;
        Enabled = false;
        return args.Where(static arg => arg != DisableArg).ToArray();
    }

    public static bool HandleRelaunch(string[] args)
    {
        if (args.Length < 5 || args[0] != ApplyArg) return false;

        string staging = args[1];
        string target = args[2];
        string exe = args[4];
        _ = int.TryParse(args[3], out int pid);

        Log($"waiting for pid {pid} to exit before writing to {target}");
        WaitForExit(pid);
        try
        {
            CopyTree(staging, target);
            Log("files replaced, relaunching");
        }
        catch (Exception e)
        {
            Log($"failed to apply update: {e.Message}");
        }

        MakeExecutable(exe);
        try
        {
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = target, UseShellExecute = false });
        }
        catch (Exception e)
        {
            Log($"failed to relaunch: {e.Message}");
        }

        return true;
    }

    public static void Configure(string repository)
    {
        if (!Enabled)
        {
            Log("disabled, updates are managed by the launcher");
            return;
        }

        _repo = repository.Trim().Trim('/');
        TryDelete(WorkDir);
        CleanStaleFiles();

        if (CurrentTag == null)
        {
            Log("development build, update check skipped");
            return;
        }

        if (!CheckOnStartup)
        {
            Log($"running {CurrentTag}, update check disabled in settings");
            return;
        }

        _phase = UpdatePhase.Checking;
        Log($"running {CurrentTag}, checking {_repo} for updates...");
        Task.Run(CheckAsync);
    }

    static async Task CheckAsync()
    {
        try
        {
            using var http = NewClient(TimeSpan.FromSeconds(15));
            using var doc = JsonDocument.Parse(await http.GetStringAsync(ApiUrl));
            var root = doc.RootElement;

            string tag = root.GetProperty("tag_name").GetString() ?? "";
            if (string.IsNullOrEmpty(tag))
            {
                Log("the latest release have no tag");
                _phase = UpdatePhase.Idle;
                return;
            }

            if (tag == CurrentTag)
            {
                Log($"up to date, {tag} is the latest release");
                _phase = UpdatePhase.Idle;
                return;
            }

            if (tag == SkippedTag.Value)
            {
                Log($"{tag} is available but is skipped, staying on {CurrentTag}");
                _phase = UpdatePhase.Idle;
                return;
            }

            string suffix = AssetSuffix();
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";
                if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                _assetUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                break;
            }

            if (string.IsNullOrEmpty(_assetUrl))
            {
                Log($"{tag} is available but it ships no *{suffix} build for this platform, is this an error?");
                _phase = UpdatePhase.Idle;
                return;
            }

            _latestTag = tag;
            _releaseNotes = (root.TryGetProperty("body", out var body) ? body.GetString() : null)?.Trim() ?? "";
            _phase = UpdatePhase.Available;
            Log($"update available: {tag} (running {CurrentTag})");
        }
        catch (Exception e)
        {
            Log($"update check failed: {e.Message}");
            _phase = UpdatePhase.Idle;
        }
    }

    static void Log(string message) => Console.WriteLine($"[AutoUpdater] {message}");

    static async Task UpdateAsync(CancellationToken token)
    {
        try
        {
            _phase = UpdatePhase.Downloading;
            Log($"downloading {_latestTag} from {_assetUrl}");
            TryDelete(WorkDir);
            Directory.CreateDirectory(WorkDir);

            string suffix = AssetSuffix();
            string archive = Path.Combine(WorkDir, "release" + suffix[(suffix.IndexOf('.'))..]);

            using (var http = NewClient(TimeSpan.FromMinutes(30)))
            using (var response = await http.GetAsync(_assetUrl, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                _total = response.Content.Headers.ContentLength ?? 0;
                _downloaded = 0;

                await using var src = await response.Content.ReadAsStreamAsync(token);
                await using var dst = File.Create(archive);
                var buffer = new byte[81920];
                int read;
                while ((read = await src.ReadAsync(buffer, token)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), token);
                    Interlocked.Add(ref _downloaded, read);
                }
            }

            Log($"downloaded {Interlocked.Read(ref _downloaded) / 1048576f:0.0} MB, extracting");
            Directory.CreateDirectory(StagingDir);
            Extract(archive, StagingDir);

            string exeName = Path.GetFileName(ExePath);
            string staged = Path.Combine(StagingDir, exeName);
            if (!File.Exists(staged)) throw new FileNotFoundException($"{exeName} not found in the release archive");

            MakeExecutable(staged);

            _phase = UpdatePhase.Applying;
            Log($"restarting to apply {_latestTag} into {InstallDir}");

            Process.Start(new ProcessStartInfo(staged)
            {
                WorkingDirectory = StagingDir,
                UseShellExecute = false,
                ArgumentList =
                {
                    ApplyArg,
                    StagingDir,
                    InstallDir,
                    Environment.ProcessId.ToString(),
                    ExePath,
                },
            });

            Environment.Exit(0);
        }
        catch (OperationCanceledException)
        {
            Log("download cancelled");
            TryDelete(WorkDir);
            _dismissed = true;
            _phase = UpdatePhase.Available;
        }
        catch (Exception e)
        {
            Log($"update failed: {e.Message}");
            _error = e.Message;
            _phase = UpdatePhase.Failed;
        }
    }

    public static void CheckNow()
    {
        if (_phase == UpdatePhase.Available)
        {
            _dismissed = false;
            return;
        }

        SkippedTag.Value = "";
        _dismissed = false;
        _phase = UpdatePhase.Checking;
        Log("manual update check requested");
        Task.Run(CheckAsync);
    }

    public static void StartUpdate()
    {
        _cancel = new CancellationTokenSource();
        Task.Run(() => UpdateAsync(_cancel.Token));
    }

    public static void CancelDownload()
    {
        _cancel?.Cancel();
    }

    public static void SkipLatest()
    {
        SkippedTag.Value = _latestTag;
        Log($"{_latestTag} skipped, it will not be offered again");
        Dismiss();
    }

    public static void AcknowledgeFailure()
    {
        _phase = UpdatePhase.Available;
        Dismiss();
    }

    public static void OpenReleasesPage()
    {
        OpenUrl(ReleasesUrl);
    }

    public static void Dismiss() => _dismissed = true;

    static HttpClient NewClient(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return http;
    }

    static string AssetSuffix()
    {
        string arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return $"windows-{arch}.zip";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return $"macos-{arch}.zip";
        return $"linux-{arch}.tar.gz";
    }

    static void Extract(string archive, string dir)
    {
        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archive, dir, true);
            return;
        }

        using var fs = File.OpenRead(archive);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        TarFile.ExtractToDirectory(gz, dir, true);
    }

    static void CopyTree(string source, string target)
    {
        foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));

        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            if (IsUserFile(relative)) continue;
            CopyWithRetry(file, Path.Combine(target, relative));
        }
    }

    static bool IsUserFile(string relative)
    {
        string name = Path.GetFileName(relative);
        if (name.Equals("settings.json", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Equals("interface.ini", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.EndsWith(".sav", StringComparison.OrdinalIgnoreCase)) return true;

        string root = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return root.Equals("disc", StringComparison.OrdinalIgnoreCase)
            || root.Equals("mods", StringComparison.OrdinalIgnoreCase);
    }

    static void CopyWithRetry(string source, string target)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Copy(source, target, true);
                MakeExecutable(target);
                return;
            }
            catch (IOException) when (attempt < 40)
            {
                Thread.Sleep(250);
            }
            catch (IOException)
            {
                MoveAside(target);
                File.Copy(source, target, true);
                MakeExecutable(target);
                return;
            }
        }
    }

    const string StaleSuffix = ".pending-delete";

    static void MoveAside(string target)
    {
        for (int n = 0; ; n++)
        {
            string aside = $"{target}{StaleSuffix}{(n == 0 ? "" : n.ToString())}";
            if (File.Exists(aside)) continue;
            File.Move(target, aside);
            return;
        }
    }

    static void CleanStaleFiles()
    {
        try
        {
            foreach (string f in Directory.GetFiles(InstallDir, "*" + StaleSuffix + "*", SearchOption.AllDirectories))
                try { File.Delete(f); } catch { }
        }
        catch
        {
        }
    }

    static void WaitForExit(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.WaitForExit(30000);
        }
        catch
        {
        }

        Thread.Sleep(500);
    }

    static void MakeExecutable(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            File.SetUnixFileMode(path, File.GetUnixFileMode(path) |
                UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
        catch
        {
        }
    }

    public static bool CanWriteInstallDir()
    {
        try
        {
            string probe = Path.Combine(InstallDir, ".update-probe");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch
        {
        }
    }
}
