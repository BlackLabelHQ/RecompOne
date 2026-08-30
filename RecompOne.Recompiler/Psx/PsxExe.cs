namespace RecompOne.Recompiler.Psx;

public sealed class PsxExe //https://problemkaputt.de/psxspx-cdrom-file-playstation-exe-and-system-cnf.htm
{
    public uint InitialPC;
    public uint InitialGP;
    public uint Destination;
    public uint TextSize;
    public uint DataAddress;
    public uint DataSize;
    public uint BssAddress;
    public uint BssSize;
    public uint InitialSP;
    public uint SpOffset;

    public string Region = "";
    public byte[] Code = [];
}