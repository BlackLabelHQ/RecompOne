namespace RecompOne.Runtime.Assets.Xa;

public sealed class SeenStream
{
    public byte File;
    public byte Channel;
    public int StartLba;
    public int LastLba;
    public int Sectors;
    public bool Stereo;
    public int Rate;
    public int Hits;
    public long LastSeen;

    public double Seconds => Rate > 0 ? Sectors * (Stereo ? 2016.0 : 4032.0) / Rate : 0;

    public SeenStream Clone()
    {
        return (SeenStream)MemberwiseClone();
    }
}

public static class XaRegistry
{
    private const int MaxEntries = 512;
    private const int SameRunSlack = 512;

    private static readonly List<SeenStream> _seen = [];
    private static readonly object _gate = new();
    private static SeenStream? _current;
    private static long _clock;

    public static volatile bool Enabled;

    public static void Note(int lba, byte file, byte channel, byte coding, bool newRun)
    {
        if (!Enabled) return;

        lock (_gate)
        {
            if (newRun || _current == null)
            {
                _current = _seen.FirstOrDefault(s => s.File == file && s.Channel == channel &&
                                                     lba >= s.StartLba && lba <= s.LastLba + SameRunSlack);
                if (_current == null)
                {
                    if (_seen.Count >= MaxEntries) _seen.RemoveAt(0);
                    _current = new SeenStream { File = file, Channel = channel, StartLba = lba, LastLba = lba };
                    _seen.Add(_current);
                }

                _current.Hits++;
            }

            var seen = _current;
            if (lba > seen.LastLba)
            {
                seen.LastLba = lba;
                seen.Sectors++;
            }
            else if (seen.Sectors == 0)
            {
                seen.Sectors = 1;
            }

            seen.Stereo = (coding & 0x01) != 0;
            seen.Rate = (coding & 0x04) != 0 ? 18900 : 37800;
            seen.LastSeen = ++_clock;
        }
    }

    public static SeenStream[] Snapshot()
    {
        lock (_gate)
        {
            return _seen.Select(s => s.Clone()).ToArray();
        }
    }

    public static void Clear()
    {
        lock (_gate)
        {
            _seen.Clear();
            _current = null;
        }
    }
}
