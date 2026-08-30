using NVorbis;

namespace RecompOne.Runtime.Assets.Decoders;

public sealed class VorbisDecoder : IPcmDecoder
{
    private readonly VorbisReader _r;
    private float[] _scratch = [];

    public int SampleRate { get; }
    public int Channels { get; }
    public long TotalFrames { get; }

    public VorbisDecoder(Stream s)
    {
        _r = new VorbisReader(s, true);
        SampleRate = _r.SampleRate;
        Channels = _r.Channels;
        TotalFrames = _r.TotalSamples;
    }

    public int ReadFrames(short[] dst, int frames)
    {
        var need = frames * Channels;
        if (_scratch.Length < need) _scratch = new float[need];

        var got = _r.ReadSamples(_scratch, 0, need);
        for (var i = 0; i < got; i++)
        {
            var v = (int)(_scratch[i] * 32767f);
            dst[i] = (short)(v < -32768 ? -32768 : v > 32767 ? 32767 : v);
        }

        return got / Channels;
    }

    public void SeekFrames(long frame)
    {
        if (frame < 0) frame = 0;
        if (frame > TotalFrames) frame = TotalFrames;
        try
        {
            _r.SeekTo(frame);
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }

    public void Dispose()
    {
        _r.Dispose();
    }
}