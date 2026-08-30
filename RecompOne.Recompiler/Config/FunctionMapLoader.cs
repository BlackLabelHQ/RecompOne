using System.Text.Json;
using System.Text.Json.Serialization;
using RecompOne.Recompiler.Symbols;

namespace RecompOne.Recompiler.Config;

public sealed class FuncMapEntry
{
    [JsonPropertyName("address")] public string Address { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("size")] public int? Size { get; set; }
}

public sealed class FuncMapFile
{
    [JsonPropertyName("functions")] public FuncMapEntry[] Functions { get; set; } = [];
    [JsonPropertyName("labels")] public FuncMapEntry[] Labels { get; set; } = [];
}

public static class FunctionMapLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static FunctionInfo Load(string path, uint textBase, byte[] textData)
    {
        using var stream = File.OpenRead(path);
        var file = JsonSerializer.Deserialize<FuncMapFile>(stream, Options)
                   ?? throw new InvalidDataException($"failed to parse function map {path}");

        var info = new FunctionInfo
        {
            LoadAddress = textBase,
            TextBase = textBase,
            TextData = textData
        };

        foreach (var e in file.Functions)
        {
            if (e.Size is not > 0)
                throw new InvalidDataException(
                    $"function map {path}: function '{e.Name}' at {e.Address} is missing a 'size'");

            info.Functions.Add(new Symbols.FunctionEntry
                { Name = e.Name, Address = Convert.ToUInt32(e.Address, 16), Size = (uint)e.Size.Value });
        }

        foreach (var e in file.Labels)
            info.NoTypeSymbols.Add(new Symbols.FunctionEntry
                { Name = e.Name, Address = Convert.ToUInt32(e.Address, 16), Size = (uint)(e.Size ?? 0) });

        return info;
    }

    public static FunctionInfo Merge(params FunctionInfo?[] sources)
    {
        var info = new FunctionInfo();
        var baseInfo = sources.FirstOrDefault(s => s != null);
        if (baseInfo != null)
        {
            info.LoadAddress = baseInfo.LoadAddress;
            info.TextBase = baseInfo.TextBase;
            info.TextData = baseInfo.TextData;
        }

        var funcsByAddr = new Dictionary<uint, Symbols.FunctionEntry>();
        var labelsByAddr = new Dictionary<uint, Symbols.FunctionEntry>();

        foreach (var src in sources)
        {
            if (src == null) continue;
            foreach (var f in src.Functions) funcsByAddr.TryAdd(f.Address, f);
            foreach (var l in src.NoTypeSymbols) labelsByAddr.TryAdd(l.Address, l);
        }

        info.Functions = funcsByAddr.Values.OrderBy(f => f.Address).ToList();
        info.NoTypeSymbols = labelsByAddr.Values.OrderBy(f => f.Address).ToList();
        return info;
    }

    public static void Save(string path, FunctionInfo info)
    {
        var file = new FuncMapFile
        {
            Functions = info.Functions.OrderBy(f => f.Address)
                .Select(f => new FuncMapEntry { Address = $"0x{f.Address:X8}", Name = f.Name, Size = (int)f.Size })
                .ToArray(),
            Labels = info.NoTypeSymbols.OrderBy(f => f.Address)
                .Select(f => new FuncMapEntry
                    { Address = $"0x{f.Address:X8}", Name = f.Name, Size = f.Size > 0 ? (int)f.Size : null })
                .ToArray()
        };

        File.WriteAllText(path, JsonSerializer.Serialize(file, Options));
    }
}