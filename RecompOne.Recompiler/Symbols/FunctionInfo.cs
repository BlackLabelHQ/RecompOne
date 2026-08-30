namespace RecompOne.Recompiler.Symbols;

public sealed class FunctionEntry
{
    public string Name = "";
    public uint Address;
    public uint Size;
}

public sealed class DataSection
{
    public string Name = "";
    public uint Va;
    public byte[] Data = [];
    public bool IsZero;
    public uint ZeroSize;
}

public sealed class FunctionInfo
{
    public uint LoadAddress;
    public uint TextBase;
    public byte[] TextData = [];
    public List<FunctionEntry> Functions = [];
    public List<FunctionEntry> NoTypeSymbols = [];
    public List<DataSection> DataSections = [];
}