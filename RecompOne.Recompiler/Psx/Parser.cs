using RecompOne.Runtime.Cdrom;

namespace RecompOne.Recompiler.Psx;

public static class Parser
{
    private static readonly byte[] Magic = "PS-X EXE"u8.ToArray();

    public static PsxExe ParseExe(DiscFs fs, string cdPath)
    {
        var data = fs.ReadFile(cdPath);
        if (!data.AsSpan(0, 8).SequenceEqual(Magic))
            throw new InvalidDataException($"it not a valid PS-EXE: {cdPath}");

        using var reader = new BinaryReader(new MemoryStream(data));
        reader.ReadBytes(16);

        var exe = new PsxExe
        {
            InitialPC = reader.ReadUInt32(),
            InitialGP = reader.ReadUInt32(),
            Destination = reader.ReadUInt32(),
            TextSize = reader.ReadUInt32(),
            DataAddress = reader.ReadUInt32(),
            DataSize = reader.ReadUInt32(),
            BssAddress = reader.ReadUInt32(),
            BssSize = reader.ReadUInt32(),
            InitialSP = reader.ReadUInt32(),
            SpOffset = reader.ReadUInt32(),
        };

        reader.ReadBytes(20); // reserved bb
        var regionBytes = reader.ReadBytes(0x34);
        int regionLen = Array.IndexOf(regionBytes, (byte)0);
        exe.Region = System.Text.Encoding.ASCII.GetString(regionBytes, 0, regionLen < 0 ? regionBytes.Length : regionLen).Trim();

        exe.Code = data[0x800..(0x800 + (int)exe.TextSize)];
        return exe;
    }

    public static PsxExe ParseBin(DiscFs fs, string cdPath)
    {
        var code = fs.ReadFile(cdPath);
        return new PsxExe { TextSize = (uint)code.Length, Code = code };
    }
}
