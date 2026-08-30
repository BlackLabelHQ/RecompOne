namespace RecompOne.Runtime.Assets;

public static class AssetHash
{
    private const byte Salt = 1;

    private const ulong Offset = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    private const byte DomainXa = 0x30;
    private const byte DomainXaNoLba = 0x31;
    private const byte DomainSample = 0x20;
    private const byte DomainPayload = 0x32;

    public static ulong
        Fnv1a64(ReadOnlySpan<byte> data) //hash fnv1a, its simple enough dont need more advanced hashing for this
    {
        var h = Offset;
        for (var i = 0; i < data.Length; i++)
        {
            h ^= data[i];
            h *= Prime;
        }

        return h;
    }

    private static ulong Frame(byte domain, ReadOnlySpan<byte> body)
    {
        Span<byte> buf = stackalloc byte[2 + 32];
        buf[0] = domain;
        buf[1] = Salt;
        var n = Math.Min(body.Length, buf.Length - 2);
        body[..n].CopyTo(buf[2..]);
        return Fnv1a64(buf[..(2 + n)]);
    }

    public static ulong Xa(in XaKey k)
    {
        Span<byte> b = stackalloc byte[6];
        BitConverter.TryWriteBytes(b, k.StartLba);
        b[4] = k.File;
        b[5] = k.Channel;
        return Frame(DomainXa, b);
    }

    public static ulong XaNoLba(in XaKey k)
    {
        Span<byte> b = stackalloc byte[2];
        b[0] = k.File;
        b[1] = k.Channel;
        return Frame(DomainXaNoLba, b);
    }

    public static ulong Sample(ReadOnlySpan<byte> adpcm)
    {
        return Frame(DomainSample, BitConverter.GetBytes(Fnv1a64(adpcm)));
    }

    public static ulong XaPayload(ReadOnlySpan<byte> payload)
    {
        return Frame(DomainPayload, BitConverter.GetBytes(Fnv1a64(payload)));
    }

    public static bool TryParseHex(string? s, out ulong value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }
}