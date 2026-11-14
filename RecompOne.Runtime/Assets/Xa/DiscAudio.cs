using RecompOne.Runtime.Cdrom;

namespace RecompOne.Runtime.Assets.Xa;

public enum DiscAudioKind : byte
{
    Xa,
    Cdda
}

public sealed class DiscAudioTrack
{
    public DiscAudioKind Kind;
    public byte File;
    public byte Channel;
    public int Number;
    public int StartLba;
    public int LastLba;
    public int Sectors;
    public bool Stereo = true;
    public int Rate = 37800;
    public bool Video;
    public string Source = "";
    public int Hits;
    public long LastPlayed;

    public double Seconds => Kind == DiscAudioKind.Cdda
        ? Sectors / DiscAudio.CddaSectorsPerSecond
        : Rate > 0 ? Sectors * (Stereo ? 2016.0 : 4032.0) / Rate : 0;

    public double Interleave => Sectors > 1 ? (LastLba - StartLba) / (double)(Sectors - 1) : 1;

    public bool Contains(int lba)
    {
        return lba >= StartLba && lba <= LastLba;
    }

    public double SecondsAt(int lba)
    {
        if (lba <= StartLba) return 0;
        if (Kind == DiscAudioKind.Cdda) return (lba - StartLba) / DiscAudio.CddaSectorsPerSecond;
        return Rate > 0 ? (lba - StartLba) / Interleave * (Stereo ? 2016.0 : 4032.0) / Rate : 0;
    }
}

public static class DiscAudio
{
    public const double CddaSectorsPerSecond = 75.0;

    private const int SectorBytes = 2336;
    private const int MaxGap = 64;
    private const int VideoReach = 32;
    private const int MinSectors = 2;
    private const byte CodingMask = 0x15;
    private const byte AudioMask = 0x24;
    private const byte EndMask = 0x81;
    private const byte NoChannel = 0xFF;
    private const string CddaSource = "CD audio";
    private const string UnknownSource = "Unnamed area";

    private static readonly object _gate = new();
    private static CdController? _disc;
    private static DiscAudioTrack[] _cdda = [];
    private static DiscAudioTrack[] _xa = [];
    private static Dictionary<(byte, byte), DiscAudioTrack[]> _byChannel = [];
    private static bool _scanning;
    private static bool _ready;
    private static int _generation;
    private static long _clock;

    public static bool Scanning
    {
        get
        {
            lock (_gate)
            {
                return _scanning;
            }
        }
    }

    public static bool Ready
    {
        get
        {
            lock (_gate)
            {
                return _ready;
            }
        }
    }

    public static bool HasXa
    {
        get
        {
            lock (_gate)
            {
                return _ready && _xa.Length > 0;
            }
        }
    }

    public static IReadOnlyList<DiscAudioTrack> Cdda
    {
        get
        {
            Ensure();
            lock (_gate)
            {
                return _cdda;
            }
        }
    }

    public static IReadOnlyList<DiscAudioTrack> Xa
    {
        get
        {
            lock (_gate)
            {
                return _ready ? _xa : [];
            }
        }
    }

    public static void Ensure()
    {
        var disc = Runtime.Cd;
        int generation;
        lock (_gate)
        {
            if (ReferenceEquals(disc, _disc)) return;
            _disc = disc;
            _cdda = [];
            _xa = [];
            _byChannel = [];
            _ready = false;
            _scanning = disc != null;
            generation = ++_generation;
            if (disc == null) return;
            _cdda = ReadCdda(disc.Fs);
        }

        Task.Run(() => Scan(disc, generation));
    }

    public static DiscAudioTrack? Find(int lba, byte file, byte channel)
    {
        DiscAudioTrack[]? list;
        lock (_gate)
        {
            if (!_ready || !_byChannel.TryGetValue((file, channel), out list)) return null;
        }

        int low = 0, high = list.Length - 1;
        DiscAudioTrack? found = null;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (list[mid].StartLba <= lba)
            {
                found = list[mid];
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return found != null && found.Contains(lba) ? found : null;
    }

    public static DiscAudioTrack? FindCdda(int lba)
    {
        foreach (var track in Cdda)
            if (track.Contains(lba))
                return track;
        return null;
    }

    public static DiscAudioTrack? CddaTrack(int number)
    {
        foreach (var track in Cdda)
            if (track.Number == number)
                return track;
        return null;
    }

    public static void Played(DiscAudioTrack track)
    {
        lock (_gate)
        {
            track.Hits++;
            track.LastPlayed = ++_clock;
        }
    }

    private static DiscAudioTrack[] ReadCdda(DiscFs fs)
    {
        var tracks = fs.Tracks.OrderBy(t => t.StartLba).ToArray();
        var found = new List<DiscAudioTrack>();
        for (var i = 0; i < tracks.Length; i++)
        {
            if (tracks[i].Kind != DiscTrackKind.Audio) continue;
            var end = i + 1 < tracks.Length ? tracks[i + 1].StartLba : fs.LeadoutLba;
            if (end <= tracks[i].StartLba) continue;
            found.Add(new DiscAudioTrack
            {
                Kind = DiscAudioKind.Cdda,
                Number = tracks[i].Number,
                StartLba = tracks[i].StartLba,
                LastLba = end - 1,
                Sectors = end - tracks[i].StartLba,
                Stereo = true,
                Rate = 44100,
                Source = CddaSource
            });
        }

        return found.ToArray();
    }

    private sealed class Open
    {
        public DiscAudioTrack Track = null!;
        public byte Coding;
        public bool Ended;
        public int File;
        public int VideoSectors;
    }

    private static void Scan(CdController disc, int generation)
    {
        var found = new List<(DiscAudioTrack Track, int Video)>();
        try
        {
            var files = disc.Fs.Enumerate().Where(e => !e.IsDir).OrderBy(e => e.Lba).ToArray();
            var open = new Dictionary<(byte, byte), Open>();
            var total = disc.Fs.DataSectors;
            var lastVideo = int.MinValue;
            var fileAt = 0;

            for (var lba = 0; lba < total; lba++)
            {
                if (Volatile.Read(ref _generation) != generation) return;

                byte[] sec;
                lock (Sdk.LibCd.DiscLock)
                {
                    sec = disc.ReadSectorData(lba, SectorBytes);
                }

                if (sec.Length < 12) continue;
                var submode = sec[2];
                if ((submode & AudioMask) != AudioMask || sec[1] == NoChannel)
                {
                    if (sec[8] == 0x60 && sec[9] == 0x01 && sec[10] == 0x01 && sec[11] == 0x80) lastVideo = lba;
                    continue;
                }

                while (fileAt + 1 < files.Length && files[fileAt + 1].Lba <= lba) fileAt++;
                var key = (sec[0], sec[1]);
                var coding = (byte)(sec[3] & CodingMask);
                if (!open.TryGetValue(key, out var current) || current.Ended || current.Coding != coding ||
                    current.File != fileAt || lba - current.Track.LastLba > MaxGap)
                {
                    if (current != null) found.Add((current.Track, current.VideoSectors));
                    current = new Open
                    {
                        Coding = coding,
                        File = fileAt,
                        Track = new DiscAudioTrack
                        {
                            Kind = DiscAudioKind.Xa,
                            File = sec[0],
                            Channel = sec[1],
                            StartLba = lba,
                            Stereo = (coding & 0x01) != 0,
                            Rate = (coding & 0x04) != 0 ? 18900 : 37800,
                            Source = SourceOf(files, fileAt, lba)
                        }
                    };
                    open[key] = current;
                }

                current.Track.LastLba = lba;
                current.Track.Sectors++;
                if (lastVideo != int.MinValue && lba - lastVideo <= VideoReach) current.VideoSectors++;
                if ((submode & EndMask) != 0) current.Ended = true;
            }

            foreach (var left in open.Values) found.Add((left.Track, left.VideoSectors));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[assets] disc audio: scan failed, using heard streams only: {ex.Message}");
            found.Clear();
        }

        Publish(found, generation);
    }

    private static string SourceOf(DiscFs.DiscEntry[] files, int at, int lba)
    {
        if (files.Length == 0 || files[at].Lba > lba) return UnknownSource;
        var sectors = (files[at].Size + 2047) / 2048;
        return lba < files[at].Lba + sectors ? files[at].Path.TrimEnd('.') : UnknownSource;
    }

    private static void Publish(List<(DiscAudioTrack Track, int Video)> found, int generation)
    {
        var tracks = new List<DiscAudioTrack>(found.Count);
        foreach (var (track, video) in found)
        {
            if (track.Sectors < MinSectors) continue;
            track.Video = video * 2 > track.Sectors;
            tracks.Add(track);
        }

        tracks.Sort((a, b) => a.StartLba.CompareTo(b.StartLba));
        var numbers = new Dictionary<(string, byte, byte), int>();
        foreach (var track in tracks)
        {
            var key = (track.Source, track.File, track.Channel);
            numbers[key] = track.Number = numbers.GetValueOrDefault(key) + 1;
        }

        var byChannel = tracks.GroupBy(t => (t.File, t.Channel)).ToDictionary(g => g.Key, g => g.ToArray());
        lock (_gate)
        {
            if (_generation != generation) return;
            _xa = tracks.ToArray();
            _byChannel = byChannel;
            _ready = true;
            _scanning = false;
        }

        Console.WriteLine($"[assets] disc audio: {tracks.Count} xa streams, {_cdda.Length} cd audio tracks");
        XaRegistry.Resnap();
    }
}
