using System.IO.Compression;

namespace RecompOne.Recompiler.Psx.Compression;

public sealed class Gzip : ICompressionFormat
{
    public string Name => "gzip";

    public bool Detectable => true;

    public bool Matches(ReadOnlySpan<byte> data)
    {
        return data.Length > 18 && data[0] == 0x1F && data[1] == 0x8B && data[2] == 0x08;
    }

    public byte[] Decode(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var stream = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }
}
