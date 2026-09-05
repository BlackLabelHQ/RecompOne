namespace RecompOne.Recompiler.Psx.Compression;

public interface ICompressionFormat
{
    string Name { get; }

    bool Detectable { get; }

    bool Matches(ReadOnlySpan<byte> data);

    byte[] Decode(byte[] data);
}
