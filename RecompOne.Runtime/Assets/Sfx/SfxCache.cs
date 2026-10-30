using System.Collections.Concurrent;

namespace RecompOne.Runtime.Assets.Sfx;

public sealed class PreparedSample
{
    public short[] Mono = [];
    public int Rate;
    public int GainQ15 = 0x8000;
    public bool PitchFollow = true;
    public int BaseRate;
    public bool? Loop;
}

public static class SfxCache
{
    private static readonly ConcurrentDictionary<ulong, PreparedSample?> _ready = new();
    private static readonly ConcurrentDictionary<ulong, byte> _loading = new();
    private static int _epoch;

    public static PreparedSample? TryGet(SfxAsset asset)
    {
        if (_ready.TryGetValue(asset.Hash, out var prepared)) return prepared;
        if (!_loading.TryAdd(asset.Hash, 0)) return null;

        var epoch = Volatile.Read(ref _epoch);
        Task.Run(() =>
        {
            var result = Prepare(asset);
            if (Volatile.Read(ref _epoch) == epoch) _ready[asset.Hash] = result;
            _loading.TryRemove(asset.Hash, out _);
        });
        return null;
    }

    public static void Clear()
    {
        Interlocked.Increment(ref _epoch);
        _ready.Clear();
        _loading.Clear();
    }

    private static PreparedSample? Prepare(SfxAsset asset)
    {
        try
        {
            var data = asset.Open();
            if (data == null) throw new FileNotFoundException(asset.FileName);

            var clip = AudioClip.Decode(asset.FileName, data);
            if (clip.Frames == 0) throw new InvalidDataException("no audio");

            return new PreparedSample
            {
                Mono = clip.Mono(),
                Rate = clip.Rate,
                GainQ15 = (int)Math.Clamp(asset.Gain * 0x8000, 0, 0x40000),
                PitchFollow = asset.PitchFollow,
                BaseRate = asset.BaseRate,
                Loop = asset.Loop
            };
        }
        catch (Exception ex)
        {
            AssetReplacerManager.Instance.Stats.SfxLoadFailures++;
            Console.Error.WriteLine($"[assets] {asset.PackId}: cannot load sound '{asset.FileName}': {ex.Message}");
            return null;
        }
    }
}
