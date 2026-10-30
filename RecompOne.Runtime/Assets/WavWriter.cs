namespace RecompOne.Runtime.Assets;

public static class WavWriter
{
    public static void Write(string path, AudioClip clip)
    {
        Write(path, clip.Pcm, clip.Channels, clip.Rate);
    }

    public static void Write(string path, ReadOnlySpan<short> pcm, int channels, int rate)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var stream = File.Create(path);
        using var w = new BinaryWriter(stream);
        var data = pcm.Length * 2;
        w.Write("RIFF"u8);
        w.Write(36 + data);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)channels);
        w.Write(rate);
        w.Write(rate * channels * 2);
        w.Write((short)(channels * 2));
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(data);
        foreach (var s in pcm) w.Write(s);
    }
}
