namespace RecompOne.Runtime;

public static class XaAudio
{
    private static readonly int[] Pos = { 0, 60, 115, 98 };
    private static readonly int[] Neg = { 0, 0, -52, -55 };

    private const int Capacity = 1 << 18;
    private const int Mask = Capacity - 1;
    private const int PrimeFrames = 1024;
    private const int MaxHold = 8192;

    private static readonly int[] _ring = new int[Capacity];
    private static int _writeIdx, _readIdx, _count;
    private static readonly object _gate = new();

    private static int _oldL, _olderL, _oldR, _olderR;
    private static int _srcRate = 37800;
    private static bool _playing;

    private static double _pos;
    private static short _s0L, _s0R, _s1L, _s1R;
    private static int _underrun;

    public static void Reset()
    {
        lock (_gate)
        {
            _oldL = _olderL = _oldR = _olderR = 0;
            _writeIdx = _readIdx = _count = 0;
            _playing = false;
            _pos = 0;
            _s0L = _s0R = _s1L = _s1R = 0;
            _underrun = 0;
        }
    }

    private static int Clamp(int v)
    {
        return v < -32768 ? -32768 : v > 32767 ? 32767 : v;
    }

    private static void DecodeBlock(byte[] sec, int b, int blk, ref int old, ref int older, int[] dst)
    {
        var hdr = sec[b + 4 + blk];
        var sv = hdr & 0xF;
        if (sv > 12) sv = 9;
        var filter = (hdr >> 4) & 0x3;
        int f0 = Pos[filter], f1 = Neg[filter];
        int col = blk >> 1, nshift = (blk & 1) * 4;
        for (var j = 0; j < 28; j++)
        {
            var nib = (sec[b + 16 + 4 * j + col] >> nshift) & 0xF;
            var t = nib >= 8 ? nib - 16 : nib;
            var s = Clamp(((t << 12) >> sv) + ((old * f0 + older * f1 + 32) >> 6));
            older = old;
            old = s;
            dst[j] = s;
        }
    }

    public static void DecodeSector(byte[] sec, int off, byte coding)
    {
        var stereo = (coding & 0x01) != 0;
        var rate = (coding & 0x04) != 0 ? 18900 : 37800;
        int[] l = new int[28], r = new int[28];
        var frames = new int[stereo ? 2016 : 4032];
        var n = 0;

        for (var p = 0; p < 18; p++)
        {
            var b = off + p * 128;
            if (stereo)
                for (var tb = 0; tb < 4; tb++)
                {
                    DecodeBlock(sec, b, tb * 2, ref _oldL, ref _olderL, l);
                    DecodeBlock(sec, b, tb * 2 + 1, ref _oldR, ref _olderR, r);
                    for (var j = 0; j < 28; j++) frames[n++] = (ushort)l[j] | (r[j] << 16);
                }
            else
                for (var blk = 0; blk < 8; blk++)
                {
                    DecodeBlock(sec, b, blk, ref _oldL, ref _olderL, l);
                    for (var j = 0; j < 28; j++) frames[n++] = (ushort)l[j] | (l[j] << 16);
                }
        }

        lock (_gate)
        {
            _srcRate = rate;
            for (var i = 0; i < n; i++)
            {
                _ring[_writeIdx] = frames[i];
                _writeIdx = (_writeIdx + 1) & Mask;
                if (_count < Capacity) _count++;
                else _readIdx = (_readIdx + 1) & Mask;
            }

            if (!_playing && _count >= PrimeFrames) _playing = true;
        }
    }

    public static void PushFrames(int[] frames, int count, int rate)
    {
        lock (_gate)
        {
            _srcRate = rate;
            for (var i = 0; i < count; i++)
            {
                _ring[_writeIdx] = frames[i];
                _writeIdx = (_writeIdx + 1) & Mask;
                if (_count < Capacity) _count++;
                else _readIdx = (_readIdx + 1) & Mask;
            }

            if (!_playing && _count >= PrimeFrames) _playing = true;
        }
    }

    public static int BufferedSamples
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    private const int LowWater = 8192;

    public static bool Playing
    {
        get
        {
            lock (_gate)
            {
                return _playing;
            }
        }
    }

    public static int SourceRate
    {
        get
        {
            lock (_gate)
            {
                return _srcRate;
            }
        }
    }

    public static int NextBlock(short[] left, short[] right, int frames)
    {
        int produced;
        bool low;
        lock (_gate)
        {
            produced = frames;
            for (var i = 0; i < frames; i++)
                if (!NextLocked(out left[i], out right[i]))
                {
                    produced = i;
                    break;
                }

            low = _count < LowWater;
        }

        if (low) Sdk.LibCd.WakeXa();
        return produced;
    }

    public static bool Next(out short left, out short right)
    {
        lock (_gate)
        {
            return NextLocked(out left, out right);
        }
    }

    private static bool NextLocked(out short left, out short right)
    {
        {
            if (!_playing)
            {
                left = right = 0;
                return false;
            }

            while (_pos >= 1.0)
            {
                _s0L = _s1L;
                _s0R = _s1R;
                if (_count > 0)
                {
                    var packed = _ring[_readIdx];
                    _readIdx = (_readIdx + 1) & Mask;
                    _count--;
                    _s1L = (short)(packed & 0xFFFF);
                    _s1R = (short)(packed >> 16);
                    _underrun = 0;
                }
                else
                {
                    if (++_underrun > MaxHold)
                    {
                        _playing = false;
                        left = right = 0;
                        return false;
                    }

                    _s1L = (short)(_s1L * 31 / 32);
                    _s1R = (short)(_s1R * 31 / 32);
                }

                _pos -= 1.0;
            }

            var f = _pos;
            left = (short)(_s0L + (_s1L - _s0L) * f);
            right = (short)(_s0R + (_s1R - _s0R) * f);
            _pos += (double)_srcRate / 44100.0;
            return true;
        }
    }
}