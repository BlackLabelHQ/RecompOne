using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using RecompOne.Recompiler.CodeGen;
using RecompOne.Recompiler.Config;
using RecompOne.Runtime.Cdrom;

namespace RecompOne.Recompiler.AutoConfigure;

public static class AutoConfigurator
{
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static int Run(string discPath, string outDir, string? gameName, string? signaturePath, bool sweepAll)
    {
        discPath = Path.GetFullPath(discPath);
        outDir = Path.GetFullPath(outDir);

        if (!File.Exists(discPath))
        {
            Console.Error.WriteLine($"disc file wasn't not found: {discPath}");
            return 1;
        }

        var fs = DiscFs.Open(discPath);
        var entries = DiscProbe.Probe(fs, out var boot);

        if (boot.Length == 0)
        {
            var first = entries.FirstOrDefault(e => e.Kind == FileKind.Executable);
            if (first == null)
            {
                Console.Error.WriteLine("no boot executable found and SYSTEM.CNF did not have named one");
                return 1;
            }

            boot = first.Path;
            Console.WriteLine(
                $"[autoconfig] SYSTEM.CNF unusable, please check the integrity of your disc file, falling back to {boot}"); //this should not happen
        }

        var db = LoadSignatures(signaturePath);
        Console.WriteLine($"[autoconfig] signatures: {db.Names} function(s), {db.Count} variant(s)");

        var funcmapDir = Path.Combine(outDir, "funcmaps");
        Directory.CreateDirectory(funcmapDir);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "main" };
        var overlays = new List<OverlayConfig>();
        var skipped = new List<ProbeEntry>();

        var bootEntry = entries.FirstOrDefault(e => Same(e.Path, boot));
        if (bootEntry == null || bootEntry.Kind != FileKind.Executable)
        {
            Console.Error.WriteLine($"boot file '{boot}' is not a PS-X EXE");
            return 1;
        }

        foreach (var entry in entries.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (entry == bootEntry) continue;

            if (entry.Kind == FileKind.Executable)
            {
                overlays.Add(new OverlayConfig
                {
                    Name = Unique(entry.Path, used),
                    File = entry.Path,
                    Skip = entry.Skip,
                    Base = $"0x{entry.Base:X8}",
                    Compression = entry.Compression.Length == 0 ? null : entry.Compression,
                    Functions = entry.Entry == 0 ? [] : [new FunctionEntry { Address = $"{entry.Entry:X8}" }]
                });
                continue;
            }

            if (entry.Kind != FileKind.RawCode) continue;

            if (entry.Base == 0)
            {
                skipped.Add(entry);
                continue;
            }

            overlays.Add(new OverlayConfig
            {
                Name = Unique(entry.Path, used),
                File = entry.Path,
                Base = $"0x{entry.Base:X8}",
                Compression = entry.Compression.Length == 0 ? null : entry.Compression,
                LinearSweep = true
            });
        }

        var id = GameId(fs, boot);
        var config = new RecompOneConfig
        {
            Game = new GameConfig
            {
                Id = id,
                Name = gameName ?? Path.GetFileNameWithoutExtension(discPath),
                Output = "../generated"
            },
            Cue = Relative(outDir, discPath),
            FuncMap = "funcmaps/main.json",
            LinearSweep = true,
            Overlays = overlays.ToArray()
        };

        Console.WriteLine($"[autoconfig] {overlays.Count} overlay(s) + main, sweeping");

        Sweep(config, fs, db, new OverlayConfig
        {
            Name = "main",
            File = boot,
            Skip = bootEntry.Skip,
            Base = $"0x{bootEntry.Base:X8}",
            LinearSweep = true
        }, funcmapDir);

        foreach (var overlay in overlays)
        {
            var sweepable = sweepAll || overlay.Skip > 0 || overlay.LinearSweep == true;
            if (!sweepable) continue;

            var clone = new OverlayConfig
            {
                Name = overlay.Name,
                File = overlay.File,
                Skip = overlay.Skip,
                Base = overlay.Base,
                LinearSweep = true
            };

            if (Sweep(config, fs, db, clone, funcmapDir))
                overlay.FuncMap = $"funcmaps/{overlay.Name}.json";
        }

        var configPath = Path.Combine(outDir, $"{Sanitize(config.Game.Name)}.json");
        File.WriteAllText(configPath, Emit(config));
        Console.WriteLine($"[autoconfig] wrote {configPath}");

        foreach (var entry in skipped)
            Console.WriteLine($"[autoconfig] could not find base address for overlay {entry.Path} " + $"({entry.Size} bytes, {entry.Reason}), please make a manual analysis and provide it");

        return 0;
    }

    private static string Emit(RecompOneConfig config)
    {
        var root = JsonNode.Parse(JsonSerializer.Serialize(config, Pretty))!.AsObject();
        Prune(root);
        foreach (var overlay in root["overlays"]!.AsArray())
            Prune(overlay!.AsObject());
        return root.ToJsonString(Pretty);
    }

    private static void Prune(JsonObject node)
    {
        foreach (var key in node.Select(p => p.Key).ToList())
        {
            var value = node[key];
            var drop = value switch
            {
                JsonArray array => array.Count == 0,
                JsonValue v when v.TryGetValue<bool>(out var b) => !b && key != "linearSweep",
                JsonValue v when v.TryGetValue<int>(out var i) => i == 0 || (key == "lba" && i == -1),
                _ => false
            };
            if (drop) node.Remove(key);
        }
    }

    private static bool Sweep(RecompOneConfig config, DiscFs fs, SignatureDb db, OverlayConfig overlay,
        string funcmapDir)
    {
        OverlayWriter.OverlayAnalysis? analysis;
        try
        {
            analysis = OverlayWriter.AnalyzeOverlay(config, overlay, fs);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[autoconfig] {overlay.Name}: sweep failed ({ex.Message})");
            return false;
        }

        if (analysis == null) return false;

        var match = SdkMatcher.Name(analysis.Functions, analysis.DiscBin, analysis.ElfInfo.TextBase, db);

        var info = new Symbols.FunctionInfo
        {
            TextBase = analysis.ElfInfo.TextBase,
            LoadAddress = analysis.ElfInfo.LoadAddress
        };
        foreach (var f in analysis.Functions.OrderBy(f => f.Start))
            info.Functions.Add(new Symbols.FunctionEntry
            {
                Name = f.Name,
                Address = f.Start,
                Size = f.End - f.Start
            });

        var path = Path.Combine(funcmapDir, $"{overlay.Name}.json");
        FunctionMapLoader.Save(path, info);

        Console.WriteLine($"[autoconfig] {overlay.Name}: {info.Functions.Count} function(s), " +
                          $"{match.Named} named ({match.ByLayout} by layout), {match.Ambiguous} ambiguous" +
                          (match.Libraries.Count > 0 ? $" [{string.Join(" ", match.Libraries)}]" : ""));
        return true;
    }

    private static SignatureDb LoadSignatures(string? path)
    {
        path ??= Path.Combine(AppContext.BaseDirectory, "AutoConfigure", "signatures", "psyq.json");
        if (File.Exists(path)) return SignatureDb.Load(path);

        var source = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "AutoConfigure", "signatures",
            "psyq.json");
        if (File.Exists(source)) return SignatureDb.Load(Path.GetFullPath(source));

        Console.WriteLine($"[autoconfig] WARNING: no signature bank at {path}, nothing will be correctly named");
        return new SignatureDb();
    }

    private static string GameId(DiscFs fs, string boot)
    {
        var name = Path.GetFileName(boot);
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot].Replace('_', '-') + name[dot..].Replace(".", "") : name;
    }

    private static bool Same(string a, string b)
    {
        return string.Equals(a.Replace('\\', '/'), b.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
    }

    private static string Unique(string path, HashSet<string> used)
    {
        var name = Sanitize(Path.GetFileNameWithoutExtension(path)).ToLowerInvariant();
        if (name.Length == 0) name = "overlay";

        var slash = path.LastIndexOf('/');
        var folder = slash < 0 ? "" : Sanitize(Path.GetFileName(path[..slash])).ToLowerInvariant();
        if (char.IsDigit(name[0]) || used.Contains(name))
            if (folder.Length > 0 && folder != name)
                name = $"{folder}_{name}";

        if (char.IsDigit(name[0])) name = "_" + name;

        var candidate = name;
        var n = 2;
        while (!used.Add(candidate)) candidate = $"{name}{n++}";
        return candidate;
    }

    private static string Sanitize(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.ToString().Trim('_');
    }

    private static string Relative(string from, string to)
    {
        var rel = Path.GetRelativePath(from, to).Replace('\\', '/');
        return rel.StartsWith("../../..") ? to.Replace('\\', '/') : rel;
    }
}