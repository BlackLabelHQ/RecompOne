namespace RecompOne.Recompiler.AutoConfigure;

public readonly record struct CodeRating(double Value, int Returns, int Prologues, int Words);

public static class CodeScore
{
    private const uint JrRa = 0x03E00008u;

    private const int Window = 4096;

    public static CodeRating Rate(byte[] data)
    {
        var words = data.Length / 4;
        if (words < 64) return new CodeRating(0, 0, 0, words);

        int returns = 0, prologues = 0;
        var best = 0.0;

        for (var start = 0; start < words; start += Window)
        {
            var end = Math.Min(words, start + Window);
            if (end - start < 64) break;

            int valid = 0, zeros = 0;
            for (var i = start; i < end; i++)
            {
                var w = BitConverter.ToUInt32(data, i * 4);
                if (w == 0) zeros++;
                if (w == JrRa) returns++;
                if (IsStackPrologue(w)) prologues++;
                if (IsPlausible(w)) valid++;
            }

            var span = end - start;
            var density = (double)valid / span;
            var live = 1.0 - Math.Min(1.0, (double)zeros / span * 1.5);
            best = Math.Max(best, density * 0.6 + live * 0.4);
        }

        var value = best;

        var expected = Math.Max(4, data.Length / 16384);
        if (returns + prologues < expected) value = Math.Min(value, 0.4);

        return new CodeRating(value, returns, prologues, words);
    }

    private static bool IsStackPrologue(uint w)
    {
        return w >> 26 == 0x09 && ((w >> 21) & 31) == 29 && ((w >> 16) & 31) == 29 && (short)(w & 0xFFFF) < 0;
    }

    private static bool IsPlausible(uint w)
    {
        var op = w >> 26;
        switch (op)
        {
            case 0x00:
                var funct = w & 0x3F;
                return funct is not (0x05 or 0x0E or 0x15 or 0x16 or 0x17 or 0x1E or 0x1F or 0x28 or 0x29 or 0x2E
                    or 0x2F or 0x3D or 0x3E or 0x3F);
            case 0x01:
            case 0x02:
            case 0x03:
            case 0x04:
            case 0x05:
            case 0x06:
            case 0x07:
            case 0x08:
            case 0x09:
            case 0x0A:
            case 0x0B:
            case 0x0C:
            case 0x0D:
            case 0x0E:
            case 0x0F:
            case 0x10:
            case 0x12:
            case 0x20:
            case 0x21:
            case 0x22:
            case 0x23:
            case 0x24:
            case 0x25:
            case 0x26:
            case 0x28:
            case 0x29:
            case 0x2A:
            case 0x2B:
            case 0x2E:
            case 0x32:
            case 0x3A:
                return true;
            default:
                return false;
        }
    }

    public static uint GuessBase(byte[] data, uint size)
    {
        var words = data.Length / 4;
        var pointers = new List<uint>();
        for (var i = 0; i < words; i++)
        {
            var w = BitConverter.ToUInt32(data, i * 4);
            if ((w & 3) == 0 && w >= 0x80000000u && w < 0x80800000u) pointers.Add(w);
        }

        if (pointers.Count < 16) return 0;

        var candidates = pointers.Select(p => p & 0xFFFFF000u).Distinct().OrderBy(p => p).Take(512).ToArray();
        var scored = candidates
            .Select(c => (Base: c, Hits: pointers.Count(p => p >= c && p < c + size)))
            .ToArray();

        var bestHits = scored.Length == 0 ? 0 : scored.Max(s => s.Hits);

        if (bestHits < 32 || bestHits < pointers.Count / 4) return 0;

        var plateau = scored.Where(s => s.Hits >= bestHits * 0.98).ToArray();
        var chosen = plateau[^1].Base;

        var aligned = chosen & ~0x3FFFu;
        if (aligned >= 0x80010000u && pointers.Count(p => p >= aligned && p < aligned + size) >= bestHits * 0.98)
            chosen = aligned;

        return chosen >= 0x80010000u && chosen + size <= 0x80200000u ? chosen : 0;
    }
}