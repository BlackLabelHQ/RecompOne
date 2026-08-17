using RecompOne.Runtime.Cdrom.Chd;

namespace RecompOne.Runtime.Cdrom;

public sealed class ChdImage : IDiscImage
{
    private const int SectorData = 2352;
    private const int FrameSize = 2448;
    private const int TrackPadding = 4;

    private sealed record Track(int Number, DiscTrackKind Kind, int DiscBase, int Pregap, int ChdFrame, int Frames)
    {
        public int ReportedLba => DiscBase + Pregap;
    }

    private readonly ChdFile _chd;
    private readonly List<Track> _tracks = [];
    private readonly int _framesPerHunk;
    private int _lastOobLba = int.MinValue;

    private ChdImage(ChdFile chd)
    {
        _chd = chd;
        _framesPerHunk = (int)(chd.HunkBytes / FrameSize);
        if (_framesPerHunk <= 0)
            throw new InvalidDataException("chd hunk size is not a multiple of the cd frame size");
        ParseTracks();
    }

    public static ChdImage Open(string path) => new(ChdFile.Open(path));

    public string Format => "chd";

    public int FirstTrack => _tracks.Count > 0 ? _tracks.Min(t => t.Number) : 1;

    public int LastTrack => _tracks.Count > 0 ? _tracks.Max(t => t.Number) : 1;

    public bool HasTracks => _tracks.Count > 0;

    public IReadOnlyList<DiscTrack> Tracks => _tracks
        .Select(t => new DiscTrack(t.Number, t.Kind, t.ReportedLba, SectorData))
        .ToList();

    public int LeadoutLba => _tracks.Count > 0 ? _tracks[^1].DiscBase + _tracks[^1].Frames : 0;

    public int DataSectors
    {
        get
        {
            var t = DataTrack();
            if (t == null) return 0;
            int next = int.MaxValue;
            foreach (var other in _tracks)
                if (other.ReportedLba > t.ReportedLba && other.ReportedLba < next) next = other.ReportedLba;
            return next != int.MaxValue ? next - t.ReportedLba : t.Frames;
        }
    }

    public bool TrackStartLba(int track, out int lba)
    {
        var t = _tracks.Find(x => x.Number == track);
        if (t == null)
        {
            lba = 0;
            return false;
        }
        lba = t.ReportedLba;
        return true;
    }

    public byte[] ReadSectorData(int lba, int size)
    {
        var buf = new byte[size];
        if (lba < 0) return buf;

        var track = DataTrack();
        if (track == null) return buf;

        if (lba >= DataSectors)
        {
            if (lba != _lastOobLba)
            {
                _lastOobLba = lba;
                Console.WriteLine($"[DiscImage] read outside data track: lba={lba}");
            }
            return buf;
        }

        int offset = size switch { >= 2340 => 12, >= 2329 => 16, _ => 24 };
        int frame = track.ChdFrame + (lba - track.DiscBase);
        int hunk = frame / _framesPerHunk;
        int indexInHunk = frame % _framesPerHunk;

        var hunkData = _chd.ReadHunk(hunk);
        int start = indexInHunk * FrameSize + offset;
        int want = Math.Min(size, SectorData - offset);
        Array.Copy(hunkData, start, buf, 0, want);
        return buf;
    }

    private Track? DataTrack() => _tracks.Find(t => t.Kind == DiscTrackKind.Data);

    private void ParseTracks()
    {
        uint cht2 = ChdBig.Tag("CHT2");
        uint chtr = ChdBig.Tag("CHTR");
        uint chcd = ChdBig.Tag("CHCD");

        int discLba = 0;
        int chdFrame = 0;

        foreach (var entry in _chd.Metadata)
        {
            if (entry.Tag != cht2 && entry.Tag != chtr && entry.Tag != chcd) continue;

            var text = entry.Text;
            int number = ParseInt(text, "TRACK:");
            int frames = ParseInt(text, "FRAMES:");
            int pregap = ParseInt(text, "PREGAP:");
            string type = ParseWord(text, "TYPE:");
            if (number <= 0 || frames <= 0) continue;

            var kind = type.StartsWith("AUDIO", StringComparison.OrdinalIgnoreCase)
                ? DiscTrackKind.Audio
                : DiscTrackKind.Data;

            _tracks.Add(new Track(number, kind, discLba, pregap, chdFrame, frames));

            discLba += frames;
            chdFrame += (frames + TrackPadding - 1) / TrackPadding * TrackPadding;
        }

        _tracks.Sort((a, b) => a.Number.CompareTo(b.Number));
    }

    private static int ParseInt(string text, string key)
    {
        int i = text.IndexOf(key, StringComparison.Ordinal);
        if (i < 0) return 0;
        i += key.Length;
        int j = i;
        while (j < text.Length && char.IsDigit(text[j])) j++;
        return j > i && int.TryParse(text.AsSpan(i, j - i), out int value) ? value : 0;
    }

    private static string ParseWord(string text, string key)
    {
        int i = text.IndexOf(key, StringComparison.Ordinal);
        if (i < 0) return "";
        i += key.Length;
        int j = i;
        while (j < text.Length && !char.IsWhiteSpace(text[j])) j++;
        return text[i..j];
    }

    public void Dispose() => _chd.Dispose();
}
