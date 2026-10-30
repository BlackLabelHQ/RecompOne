namespace RecompOne.Runtime.Assets.Sfx;

public sealed class SeenSample
{
    public ulong Hash;
    public int Bytes;
    public byte[] Adpcm = [];
    public bool Loops;
    public int Pitch;
    public int Hits;
    public long FirstSeen;
    public long LastSeen;

    public int Samples => Bytes / 16 * SpuAdpcm.SamplesPerBlock;
    public int NaturalRate => SpuAdpcm.NaturalRate(Pitch);
    public double Seconds => Samples / (double)NaturalRate;
}

public static class SampleRegistry
{
    private const int MaxEntries = 2048;

    private static readonly Dictionary<ulong, SeenSample> _seen = [];
    private static readonly object _gate = new();
    private static long _clock;

    public static volatile bool Enabled;

    public static int Count
    {
        get
        {
            lock (_gate)
            {
                return _seen.Count;
            }
        }
    }

    public static void Note(ulong hash, byte[] ram, uint start, int bytes, bool loops, int pitch)
    {
        if (!Enabled) return;

        lock (_gate)
        {
            if (_seen.TryGetValue(hash, out var known))
            {
                known.Hits++;
                known.Pitch = pitch;
                known.LastSeen = ++_clock;
                return;
            }

            if (_seen.Count >= MaxEntries)
                foreach (var (key, _) in _seen.OrderBy(p => p.Value.LastSeen).Take(MaxEntries / 8).ToArray())
                    _seen.Remove(key);

            _seen[hash] = new SeenSample
            {
                Hash = hash,
                Bytes = bytes,
                Adpcm = SpuAdpcm.Copy(ram, start, bytes),
                Loops = loops,
                Pitch = pitch,
                Hits = 1,
                FirstSeen = ++_clock,
                LastSeen = _clock
            };
        }
    }

    public static SeenSample[] Snapshot()
    {
        lock (_gate)
        {
            return _seen.Values.ToArray();
        }
    }

    public static SeenSample? Get(ulong hash)
    {
        lock (_gate)
        {
            return _seen.GetValueOrDefault(hash);
        }
    }

    public static void Clear()
    {
        lock (_gate)
        {
            _seen.Clear();
        }
    }
}
