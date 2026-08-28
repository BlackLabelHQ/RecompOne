using System.Numerics;

namespace RecompOne.Runtime.Memory;

//to make a ram map similar do pcsxRedux's
public sealed class RamLogger
{
    public const int Width = 2048;
    public const int Height = 1024; 

    readonly uint[] _writeTimestamps = new uint[Width * Height];
    readonly uint[] _readTimestamps = new uint[Width * Height];
    uint _cycle;

    public static bool TrackReads;
    public static bool TrackWrites;

    public float DecayFrames = 90f;
    public Vector4 BackdropColor = new(0.25f, 0.15f, 0.15f, 1f);
    public Vector4 WriteColor = new(1f, 0f, 0f, 0.75f);
    public Vector4 ReadColor = new(0.3f, 0.5f, 1f, 0.75f);
    public bool ShowGreyscale = true;

    public uint Cycle => _cycle;
    public void Tick() => _cycle++;

    public uint GetWriteStamp(int byteIdx) => (uint)byteIdx < (uint)_writeTimestamps.Length ? _writeTimestamps[byteIdx] : 0u;

    
    public uint GetReadStamp(int byteIdx) =>
        (uint)byteIdx < (uint)_readTimestamps.Length ? _readTimestamps[byteIdx] : 0u;

    float HeatOf(uint ts)
    {
        if (ts == 0) return 0f;
        uint age = _cycle - ts;
        float half = MathF.Max(1f, DecayFrames);
        if (age > half * 16f) return 0f;
        return MathF.Exp(-age * 0.6931472f / half);
    }

    public float HeatAt(int byteIdx) => HeatOf(GetWriteStamp(byteIdx));

    public float ReadHeatAt(int byteIdx) => HeatOf(GetReadStamp(byteIdx));

    public void RecordWrite(uint physAddr, int bytes)
    {
        for (int i = 0; i < bytes; i++)
        {
            int idx = (int)((physAddr + (uint)i) & 0x1FFFFF);
            if (idx < _writeTimestamps.Length) _writeTimestamps[idx] = _cycle;
        }
    }

    //rd for show
    public void RecordRead(uint physAddr, int bytes)
    {
        for (int i = 0; i < bytes; i++)
        {
            int idx = (int)((physAddr + (uint)i) & 0x1FFFFF);
            if (idx < _readTimestamps.Length) _readTimestamps[idx] = _cycle;
        }
    }

    public void BuildTexture(ReadOnlySpan<byte> ram, byte[] output)
    {
        int total = Width * Height;
        float br = BackdropColor.X, bg = BackdropColor.Y, bb = BackdropColor.Z;
        float wr = WriteColor.X, wg = WriteColor.Y, wb = WriteColor.Z, wa = WriteColor.W;
        float rr = ReadColor.X, rg = ReadColor.Y, rb = ReadColor.Z, ra = ReadColor.W;

        float half = MathF.Max(1f, DecayFrames);
        float k = -0.6931472f / half;
        float cutoff = half * 16f;
        uint cycle = _cycle;

        for (int i = 0; i < total; i++)
        {
            byte b = i < ram.Length ? ram[i] : (byte)0;
            float shade = ShowGreyscale ? 1f - b / 255f : 1f;

            float r = br * shade, g = bg * shade, bl = bb * shade;

            float rHeat = 0f, wHeat = 0f;
            uint rts = _readTimestamps[i];
            if (rts != 0)
            {
                uint age = cycle - rts;
                if (age <= cutoff) rHeat = MathF.Exp(age * k);
            }
            uint wts = _writeTimestamps[i];
            if (wts != 0)
            {
                uint age = cycle - wts;
                if (age <= cutoff) wHeat = MathF.Exp(age * k);
            }

            float totalHeat = rHeat * ra + wHeat * wa;
            if (totalHeat > 0.0001f)
            {
                float inv = 1f / totalHeat;
                float hr = (rHeat * rr * ra + wHeat * wr * wa) * inv;
                float hg = (rHeat * rg * ra + wHeat * wg * wa) * inv;
                float hb = (rHeat * rb * ra + wHeat * wb * wa) * inv;
                float blend = totalHeat > 1f ? 1f : totalHeat;
                r += (hr - r) * blend;
                g += (hg - g) * blend;
                bl += (hb - bl) * blend;
            }

            int o = i << 2;
            output[o] = (byte)(r  * 255);
            output[o + 1] = (byte)(g  * 255);
            output[o + 2] = (byte)(bl * 255);
            output[o + 3] = 255;
        }
    }
}
