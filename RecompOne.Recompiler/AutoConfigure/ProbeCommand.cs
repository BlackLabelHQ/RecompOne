using System.Text.Json;
using RecompOne.Runtime.Cdrom;

namespace RecompOne.Recompiler.AutoConfigure;

public static class ProbeCommand
{
    public static int Run(string discPath, string? jsonOut, bool showAll)
    {
        discPath = Path.GetFullPath(discPath);
        if (!File.Exists(discPath))
        {
            Console.Error.WriteLine($"disc file were not found: {discPath}");
            return 1;
        }

        var fs = DiscFs.Open(discPath);
        var entries = DiscProbe.Probe(fs, out var boot);

        Console.WriteLine($"[probe] {discPath}");
        Console.WriteLine($"[probe] format: {fs.Format}, boot: {(boot.Length > 0 ? boot : "(uknown)")}");
        Console.WriteLine();

        foreach (var group in new[] { FileKind.Executable, FileKind.RawCode, FileKind.Media, FileKind.Data })
        {
            var list = entries.Where(e => e.Kind == group)
                .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList();
            if (list.Count == 0) continue;
            if (!showAll && group is FileKind.Media or FileKind.Data)
            {
                Console.WriteLine($"{group.ToString().ToLowerInvariant()}: {list.Count} file(s)");
                continue;
            }

            Console.WriteLine($"{group.ToString().ToLowerInvariant()}: {list.Count} file(s)");
            foreach (var e in list)
            {
                var where = e.Base != 0 ? $"0x{e.Base:X8}{(e.BaseIsGuess ? "?" : " ")}" : "         ";
                var mark = Equals(e.Path, boot) ? "*" : " ";
                Console.WriteLine($"  {mark} {where} {e.Size,9}  {e.Path,-40} {e.Reason}");
            }

            Console.WriteLine();
        }

        if (jsonOut != null)
        {
            var payload = new
            {
                disc = discPath,
                boot,
                files = entries.Select(e => new
                {
                    path = e.Path,
                    lba = e.Lba,
                    size = e.Size,
                    kind = e.Kind.ToString().ToLowerInvariant(),
                    reason = e.Reason,
                    baseAddress = e.Base == 0 ? null : $"0x{e.Base:X8}",
                    baseIsGuess = e.BaseIsGuess,
                    skip = e.Skip,
                    score = Math.Round(e.Score, 3)
                })
            };
            File.WriteAllText(jsonOut, JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
            Console.WriteLine($"[probe] wrote {Path.GetFullPath(jsonOut)}");
        }

        return 0;
    }
}