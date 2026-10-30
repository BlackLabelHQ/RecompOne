using RecompOne.Runtime.Assets.Decoders;

namespace RecompOne.Runtime.Assets;

public sealed class AudioClip
{
    public short[] Pcm = [];
    public int Channels = 1;
    public int Rate = 44100;

    public int Frames => Channels > 0 ? Pcm.Length / Channels : 0;
    public double Seconds => Rate > 0 ? Frames / (double)Rate : 0;

    public static AudioClip Decode(string name, byte[] data)
    {
        using var decoder = PcmDecoderFactory.Open(name, data);
        var channels = Math.Max(1, decoder.Channels);
        var chunk = new short[4096 * channels];
        var all = new List<short>(decoder.TotalFrames > 0 ? (int)Math.Min(decoder.TotalFrames * channels, int.MaxValue / 2) : 65536);

        while (true)
        {
            var frames = decoder.ReadFrames(chunk, 4096);
            if (frames <= 0) break;
            for (var i = 0; i < frames * channels; i++) all.Add(chunk[i]);
        }

        return new AudioClip { Pcm = all.ToArray(), Channels = channels, Rate = decoder.SampleRate };
    }

    public static AudioClip? TryLoad(string path)
    {
        try
        {
            return Decode(path, File.ReadAllBytes(path));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[assets] cannot decode '{path}': {ex.Message}");
            return null;
        }
    }

    public short[] Mono()
    {
        if (Channels == 1) return Pcm;

        var mono = new short[Frames];
        for (var f = 0; f < mono.Length; f++)
        {
            var sum = 0;
            for (var c = 0; c < Channels; c++) sum += Pcm[f * Channels + c];
            mono[f] = (short)(sum / Channels);
        }

        return mono;
    }
}
