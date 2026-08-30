using System.Reflection;

namespace RecompOne.Runtime.Bios;

public static class KromFont
{
    public const uint Font1Address = 0xBFC66000u;
    public const uint Font2Address = 0xBFC66000u + 0x3D68u; // 0xBFC69D68

    private const int Font1RomOffset = 0x66000;
    private const int Font2RomOffset = 0x69D68;

    private const int GlyphBytes = 0x1E;

    private static readonly byte[] Font1 = LoadResource("font1.raw");
    private static readonly byte[] Font2 = LoadResource("font2.raw");

    public static void InstallInto(byte[] biosRom)
    {
        Array.Copy(Font1, 0, biosRom, Font1RomOffset, Font1.Length);
        Array.Copy(Font2, 0, biosRom, Font2RomOffset, Font2.Length);
    }

    public static uint Krom2RawAdd(uint code)
    {
        var c = (ushort)code;
        if (c >= 0x8140 && c <= 0x84BE)
            return Font1Address + (uint)(Krom2Offset(code) * GlyphBytes);
        if (c >= 0x889F && c <= 0x9872)
            return Font2Address + (uint)(Krom2Offset(code) * GlyphBytes);
        return 0xFFFFFFFFu;
    }

    public static ushort Krom2Offset(uint code)
    {
        var c = (ushort)code;
        if (c < 0x8140 || c > 0x9872) return 0;
        var idx = 1;
        while (Table[idx].Codepoint <= c) idx++;
        idx--;
        return (ushort)(c - Table[idx].Codepoint + Table[idx].Offset);
    }

    private readonly record struct Lookup(ushort Codepoint, ushort Offset);

    private static readonly Lookup[] Table =
    {
        new(0x8140, 0x0000), new(0x8180, 0x003f), new(0x81ad, 0x006d), new(0x81b8, 0x006c),
        new(0x81c0, 0x0080), new(0x81c8, 0x0074), new(0x81cf, 0x008f), new(0x81da, 0x007b),
        new(0x81e9, 0x00a9), new(0x81f0, 0x008a), new(0x81f8, 0x00b8), new(0x81fc, 0x0092),
        new(0x81fd, 0x00bd), new(0x824f, 0x0093), new(0x8259, 0x0119), new(0x8260, 0x009d),
        new(0x827a, 0x013a), new(0x8281, 0x00b7), new(0x829b, 0x015b), new(0x829f, 0x00d1),
        new(0x82f2, 0x01b2), new(0x8340, 0x0124), new(0x837f, 0x023f), new(0x8380, 0x0163),
        new(0x8397, 0x0257), new(0x839f, 0x017a), new(0x83b7, 0x0277), new(0x83bf, 0x0192),
        new(0x83d7, 0x0297), new(0x8440, 0x01aa), new(0x8461, 0x0321), new(0x8470, 0x01cb),
        new(0x847f, 0x033f), new(0x8480, 0x01da), new(0x8492, 0x0352), new(0x849f, 0x01ec),
        new(0x889f, 0x0000), new(0x8900, 0x001e), new(0x897f, 0x009c), new(0x8a00, 0x00da),
        new(0x8a7f, 0x0158), new(0x8b00, 0x0196), new(0x8b7f, 0x0214), new(0x8c00, 0x0252),
        new(0x8c7f, 0x02d0), new(0x8d00, 0x030e), new(0x8d7f, 0x038c), new(0x8e00, 0x03ca),
        new(0x8e7f, 0x0448), new(0x8f00, 0x0486), new(0x8f7f, 0x0504), new(0x9000, 0x0542),
        new(0x907f, 0x05c0), new(0x9100, 0x05fe), new(0x917f, 0x067c), new(0x9200, 0x06ba),
        new(0x927f, 0x0738), new(0x9300, 0x0776), new(0x937f, 0x07f4), new(0x9400, 0x0832),
        new(0x947f, 0x08b0), new(0x9500, 0x08ee), new(0x957f, 0x096c), new(0x9600, 0x09aa),
        new(0x967f, 0x0a28), new(0x9700, 0x0a66), new(0x977f, 0x0ae4), new(0x9800, 0x0b22),
        new(0xffff, 0x0000)
    };

    private static byte[] LoadResource(string fileName)
    {
        var asm = typeof(KromFont).Assembly;
        var expected = $"{asm.GetName().Name}.Bios.Fonts.{fileName}";
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n == expected || n.EndsWith("." + fileName, StringComparison.Ordinal));
        using var s = asm.GetManifestResourceStream(name ?? expected)
                      ?? throw new InvalidOperationException($"the embedded font resource wasn't found: {fileName}");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}