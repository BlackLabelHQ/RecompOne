namespace RecompOne.Recompiler.Psx.Compression;

public sealed class Konami : ICompressionFormat //the demo disc uses this compression for game
{
    public string Name => "konami";

    public bool Detectable => false;

    public bool Matches(ReadOnlySpan<byte> data)
    {
        return false;
    }

    public byte[] Decode(byte[] data)
    {
        var output = (byte[])data.Clone();
        uint seed = 0;
        for (var i = 0; i + 4 <= output.Length; i += 4)
        {
            seed = (seed + 0x01309125u) * 0x03A452F7u;
            var word = BitConverter.ToUInt32(output, i) ^ seed;
            BitConverter.GetBytes(word).CopyTo(output, i);
        }

        return output;
    }
}
