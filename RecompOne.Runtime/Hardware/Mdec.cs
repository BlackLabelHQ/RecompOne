namespace RecompOne.Runtime;

public sealed class Mdec
{
    private static readonly int[] Zigzag =
    {
        0, 1, 8, 16, 9, 2, 3, 10,
        17, 24, 32, 25, 18, 11, 4, 5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63
    };

    private static readonly short[] DefaultScale =
    {
        0x5A82, 0x5A82, 0x5A82, 0x5A82, 0x5A82, 0x5A82, 0x5A82, 0x5A82,
        0x7D8A, 0x6A6D, 0x471C, 0x18F8, unchecked((short)0xE707), unchecked((short)0xB8E3), unchecked((short)0x9592),
        unchecked((short)0x8275),
        0x7641, 0x30FB, unchecked((short)0xCF04), unchecked((short)0x89BE), unchecked((short)0x89BE),
        unchecked((short)0xCF04), 0x30FB, 0x7641,
        0x6A6D, unchecked((short)0xE707), unchecked((short)0x8275), unchecked((short)0xB8E3), 0x471C, 0x7D8A, 0x18F8,
        unchecked((short)0x9592),
        0x5A82, unchecked((short)0xA57D), unchecked((short)0xA57D), 0x5A82, 0x5A82, unchecked((short)0xA57D),
        unchecked((short)0xA57D), 0x5A82,
        0x471C, unchecked((short)0x8275), 0x18F8, 0x6A6D, unchecked((short)0x9592), unchecked((short)0xE707), 0x7D8A,
        unchecked((short)0xB8E3),
        0x30FB, unchecked((short)0x89BE), 0x7641, unchecked((short)0xCF04), unchecked((short)0xCF04), 0x7641,
        unchecked((short)0x89BE), 0x30FB,
        0x18F8, unchecked((short)0xB8E3), 0x6A6D, unchecked((short)0x8275), 0x7D8A, unchecked((short)0x9592), 0x471C,
        unchecked((short)0xE707)
    };

    private readonly byte[] _quantLuma = new byte[64];
    private readonly byte[] _quantChroma = new byte[64];
    private readonly short[] _scale = new short[64];

    private int _depth;
    private bool _signed;
    private bool _bit15;
    private bool _enableIn;
    private bool _enableOut;

    private enum Mode
    {
        Idle,
        Decode,
        Quant,
        Scale
    }

    private Mode _mode = Mode.Idle;
    private int _paramsRemaining = -1;

    private readonly List<ushort> _inHalfwords = new();
    private readonly List<byte> _tableBytes = new();
    private bool _quantColor;

    private readonly Queue<uint> _out = new();
    private int _readPos;

    public Mdec()
    {
        Array.Copy(DefaultScale, _scale, 64);
    }

    public uint ReadStatus()
    {
        uint stat = 0;
        if (_out.Count == 0) stat |= 1u << 31;
        if (_mode != Mode.Idle) stat |= 1u << 29;
        if (_enableIn && _paramsRemaining > 0) stat |= 1u << 28;
        if (_enableOut && _out.Count > 0) stat |= 1u << 27;
        stat |= (uint)(_depth & 3) << 25;
        if (_signed) stat |= 1u << 24;
        if (_bit15) stat |= 1u << 23;
        var remaining = _paramsRemaining > 0 ? (uint)(_paramsRemaining - 1) : 0xFFFFu;
        stat |= remaining & 0xFFFFu;
        return stat;
    }

    public uint ReadData()
    {
        return _out.Count > 0 ? _out.Dequeue() : 0u;
    }

    public bool OutEmpty => _out.Count == 0;

    public void WriteControl(uint value)
    {
        if ((value & (1u << 31)) != 0)
        {
            _mode = Mode.Idle;
            _paramsRemaining = -1;
            _inHalfwords.Clear();
            _tableBytes.Clear();
            _out.Clear();
            _enableIn = _enableOut = false;
            return;
        }

        _enableIn = (value & (1u << 30)) != 0;
        _enableOut = (value & (1u << 29)) != 0;
    }

    public void Write0(uint word)
    {
        if (_mode == Mode.Idle)
        {
            BeginCommand(word);
            return;
        }

        switch (_mode)
        {
            case Mode.Decode:
                _inHalfwords.Add((ushort)word);
                _inHalfwords.Add((ushort)(word >> 16));
                break;
            case Mode.Quant:
            case Mode.Scale:
                _tableBytes.Add((byte)word);
                _tableBytes.Add((byte)(word >> 8));
                _tableBytes.Add((byte)(word >> 16));
                _tableBytes.Add((byte)(word >> 24));
                break;
        }

        if (--_paramsRemaining > 0) return;

        if (_mode == Mode.Decode) DecodeAll();
        else if (_mode == Mode.Quant) LoadQuant();
        else if (_mode == Mode.Scale) LoadScale();

        _mode = Mode.Idle;
        _paramsRemaining = -1;
    }

    private void BeginCommand(uint word)
    {
        var cmd = (word >> 29) & 7;
        switch (cmd)
        {
            case 1:
                _depth = (int)((word >> 27) & 3);
                _signed = (word & (1u << 26)) != 0;
                _bit15 = (word & (1u << 25)) != 0;
                _paramsRemaining = (int)(word & 0xFFFF);
                _inHalfwords.Clear();
                _mode = Mode.Decode;
                break;
            case 2:
                _quantColor = (word & 1) != 0;
                _paramsRemaining = _quantColor ? 32 : 16;
                _tableBytes.Clear();
                _mode = Mode.Quant;
                break;
            case 3:
                _paramsRemaining = 32;
                _tableBytes.Clear();
                _mode = Mode.Scale;
                break;
            default:
                _depth = (int)((word >> 27) & 3);
                _signed = (word & (1u << 26)) != 0;
                _bit15 = (word & (1u << 25)) != 0;
                break;
        }
    }

    private void LoadQuant()
    {
        for (var i = 0; i < 64; i++) _quantLuma[i] = _tableBytes[i];
        if (_quantColor)
            for (var i = 0; i < 64; i++)
                _quantChroma[i] = _tableBytes[64 + i];
    }

    private void LoadScale()
    {
        for (var i = 0; i < 64; i++)
            _scale[i] = (short)(_tableBytes[i * 2] | (_tableBytes[i * 2 + 1] << 8));
    }

    private void DecodeAll()
    {
        _readPos = 0;
        var color = _depth >= 2;
        var crBlock = new int[64];
        var cbBlock = new int[64];
        var yBlock = new int[64];
        var mbStart = _out.Count;
        var mbCount = 0;

        while (_readPos < _inHalfwords.Count)
            if (color)
            {
                var rgb = new byte[16 * 16 * 4];
                if (!DecodeBlock(_quantChroma, crBlock)) break;
                DecodeBlock(_quantChroma, cbBlock);
                for (var q = 0; q < 4; q++)
                {
                    DecodeBlock(_quantLuma, yBlock);
                    YuvToRgb(crBlock, cbBlock, yBlock, (q & 1) * 8, (q >> 1) * 8, rgb);
                }

                PushColor(rgb);
                mbCount++;
            }
            else
            {
                if (!DecodeBlock(_quantLuma, yBlock)) break;
                PushMono(yBlock);
                mbCount++;
            }

        Log.Mdec(
            $"decode depth={_depth} signed={_signed} bit15={_bit15} inHW={_inHalfwords.Count} consumedHW={_readPos} mbs={mbCount} wordsOut={_out.Count - mbStart} outTotal={_out.Count}");
    }

    private ushort NextHalfword()
    {
        return _readPos < _inHalfwords.Count ? _inHalfwords[_readPos++] : (ushort)0xFE00;
    }

    private bool DecodeBlock(byte[] qt, int[] block)
    {
        Array.Clear(block);

        var n = NextHalfword();
        while (n == 0xFE00 && _readPos < _inHalfwords.Count) n = NextHalfword();

        if (n == 0xFE00) return false;

        var qScale = (n >> 10) & 0x3F;
        var val = Signed10(n) * qt[0];
        var k = 0;

        while (true)
        {
            if (qScale == 0) val = Signed10(n) * 2;
            val = Math.Clamp(val, -0x400, 0x3FF);
            block[Zigzag[k]] = val;
            n = NextHalfword();
            if (n == 0xFE00) break;
            k += ((n >> 10) & 0x3F) + 1;
            if (k > 63) break;
            val = (Signed10(n) * qt[k] * qScale + 4) / 8;
        }

        IdctCore(block);
        return true;
    }

    private void IdctCore(int[] blk)
    {
        var tmp = new long[64];
        for (var pass = 0; pass < 2; pass++)
        {
            for (var x = 0; x < 8; x++)
            for (var y = 0; y < 8; y++)
            {
                long sum = 0;
                for (var z = 0; z < 8; z++)
                    sum += (long)blk[y + z * 8] * (_scale[x + z * 8] / 8);
                tmp[x + y * 8] = (sum + 0xFFF) / 0x2000;
            }

            for (var i = 0; i < 64; i++) blk[i] = (int)tmp[i];
        }
    }

    private void YuvToRgb(int[] cr, int[] cb, int[] y, int xx, int yy, byte[] dst)
    {
        for (var py = 0; py < 8; py++)
        for (var px = 0; px < 8; px++)
        {
            var c = (px + xx) / 2 + (py + yy) / 2 * 8;
            double r = cr[c];
            double b = cb[c];
            var g = -0.3437 * b - 0.7143 * r;
            r = 1.402 * r;
            b = 1.772 * b;
            var yv = y[px + py * 8];
            var ri = Math.Clamp((int)Math.Round(yv + r), -128, 127);
            var gi = Math.Clamp((int)Math.Round(yv + g), -128, 127);
            var bi = Math.Clamp((int)Math.Round(yv + b), -128, 127);
            if (!_signed)
            {
                ri ^= 0x80;
                gi ^= 0x80;
                bi ^= 0x80;
            }

            var o = (px + xx + (py + yy) * 16) * 4;
            dst[o + 0] = (byte)ri;
            dst[o + 1] = (byte)gi;
            dst[o + 2] = (byte)bi;
        }
    }

    private void PushColor(byte[] rgb)
    {
        if (_depth == 3)
        {
            var px = new ushort[256];
            for (var i = 0; i < 256; i++)
            {
                var r = rgb[i * 4 + 0] >> 3;
                var g = rgb[i * 4 + 1] >> 3;
                var b = rgb[i * 4 + 2] >> 3;
                var v = (ushort)(r | (g << 5) | (b << 10));
                if (_bit15) v |= 0x8000;
                px[i] = v;
            }

            for (var i = 0; i < 256; i += 2)
                _out.Enqueue((uint)(px[i] | (px[i + 1] << 16)));
        }
        else
        {
            var bytes = new byte[256 * 3];
            for (var i = 0; i < 256; i++)
            {
                bytes[i * 3 + 0] = rgb[i * 4 + 0];
                bytes[i * 3 + 1] = rgb[i * 4 + 1];
                bytes[i * 3 + 2] = rgb[i * 4 + 2];
            }

            PackBytes(bytes);
        }
    }

    private void PushMono(int[] y)
    {
        var bytes = new byte[_depth == 0 ? 32 : 64];
        if (_depth == 1)
            for (var i = 0; i < 64; i++)
            {
                var v = Math.Clamp(y[i], -128, 127);
                if (!_signed) v ^= 0x80;
                bytes[i] = (byte)v;
            }
        else
            for (var i = 0; i < 64; i++)
            {
                var v = Math.Clamp(y[i], -128, 127);
                if (!_signed) v ^= 0x80;
                var nib = (v & 0xFF) >> 4;
                if ((i & 1) == 0) bytes[i / 2] = (byte)nib;
                else bytes[i / 2] |= (byte)(nib << 4);
            }

        PackBytes(bytes);
    }

    private void PackBytes(byte[] bytes)
    {
        for (var i = 0; i < bytes.Length; i += 4)
            _out.Enqueue((uint)(bytes[i] | (bytes[i + 1] << 8) | (bytes[i + 2] << 16) | (bytes[i + 3] << 24)));
    }

    private static int Signed10(ushort n)
    {
        var v = n & 0x3FF;
        return (v & 0x200) != 0 ? v - 0x400 : v;
    }
}