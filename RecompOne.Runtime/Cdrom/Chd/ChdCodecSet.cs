using System.IO.Compression;

namespace RecompOne.Runtime.Cdrom.Chd;

internal sealed class ChdCodecSet
{
    public const int SectorData = 2352;
    public const int SubcodeData = 96;
    public const int FrameSize = SectorData + SubcodeData;

    private static readonly byte[] SyncHeader =
        [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];

    private readonly uint[] _compressors;
    private readonly int _frames;
    private readonly byte[] _scratch;

    public ChdCodecSet(ChdHeader header)
    {
        _compressors = header.Compressors;
        _frames = (int)(header.HunkBytes / FrameSize);
        _scratch = new byte[header.HunkBytes];
    }

    public void Decompress(int slot, byte[] source, byte[] dest)
    {
        uint codec = _compressors[slot];

        if (codec == ChdBig.Tag("cdzl")) { DecompressCd(source, dest, Zlib); return; }
        if (codec == ChdBig.Tag("cdlz")) { DecompressCd(source, dest, Lzma); return; }
        if (codec == ChdBig.Tag("cdfl")) { DecompressCdFlac(source, dest); return; }
        if (codec == ChdBig.Tag("zlib")) { Zlib(source, 0, source.Length, dest, 0, dest.Length); return; }
        if (codec == ChdBig.Tag("lzma")) { Lzma(source, 0, source.Length, dest, 0, dest.Length); return; }

        throw new NotSupportedException($"chd codec {TagName(codec)} its not supported"); //most should use lzma i believe so shouldnt be a problem
    }

    private delegate void Decoder(byte[] src, int srcOffset, int srcLength, byte[] dst, int dstOffset, int dstLength);

    private void DecompressCd(byte[] source, byte[] dest, Decoder baseDecoder)
    {
        int eccBytes = (_frames + 7) / 8;
        int lengthBytes = source.Length < 0x10000 ? 2 : 3;
        int headerBytes = eccBytes + lengthBytes;

        int baseLength = (source[eccBytes] << 8) | source[eccBytes + 1];
        if (lengthBytes > 2) baseLength = (baseLength << 8) | source[eccBytes + 2];

        int dataBytes = _frames * SectorData;
        int subBytes = _frames * SubcodeData;

        baseDecoder(source, headerBytes, baseLength, _scratch, 0, dataBytes);
        Zlib(source, headerBytes + baseLength, source.Length - headerBytes - baseLength, _scratch, dataBytes, subBytes);

        Reassemble(dest, source, eccBytes);
    }

    private void DecompressCdFlac(byte[] source, byte[] dest)
    {
        int dataBytes = _frames * SectorData;
        int subBytes = _frames * SubcodeData;

        int consumed = ChdFlac.Decode(source, 0, _scratch, 0, dataBytes);
        Zlib(source, consumed, source.Length - consumed, _scratch, dataBytes, subBytes);

        Reassemble(dest, source, 0);
    }

    private void Reassemble(byte[] dest, byte[] source, int eccBytes)
    {
        int dataBytes = _frames * SectorData;

        for (int frame = 0; frame < _frames; frame++)
        {
            int sector = frame * FrameSize;
            Array.Copy(_scratch, frame * SectorData, dest, sector, SectorData);
            Array.Copy(_scratch, dataBytes + frame * SubcodeData, dest, sector + SectorData, SubcodeData);

            if (eccBytes > 0 && (source[frame / 8] & (1 << (frame % 8))) != 0)
            {
                Array.Copy(SyncHeader, 0, dest, sector, SyncHeader.Length);
                ChdEcc.Generate(dest, sector);
            }
        }
    }

    private static void Zlib(byte[] src, int srcOffset, int srcLength, byte[] dst, int dstOffset, int dstLength)
    {
        using var input = new MemoryStream(src, srcOffset, srcLength, writable: false);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        deflate.ReadExactly(dst, dstOffset, dstLength);
    }

    private static void Lzma(byte[] src, int srcOffset, int srcLength, byte[] dst, int dstOffset, int dstLength)
        => ChdLzma.Decode(src, srcOffset, srcLength, dst, dstOffset, dstLength);

    private static string TagName(uint tag) =>
        new([(char)(tag >> 24), (char)((tag >> 16) & 0xFF), (char)((tag >> 8) & 0xFF), (char)(tag & 0xFF)]);
}
