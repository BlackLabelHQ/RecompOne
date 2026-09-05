using System.IO.Compression;

namespace RecompOne.Recompiler.Psx.Compression;

public sealed class Zlib : ICompressionFormat
{
    public string Name => "zlib";

    public bool Detectable => true;

    public bool Matches(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16 || data[0] != 0x78) return false;
        return ((data[0] << 8) | data[1]) % 31 == 0;
    }

    public byte[] Decode(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var stream = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }
}
