namespace RecompOne.Recompiler.Psx.Compression;

public static class Compression
{
    private const int MaxLayers = 4;

    private static readonly ICompressionFormat[] All =
    [
        new Rnc(),
        new Gzip(),
        new Zlib(),
        new Konami()
    ];

    public static ICompressionFormat? Find(string name)
    {
        return All.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public static ICompressionFormat? Detect(ReadOnlySpan<byte> data)
    {
        foreach (var format in All)
            if (format.Detectable && format.Matches(data))
                return format;

        return null;
    }

    public static byte[] Apply(byte[] data, string overlay, string? declared)
    {
        if (!string.IsNullOrEmpty(declared))
        {
            var format = Find(declared);
            if (format == null)
                Console.WriteLine($"[Compression] {overlay}: unknown format '{declared}'");
            else
                data = Run(format, data, overlay);
        }

        for (var layer = 0; layer < MaxLayers; layer++)
        {
            var format = Detect(data);
            if (format == null) break;
            data = Run(format, data, overlay);
        }

        return data;
    }

    private static byte[] Run(ICompressionFormat format, byte[] data, string overlay)
    {
        try
        {
            var output = format.Decode(data);
            Console.WriteLine($"[Compression] {overlay}: {format.Name} {data.Length} -> {output.Length} bytes");
            return output;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Compression] {overlay}: {format.Name} failed ({e.Message})");
            return data;
        }
    }
}
