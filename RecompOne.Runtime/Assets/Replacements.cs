using RecompOne.Runtime.Assets.Decoders;

namespace RecompOne.Runtime.Assets;

public enum ShortPolicy : byte
{
    Silence = 0,
    Loop = 1,
    EndStream = 2
}

public sealed class XaOptions
{
    public float Gain = 1f;
    public bool Loop;
    public double LoopStart;
    public double LoopEnd;
    public bool Extend;
    public int ExtendMaxMs = 600000;
    public ShortPolicy OnShorter = ShortPolicy.Silence;
    public int TailMs;
    public bool AllowStr;

    public XaOptions Clone()
    {
        return (XaOptions)MemberwiseClone();
    }
}

public sealed class ReplacementStream : IDisposable
{
    private readonly IPcmDecoder _dec;
    private readonly XaOptions _opt;
    private readonly short[] _src;
    private readonly int _srcChannels;

    private int _srcCount, _srcIndex;
    private double _frac;
    private int _outRate;
    private double _step;

    private short _l0, _r0, _l1, _r1;
    private bool _primed;
    private bool _ended;

    private long _framesOut;

    public string Name { get; }
    public bool Ended => _ended;
    public XaOptions Options => _opt;
    public long FramesProduced => _framesOut;
    public double PositionSeconds => _outRate > 0 ? _framesOut / (double)_outRate : 0;

    public ReplacementStream(string name, IPcmDecoder decoder, XaOptions options)
    {
        Name = name;
        _dec = decoder;
        _opt = options;
        _srcChannels = decoder.Channels < 1 ? 1 : decoder.Channels;
        _src = new short[4096 * _srcChannels];
    }

    public void SeekSeconds(double seconds)
    {
        if (seconds < 0) seconds = 0;
        _dec.SeekFrames((long)(seconds * _dec.SampleRate));
        _srcCount = _srcIndex = 0;
        _frac = 0;
        _primed = false;
        _ended = false;
        _framesOut = (long)(seconds * (_outRate > 0 ? _outRate : _dec.SampleRate));
    }

    private bool NextSourceFrame(out short l, out short r)
    {
        if (_srcIndex >= _srcCount)
        {
            var frames = _dec.ReadFrames(_src, _src.Length / _srcChannels);
            if (frames <= 0)
            {
                if (_opt.Loop)
                {
                    _dec.SeekFrames((long)(_opt.LoopStart * _dec.SampleRate));
                    frames = _dec.ReadFrames(_src, _src.Length / _srcChannels);
                }

                if (frames <= 0)
                {
                    l = r = 0;
                    _ended = true;
                    return false;
                }
            }

            _srcCount = frames;
            _srcIndex = 0;
        }

        var b = _srcIndex * _srcChannels;
        _srcIndex++;

        if (_srcChannels == 1)
        {
            l = r = _src[b];
        }
        else
        {
            l = _src[b];
            r = _src[b + 1];
        }

        if (_opt.Gain != 1f)
        {
            l = Sat((int)(l * _opt.Gain));
            r = Sat((int)(r * _opt.Gain));
        }

        return true;
    }

    private static short Sat(int v)
    {
        return (short)(v < -32768 ? -32768 : v > 32767 ? 32767 : v);
    }

    public int ReadPacked(int[] dst, int frames, int outRate)
    {
        return ReadPacked(dst, 0, frames, outRate);
    }

    public int ReadPacked(int[] dst, int offset, int frames, int outRate)
    {
        if (outRate <= 0) return 0;
        if (outRate != _outRate)
        {
            _outRate = outRate;
            _step = _dec.SampleRate / (double)outRate;
        }

        if (!_primed)
        {
            if (!NextSourceFrame(out _l1, out _r1)) return 0;
            _l0 = _l1;
            _r0 = _r1;
            _primed = true;
            _frac = 0;
        }

        var produced = 0;
        while (produced < frames)
        {
            while (_frac >= 1.0)
            {
                _l0 = _l1;
                _r0 = _r1;
                if (!NextSourceFrame(out _l1, out _r1))
                {
                    _l1 = _r1 = 0;
                    if (_ended) return produced;
                }

                _frac -= 1.0;
            }

            var l = (short)(_l0 + (_l1 - _l0) * _frac);
            var r = (short)(_r0 + (_r1 - _r0) * _frac);
            dst[offset + produced] = (ushort)l | (r << 16);
            produced++;
            _frac += _step;
        }

        _framesOut += produced;
        return produced;
    }

    public void Dispose()
    {
        _dec.Dispose();
    }
}

public sealed class ReplacementSample
{
    public short[] Pcm = [];
    public int Channels = 1;
    public int SampleRate = 44100;
    public int LoopStart, LoopEnd;
    public bool Loops;
    public float Gain = 1f;
}

public enum TextureMode : byte
{
    Rgba = 0,
    Indexed = 1
}

public sealed class ReplacementTexture
{
    public int Width, Height;
    public float ScaleX = 1f, ScaleY = 1f;
    public byte[] Rgba = [];
    public TextureMode Mode = TextureMode.Rgba;
    public uint GpuHandle;
    public bool Failed;
}

public sealed class ReplacementClut
{
    public int Count;
    public byte[] Rgba = [];
    public uint GpuHandle;
    public bool Failed;
}