using System.Text;
using RecompOne.Recompiler.Psx.Compression;
using RecompOne.Runtime.Cdrom;

namespace RecompOne.Recompiler.AutoConfigure;

public enum FileKind
{
    Executable,
    RawCode,
    Media,
    Data
}

public sealed record ProbeEntry(
    string Path,
    int Lba,
    uint Size,
    FileKind Kind,
    string Reason,
    uint Base,
    uint CodeSize,
    uint Entry,
    int Skip,
    string Compression,
    double Score,
    bool BaseIsGuess);

public static class DiscProbe
{
    private static readonly byte[] ExeMagic = "PS-X EXE"u8.ToArray();

    private static readonly (byte[] Magic, string Name)[] MediaMagic =
    {
        ("VABp"u8.ToArray(), "vab"),
        ("pBAV"u8.ToArray(), "vab"),
        ("pQES"u8.ToArray(), "seq"),
        ("SEQp"u8.ToArray(), "seq"),
        ("RIFF"u8.ToArray(), "riff"),
        ("CDXA"u8.ToArray(), "xa"),
        ("SMF"u8.ToArray(), "smf"),
        ("BS\x00"u8.ToArray(), "mdec")
    };

    private static readonly string[] MediaExt =
    {
        ".STR", ".XA", ".VB", ".VH", ".VAB", ".SEQ", ".TIM", ".TXT", ".WAV", ".ANM",
        ".BS", ".MOV", ".PXL", ".SPU", ".SMP", ".RAW", ".PAL", ".CLT", ".FNT"
    };

    public static List<ProbeEntry> Probe(DiscFs fs, out string bootExe)
    {
        bootExe = "";
        try
        {
            bootExe = SystemCfgBoot(fs);
        }
        catch
        {
        }

        var result = new List<ProbeEntry>();
        var sectors = fs.DataSectors;
        foreach (var entry in fs.Enumerate())
        {
            if (entry.IsDir || entry.Size == 0) continue;
            if (sectors > 0 && entry.Lba >= sectors)
            {
                result.Add(new ProbeEntry(entry.Path, entry.Lba, entry.Size, FileKind.Media, "outside ithe data track", 0, 0, 0, 0, "", 0, false));
                continue;
            }

            result.Add(Classify(fs, entry, sectors));
        }

        return Consensus(result);
    }

    private static List<ProbeEntry> Consensus(List<ProbeEntry> entries)
    {
        foreach (var folder in entries.Where(e => e.Kind == FileKind.RawCode && e.Base != 0)
                     .GroupBy(e => TopFolder(e.Path)))
        {
            var votes = folder.GroupBy(e => e.Base).OrderByDescending(g => g.Count()).First();
            if (votes.Count() < 3) continue;

            foreach (var odd in folder.Where(e => e.Base != votes.Key))
            {
                if (Math.Abs((long)odd.Base - votes.Key) > 0x20000) continue;
                entries[entries.IndexOf(odd)] = odd with
                {
                    Base = votes.Key,
                    Reason = odd.Reason + ", base from the folder consensos"
                };
            }
        }

        return entries;
    }

    private static string TopFolder(string path)
    {
        var slash = path.IndexOf('/');
        return slash < 0 ? "" : path[..slash];
    }

    public static string SystemCfgBoot(DiscFs fs)
    {
        var text = Encoding.ASCII.GetString(fs.ReadFile("SYSTEM.CNF"));
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Contains(';') ? raw[..raw.IndexOf(';')] : raw;
            var parts = line.Split('=', 2);
            if (parts.Length != 2 || parts[0].Trim() != "BOOT") continue;

            var value = parts[1].Trim().Split(';')[0];
            var colon = value.IndexOf(':');
            if (colon >= 0) value = value[(colon + 1)..];
            return value.TrimStart('\\', '/').Replace('\\', '/');
        }

        return "";
    }

    private static ProbeEntry Classify(DiscFs fs, DiscFs.DiscEntry entry, int sectors)
    {
        var probe = fs.ReadSectors(entry.Lba, Room(entry, sectors, 8192));
        var compression = "";

        if (Compression.Detect(probe) is { } container)
        {
            var whole = fs.ReadSectors(entry.Lba, Room(entry, sectors, int.MaxValue));
            var opened = Compression.Apply(whole, entry.Path, null);
            if (opened.Length != whole.Length)
            {
                compression = container.Name;
                probe = opened;
            }
        }


        if (probe.Length >= 0x800 && probe.AsSpan(0, 8).SequenceEqual(ExeMagic))
        {
            var entryPc = BitConverter.ToUInt32(probe, 0x10);
            var dest = BitConverter.ToUInt32(probe, 0x18);
            var textSize = BitConverter.ToUInt32(probe, 0x1C);
            var text = CodeScore.Rate(Sample(fs, entry, sectors)[0x800..]);
            var packed = text.Value < 0.55;
            return new ProbeEntry(entry.Path, entry.Lba, entry.Size, FileKind.Executable,
                packed
                    ? $"PS-X EXE header, but the text does not read as code ({text.Value:0.00}), packed?"
                    : "PS-X EXE header",
                dest, textSize, entryPc, 0x800, compression, text.Value, false);
        }

        foreach (var (magic, name) in MediaMagic)
            if (probe.Length >= magic.Length && probe.AsSpan(0, magic.Length).SequenceEqual(magic))
                return Media(entry, name + " header");

        if (IsForm2(fs, entry.Lba))
            return Media(entry, "form 2 sectors");

        var ext = Path.GetExtension(entry.Path).ToUpperInvariant();
        if (MediaExt.Contains(ext))
            return Media(entry, $"{ext.TrimStart('.').ToLowerInvariant()} file");

        var deep = compression.Length > 0 ? probe : Sample(fs, entry, sectors);
        var score = CodeScore.Rate(deep);
        if (score.Value < 0.55)
            return new ProbeEntry(entry.Path, entry.Lba, entry.Size, FileKind.Data,
                $"does not read as code ({score.Value:0.00})", 0, 0, 0, 0, compression, score.Value, false);

        var guess = CodeScore.GuessBase(deep, entry.Size);
        return new ProbeEntry(entry.Path, entry.Lba, entry.Size, FileKind.RawCode,
            $"mips-like ({score.Value:0.00}, {score.Returns} returns, {score.Prologues} prologues)",
            guess, (uint)deep.Length, 0, 0, compression, score.Value, true);
    }

    private const int WindowBytes = 16 * 1024;
    private const int Windows = 24;

    private static byte[] Sample(DiscFs fs, DiscFs.DiscEntry entry, int sectors)
    {
        var total = (long)Room(entry, sectors, int.MaxValue);
        if (total <= WindowBytes * Windows)
            return fs.ReadSectors(entry.Lba, (int)total);

        var stride = total / Windows / 2048 * 2048;
        var buffer = new byte[WindowBytes * Windows];
        for (var i = 0; i < Windows; i++)
        {
            var lba = entry.Lba + (int)(stride * i / 2048);
            var chunk = fs.ReadSectors(lba, WindowBytes);
            chunk.CopyTo(buffer.AsSpan(i * WindowBytes));
        }

        return buffer;
    }

    private static int Room(DiscFs.DiscEntry entry, int sectors, int want)
    {
        var size = (int)Math.Min(entry.Size, (uint)want);
        if (sectors <= 0) return size;
        var left = (sectors - entry.Lba) * 2048;
        return left <= 0 ? 0 : Math.Min(size, left);
    }

    private static ProbeEntry Media(DiscFs.DiscEntry entry, string reason)
    {
        return new ProbeEntry(entry.Path, entry.Lba, entry.Size, FileKind.Media, reason, 0, 0, 0, 0, "", 0, false);
    }

    private static bool IsForm2(DiscFs fs, int lba)
    {
        var raw = fs.ReadSectorData(lba, 2336);
        if (raw.Length < 8) return false;
        for (var i = 0; i < 4; i++)
            if (raw[i] != raw[i + 4])
                return false;
        return (raw[2] & 0x20) != 0;
    }
}