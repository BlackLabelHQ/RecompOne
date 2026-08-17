namespace RecompOne.Runtime.Cdrom.Chd;

//according to ref
internal enum ChdCompression
{
    Type0 = 0,
    Type1 = 1,
    Type2 = 2,
    Type3 = 3,
    None = 4,
    Self = 5,
    Parent = 6,
    RleSmall = 7,
    RleLarge = 8,
    Self0 = 9,
    Self1 = 10,
    ParentSelf = 11,
    Parent0 = 12,
    Parent1 = 13,
}

internal readonly record struct ChdMapEntry(ChdCompression Compression, uint Length, ulong Offset, ushort Crc);

internal sealed class ChdHeader
{
    public const int V5Length = 0x7C;

    public int Version { get; init; }
    public uint[] Compressors { get; init; } = new uint[4];
    public ulong LogicalBytes { get; init; }
    public ulong MapOffset { get; init; }
    public ulong MetaOffset { get; init; }
    public uint HunkBytes { get; init; }
    public uint UnitBytes { get; init; }

    public uint HunkCount => (uint)((LogicalBytes + HunkBytes - 1) / HunkBytes);

    public bool IsCompressed => Compressors[0] != 0;

    public static ChdHeader ReadV5(ReadOnlySpan<byte> raw)
    {
        var compressors = new uint[4];
        for (int i = 0; i < 4; i++)
            compressors[i] = ChdBig.U32(raw, 0x10 + i * 4);

        return new ChdHeader
        {
            Version = 5,
            Compressors = compressors,
            LogicalBytes = ChdBig.U64(raw, 0x20),
            MapOffset = ChdBig.U64(raw, 0x28),
            MetaOffset = ChdBig.U64(raw, 0x30),
            HunkBytes = ChdBig.U32(raw, 0x38),
            UnitBytes = ChdBig.U32(raw, 0x3C),
        };
    }
}

internal static class ChdBig
{
    public static ushort U16(ReadOnlySpan<byte> b, int o) => (ushort)((b[o] << 8) | b[o + 1]);

    public static uint U24(ReadOnlySpan<byte> b, int o) => (uint)((b[o] << 16) | (b[o + 1] << 8) | b[o + 2]);

    public static uint U32(ReadOnlySpan<byte> b, int o) =>
        ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) | ((uint)b[o + 2] << 8) | b[o + 3];

    public static ulong U48(ReadOnlySpan<byte> b, int o)
    {
        ulong value = 0;
        for (int i = 0; i < 6; i++) value = (value << 8) | b[o + i];
        return value;
    }

    public static ulong U64(ReadOnlySpan<byte> b, int o)
    {
        ulong value = 0;
        for (int i = 0; i < 8; i++) value = (value << 8) | b[o + i];
        return value;
    }

    public static uint Tag(string s) =>
        ((uint)s[0] << 24) | ((uint)s[1] << 16) | ((uint)s[2] << 8) | s[3];
}
