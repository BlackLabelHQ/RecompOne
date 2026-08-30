namespace RecompOne.Runtime.Assets.Decoders;

public sealed class WavDecoder : IPcmDecoder
{
    private readonly Stream _s;
    private readonly long _dataStart;
    private readonly long _dataBytes;
    private readonly int _bits;
    private readonly bool _float;
    private readonly int _blockAlign;
    private readonly byte[] _buf;

    private long _framePos;

    public int SampleRate { get; }
    public int Channels { get; }
    public long TotalFrames { get; }

    public WavDecoder(Stream s)
    {
        _s = s;
        _s.Position = 0;

        Span<byte> hdr = stackalloc byte[12];
        ReadExact(hdr);
        if (hdr[0] != 'R' || hdr[1] != 'I' || hdr[2] != 'F' || hdr[3] != 'F' ||
            hdr[8] != 'W' || hdr[9] != 'A' || hdr[10] != 'V' || hdr[11] != 'E')
            throw new InvalidDataException("not a RIFF/WAVE file");

        int fmtTag = 0, channels = 0, rate = 0, bits = 0;
        long dataStart = -1, dataBytes = 0;

        Span<byte> ch = stackalloc byte[8];
        while (_s.Position + 8 <= _s.Length)
        {
            ReadExact(ch);
            var id = $"{(char)ch[0]}{(char)ch[1]}{(char)ch[2]}{(char)ch[3]}";
            var size = BitConverter.ToUInt32(ch[4..]);
            var next = _s.Position + size + (size & 1);

            if (id == "fmt ")
            {
                var fmt = new byte[Math.Min(size, 40)];
                ReadExact(fmt);
                fmtTag = BitConverter.ToUInt16(fmt, 0);
                channels = BitConverter.ToUInt16(fmt, 2);
                rate = BitConverter.ToInt32(fmt, 4);
                bits = BitConverter.ToUInt16(fmt, 14);
                if (fmtTag == 0xFFFE && fmt.Length >= 26)
                    fmtTag = BitConverter.ToUInt16(fmt, 24);
            }
            else if (id == "data")
            {
                dataStart = _s.Position;
                dataBytes = size;
            }

            if (next <= _s.Position) break;
            _s.Position = next;
        }

        if (dataStart < 0 || channels <= 0 || rate <= 0 || bits <= 0)
            throw new InvalidDataException("malformed WAVE: it missing fmt/data");
        if (fmtTag != 1 && fmtTag != 3)
            throw new InvalidDataException($"unsupported WAVE format tag {fmtTag} (only PCM and IEEE float)");
        if (bits != 8 && bits != 16 && bits != 24 && bits != 32)
            throw new InvalidDataException($"unsupported WAVE bti depth {bits}");

        _float = fmtTag == 3;
        _bits = bits;
        _dataStart = dataStart;
        _blockAlign = channels * (bits / 8);
        _dataBytes = Math.Min(dataBytes, _s.Length - dataStart);

        SampleRate = rate;
        Channels = channels;
        TotalFrames = _dataBytes / _blockAlign;

        _buf = new byte[_blockAlign * 1024];
        _s.Position = _dataStart;
    }

    private void ReadExact(Span<byte> dst)
    {
        var done = 0;
        while (done < dst.Length)
        {
            var n = _s.Read(dst[done..]);
            if (n <= 0) throw new EndOfStreamException();
            done += n;
        }
    }

    public int ReadFrames(short[] dst, int frames)
    {
        var produced = 0;
        while (produced < frames)
        {
            var want = Math.Min(frames - produced, _buf.Length / _blockAlign);
            var remaining = TotalFrames - _framePos;
            if (remaining <= 0) break;
            if (want > remaining) want = (int)remaining;

            var bytes = want * _blockAlign;
            var got = 0;
            while (got < bytes)
            {
                var n = _s.Read(_buf, got, bytes - got);
                if (n <= 0) break;
                got += n;
            }

            var gotFrames = got / _blockAlign;
            if (gotFrames <= 0) break;

            Convert(_buf, gotFrames, dst, produced * Channels);
            produced += gotFrames;
            _framePos += gotFrames;
        }

        return produced;
    }

    private void Convert(byte[] src, int frames, short[] dst, int dstIndex)
    {
        var n = frames * Channels;
        switch (_bits)
        {
            case 8:
                for (var i = 0; i < n; i++)
                    dst[dstIndex + i] = (short)((src[i] - 128) << 8);
                break;
            case 16:
                for (var i = 0; i < n; i++)
                    dst[dstIndex + i] = BitConverter.ToInt16(src, i * 2);
                break;
            case 24:
                for (var i = 0; i < n; i++)
                {
                    var v = src[i * 3] | (src[i * 3 + 1] << 8) | ((sbyte)src[i * 3 + 2] << 16);
                    dst[dstIndex + i] = (short)(v >> 8);
                }

                break;
            default:
                if (_float)
                    for (var i = 0; i < n; i++)
                    {
                        var f = BitConverter.ToSingle(src, i * 4);
                        var v = (int)(f * 32767f);
                        dst[dstIndex + i] = (short)(v < -32768 ? -32768 : v > 32767 ? 32767 : v);
                    }
                else
                    for (var i = 0; i < n; i++)
                        dst[dstIndex + i] = (short)(BitConverter.ToInt32(src, i * 4) >> 16);

                break;
        }
    }

    public void SeekFrames(long frame)
    {
        if (frame < 0) frame = 0;
        if (frame > TotalFrames) frame = TotalFrames;
        _framePos = frame;
        _s.Position = _dataStart + frame * _blockAlign;
    }

    public void Dispose()
    {
        _s.Dispose();
    }
}