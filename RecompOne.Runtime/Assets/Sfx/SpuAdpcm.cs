namespace RecompOne.Runtime.Assets.Sfx;

public static class SpuAdpcm
{
    public const int MaxSampleBytes = 128 * 1024;
    public const int SamplesPerBlock = 28;

    private static readonly int[] K0 = [0, 60, 115, 98, 122];
    private static readonly int[] K1 = [0, 0, -52, -55, -60];

    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    public static int Measure(byte[] ram, uint start, out bool loops)
    {
        loops = false;
        var mask = ram.Length - 1;
        var length = 0;
        while (length < MaxSampleBytes)
        {
            int flags = ram[(int)(start + (uint)length + 1u) & mask];
            length += 16;
            if ((flags & 1) == 0) continue;
            loops = (flags & 2) != 0;
            return length;
        }

        return 0;
    }

    public static ulong Hash(byte[] ram, uint start, int length)
    {
        var mask = ram.Length - 1;
        var h = FnvOffset ^ (ulong)length;
        for (var i = 0; i < length; i++)
        {
            var at = (int)(start + (uint)i) & mask;
            h ^= (i & 15) == 1 ? (byte)(ram[at] & ~7) : ram[at];
            h *= FnvPrime;
        }

        return h;
    }

    public static byte[] Copy(byte[] ram, uint start, int length)
    {
        var mask = ram.Length - 1;
        var copy = new byte[length];
        for (var i = 0; i < length; i++) copy[i] = ram[(int)(start + (uint)i) & mask];
        return copy;
    }

    public static short[] Decode(ReadOnlySpan<byte> adpcm)
    {
        var blocks = adpcm.Length / 16;
        var pcm = new short[blocks * SamplesPerBlock];
        int old = 0, older = 0, n = 0;

        for (var b = 0; b < blocks; b++)
        {
            var block = adpcm.Slice(b * 16, 16);
            var shift = block[0] & 0xF;
            if (shift > 12) shift = 9;
            var filter = Math.Min((block[0] >> 4) & 0x7, 4);
            int k0 = K0[filter], k1 = K1[filter];

            for (var i = 0; i < 14; i++)
            {
                var data = block[2 + i];
                for (var half = 0; half < 2; half++)
                {
                    var nibble = half == 0 ? data & 0xF : data >> 4;
                    var s = (nibble << 28) >> 28;
                    s = (s << 12) >> shift;
                    s += (old * k0 + older * k1) >> 6;
                    s = Math.Clamp(s, -32768, 32767);
                    older = old;
                    old = s;
                    pcm[n++] = (short)s;
                }
            }
        }

        return pcm;
    }

    public static int NaturalRate(int pitch)
    {
        return pitch <= 0 ? 22050 : Math.Clamp((int)(pitch * 44100L / 4096), 1000, 176400);
    }
}
