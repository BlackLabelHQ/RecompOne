using RecompOne.Recompiler.CodeGen;
using RecompOne.Recompiler.Config;
using RecompOne.Recompiler.Elf;
using RecompOne.Recompiler.Map;
using RecompOne.Recompiler.Symbols;
using RecompOne.Runtime.Cdrom;

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: recompone <config.json>");
    Console.Error.WriteLine("       recompone --generate-function-file -elf <path> -map <path> -out <output.json> [-rebase <hex>]");
    return 1;
}

if (string.Equals(args[0], "--generate-function-file", StringComparison.OrdinalIgnoreCase))
    return GenerateFunctionFile(args);

string configPath = Path.GetFullPath(args[0]);
if (!File.Exists(configPath))
{
    Console.Error.WriteLine($"config not found: {configPath}");
    return 1;
}

var config = ConfigLoader.Load(configPath);
string configDir = Path.GetDirectoryName(configPath)!;

string? ResolvePath(string? p) => p == null ? null : Path.IsPathRooted(p) ? p : Path.GetFullPath(Path.Combine(configDir, p));

config.Elf = ResolvePath(config.Elf);
config.Map = ResolvePath(config.Map);
config.FuncMap = ResolvePath(config.FuncMap);
foreach (var overlay in config.Overlays)
{
    overlay.Elf = ResolvePath(overlay.Elf);
    overlay.Map = ResolvePath(overlay.Map);
    overlay.FuncMap = ResolvePath(overlay.FuncMap);
}

string cuePath = Path.GetFullPath(Path.Combine(configDir, config.Cue));

if (!File.Exists(cuePath))
{
    Console.Error.WriteLine($"disc file not found: {cuePath}");
    return 1;
}

Console.WriteLine($"[RecompOne] Game: {config.Game.Name} ({config.Game.Id})");
Console.WriteLine($"[RecompOne] Disc file: {cuePath}");

var fs = DiscFs.Open(cuePath);
string outDir = Path.GetFullPath(Path.Combine(configDir, config.Game.Output));
Directory.CreateDirectory(outDir);

Console.WriteLine($"[RecompOne] Output Path: {outDir}");

try
{
    OverlayWriter.Write(config, fs, outDir);
    Console.WriteLine("[RecompOne] Recompilation finished.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[RecompOne] Error: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
    return 1;
}

static int GenerateFunctionFile(string[] args)
{
    string? elfPath = null, mapPath = null, outPath = null;
    string? discPath = null, discFile = null, baseAddr = null;
    bool linearSweep = false;
    int offset = 0, skip = 0, lba = -1, size = -1;
    bool decrypt = false;
    int rebase = 0;

    for (int i = 1; i < args.Length; i++)
    {
        switch (args[i].ToLowerInvariant())
        {
            case "-elf": elfPath = args[++i]; break;
            case "-map": mapPath = args[++i]; break;
            case "-out": outPath = args[++i]; break;
            case "-linear-sweep": linearSweep = true; break;
            case "-disc": discPath = args[++i]; break;
            case "-file": discFile = args[++i]; break;
            case "-base": baseAddr = args[++i]; break;
            case "-offset": offset = Convert.ToInt32(args[++i], 16); break;
            case "-skip": skip = Convert.ToInt32(args[++i], 16); break;
            case "-lba": lba = int.Parse(args[++i]); break;
            case "-size": size = Convert.ToInt32(args[++i], 16); break;
            case "-decrypt": decrypt = true; break;
            case "-rebase": rebase = Convert.ToInt32(args[++i], 16); break;
            default:
                Console.Error.WriteLine($"unknown argument: {args[i]}");
                return 1;
        }
    }

    if (outPath == null)
    {
        Console.Error.WriteLine("missing -out <output.json>");
        return 1;
    }

    if (linearSweep)
    {
        if (discPath == null || baseAddr == null || (discFile == null && lba < 0))
        {
            Console.Error.WriteLine("-linear-sweep needs -disc <cue>, -base <hex> and eiter -file <path in disc> or -lba <n> -size <hex>");
            return 1;
        }
        return GenerateFromLinearSweep(discPath, discFile, baseAddr, offset, skip, lba, size, decrypt, rebase, outPath);
    }

    if (elfPath == null && mapPath == null)
    {
        Console.Error.WriteLine("at least one of -elf or -map is required");
        return 1;
    }

    FunctionInfo? elfInfo = null;
    if (elfPath != null)
    {
        if (!File.Exists(elfPath))
        {
            Console.Error.WriteLine($"elf not found: {elfPath}");
            return 1;
        }
        Console.WriteLine($"[RecompOne] reading ELF: {elfPath}");
        elfInfo = ElfReader.Read(elfPath);
        Console.WriteLine($"[RecompOne] ELF: {elfInfo.Functions.Count} function(s), {elfInfo.NoTypeSymbols.Count} label(s)");
    }

    FunctionInfo? mapInfo = null;
    if (mapPath != null)
    {
        if (!File.Exists(mapPath))
        {
            Console.Error.WriteLine($"map not found: {mapPath}");
            return 1;
        }
        Console.WriteLine($"[RecompOne] reading MAP: {mapPath}");
        mapInfo = MapReader.Read(mapPath);
        Console.WriteLine($"[RecompOne] MAP: {mapInfo.Functions.Count} function(s)");
    }

    var merged = FunctionMapLoader.Merge(elfInfo, mapInfo);

    if (rebase != 0)
    {
        uint delta = (uint)rebase;
        foreach (var f in merged.Functions) f.Address += delta;
        foreach (var f in merged.NoTypeSymbols) f.Address += delta;
    }

    FunctionMapLoader.Save(outPath, merged);
    Console.WriteLine($"[RecompOne] wrote {merged.Functions.Count} function(s), {merged.NoTypeSymbols.Count} label(s) -> {outPath}");
    return 0;
}

static int GenerateFromLinearSweep(string discPath, string? discFile, string baseAddr,
    int offset, int skip, int lba, int size, bool decrypt, int rebase, string outPath)
{
    discPath = Path.GetFullPath(discPath);
    if (!File.Exists(discPath))
    {
        Console.Error.WriteLine($"disc file not found: {discPath}");
        return 1;
    }

    var overlay = new OverlayConfig
    {
        Name = Path.GetFileNameWithoutExtension(outPath),
        Base = baseAddr,
        File = discFile,
        Offset = offset,
        Skip = skip,
        Lba = lba,
        Size = size >= 0 ? size : null,
        Decrypt = decrypt,
        Rebase = rebase,
        LinearSweep = true,
    };

    Console.WriteLine($"[RecompOne] sweeping {discFile ?? $"lba {lba}"} from {discPath}");

    var fs = DiscFs.Open(discPath);
    var analysis = OverlayWriter.AnalyzeOverlay(new RecompOneConfig(), overlay, fs);
    if (analysis == null)
    {
        Console.Error.WriteLine("[RecompOne] sweap produced nothing");
        return 1;
    }

    var info = new FunctionInfo
    {
        TextBase = analysis.ElfInfo.TextBase,
        LoadAddress = analysis.ElfInfo.LoadAddress,
    };
    foreach (var f in analysis.Functions.OrderBy(f => f.Start))
        info.Functions.Add(new RecompOne.Recompiler.Symbols.FunctionEntry
        {
            Name = f.Name,
            Address = f.Start,
            Size = f.End - f.Start,
        });
    info.NoTypeSymbols.AddRange(analysis.ElfInfo.NoTypeSymbols);

    FunctionMapLoader.Save(outPath, info);
    Console.WriteLine($"[RecompOne] wrote {info.Functions.Count} function(s), {info.NoTypeSymbols.Count} label(s) -> {outPath}");
    return 0;
}
