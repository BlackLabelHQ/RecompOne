namespace RecompOne.Runtime.Assets;

public enum AssetKind : byte
{
    Texture = 1,
    Sample = 2,
    Xa = 3,
    Clut = 5
}

public readonly struct AssetId : IEquatable<AssetId>
{
    public readonly ulong Hash;
    public readonly AssetKind Kind;

    public AssetId(AssetKind kind, ulong hash)
    {
        Kind = kind;
        Hash = hash;
    }

    public bool Equals(AssetId other)
    {
        return Hash == other.Hash && Kind == other.Kind;
    }

    public override bool Equals(object? o)
    {
        return o is AssetId a && Equals(a);
    }

    public override int GetHashCode()
    {
        return (int)(Hash ^ (Hash >> 32)) ^ ((int)Kind << 28);
    }

    public override string ToString()
    {
        return $"{Kind}:{Hash:x16}";
    }
}

public readonly struct XaKey : IEquatable<XaKey>
{
    public readonly int StartLba;
    public readonly byte File;
    public readonly byte Channel;

    public XaKey(int startLba, byte file, byte channel)
    {
        StartLba = startLba;
        File = file;
        Channel = channel;
    }

    public bool Equals(XaKey other)
    {
        return StartLba == other.StartLba && File == other.File && Channel == other.Channel;
    }

    public override bool Equals(object? o)
    {
        return o is XaKey k && Equals(k);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(StartLba, File, Channel);
    }

    public override string ToString()
    {
        return $"f{File}c{Channel}@{StartLba}";
    }
}

public readonly struct TextureKey : IEquatable<TextureKey>
{
    public readonly ulong Index;
    public readonly ulong Clut;
    public readonly byte Bpp;
    public readonly ushort W, H;

    public TextureKey(ulong index, ulong clut, byte bpp, ushort w, ushort h)
    {
        Index = index;
        Clut = clut;
        Bpp = bpp;
        W = w;
        H = h;
    }

    public bool Equals(TextureKey other)
    {
        return Index == other.Index && Clut == other.Clut && Bpp == other.Bpp && W == other.W && H == other.H;
    }

    public override bool Equals(object? o)
    {
        return o is TextureKey k && Equals(k);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Index, Clut, Bpp, W, H);
    }

    public override string ToString()
    {
        return $"{Index:x16}_{Clut:x16}_{Bpp}bpp_{W}x{H}";
    }
}