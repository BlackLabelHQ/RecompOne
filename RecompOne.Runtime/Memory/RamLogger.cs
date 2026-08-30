using System.Numerics;

namespace RecompOne.Runtime.Memory;

//to make a ram map similar do pcsxRedux's
public sealed class RamLogger
{
    public const int Width = 2048;
    public const int Height = 1024;

    private readonly uint[] _writeTimestamps = new uint[Width * Height];
    private readonly uint[] _readTimestamps = new uint[Width * Height];
    private uint _cycle;

    public static bool TrackReads;
    public static bool TrackWrites;

    public float DecayFrames = 90f;
    public Vector4 BackdropColor = new(0.25f, 0.15f, 0.15f, 1f);
    public Vector4 WriteColor = new(1f, 0f, 0f, 0.75f);
    public Vector4 ReadColor = new(0.3f, 0.5f, 1f, 0.75f);
    public bool ShowGreyscale = true;

    public uint Cycle => _cycle;

    public void Tick()
    {
        _cycle++;
    }

    public uint GetWriteStamp(int byteIdx)
    {
        return (uint)byteIdx < (uint)_writeTimestamps.Length ? _writeTimestamps[byteIdx] : 0u;
    }


    public uint GetReadStamp(int byteIdx)
    {
        return (uint)byteIdx < (uint)_readTimestamps.Length ? _readTimestamps[byteIdx] : 0u;
    }

    private float HeatOf(uint ts)
    {
        if (ts == 0) return 0f;
        var age = _cycle - ts;
        var half = MathF.Max(1f, DecayFrames);
        if (age > half * 16f) return 0f;
        return MathF.Exp(-age * 0.6931472f / half);
    }

    public float HeatAt(int byteIdx)
    {
        return HeatOf(GetWriteStamp(byteIdx));
    }

    public float ReadHeatAt(int byteIdx)
    {
        return HeatOf(GetReadStamp(byteIdx));
    }

    public void RecordWrite(uint physAddr, int bytes)
    {
        for (var i = 0; i < bytes; i++)
        {
            var idx = (int)((physAddr + (uint)i) & 0x1FFFFF);
            if (idx < _writeTimestamps.Length) _writeTimestamps[idx] = _cycle;
        }
    }

    //rd for show
    public void RecordRead(uint physAddr, int bytes)
    {
        for (var i = 0; i < bytes; i++)
        {
            var idx = (int)((physAddr + (uint)i) & 0x1FFFFF);
            if (idx < _readTimestamps.Length) _readTimestamps[idx] = _cycle;
        }
    }

    public void BuildTexture(ReadOnlySpan<byte> ram, byte[] output)
    {
        var total = Width * Height;
        float br = BackdropColor.X, bg = BackdropColor.Y, bb = BackdropColor.Z;
        float wr = WriteColor.X, wg = WriteColor.Y, wb = WriteColor.Z, wa = WriteColor.W;
        float rr = ReadColor.X, rg = ReadColor.Y, rb = ReadColor.Z, ra = ReadColor.W;

        var half = MathF.Max(1f, DecayFrames);
        var k = -0.6931472f / half;
        var cutoff = half * 16f;
        var cycle = _cycle;

        for (var i = 0; i < total; i++)
        {
            var b = i < ram.Length ? ram[i] : (byte)0;
            var shade = ShowGreyscale ? 1f - b / 255f : 1f;

            float r = br * shade, g = bg * shade, bl = bb * shade;

            float rHeat = 0f, wHeat = 0f;
            var rts = _readTimestamps[i];
            if (rts != 0)
            {
                var age = cycle - rts;
                if (age <= cutoff) rHeat = MathF.Exp(age * k);
            }

            var wts = _writeTimestamps[i];
            if (wts != 0)
            {
                var age = cycle - wts;
                if (age <= cutoff) wHeat = MathF.Exp(age * k);
            }

            var totalHeat = rHeat * ra + wHeat * wa;
            if (totalHeat > 0.0001f)
            {
                var inv = 1f / totalHeat;
                var hr = (rHeat * rr * ra + wHeat * wr * wa) * inv;
                var hg = (rHeat * rg * ra + wHeat * wg * wa) * inv;
                var hb = (rHeat * rb * ra + wHeat * wb * wa) * inv;
                var blend = totalHeat > 1f ? 1f : totalHeat;
                r += (hr - r) * blend;
                g += (hg - g) * blend;
                bl += (hb - bl) * blend;
            }

            var o = i << 2;
            output[o] = (byte)(r * 255);
            output[o + 1] = (byte)(g * 255);
            output[o + 2] = (byte)(bl * 255);
            output[o + 3] = 255;
        }
    }
}