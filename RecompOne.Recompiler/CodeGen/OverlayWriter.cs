using System.Text;
using System.Text.RegularExpressions;
using RecompOne.Recompiler.Analysis;
using RecompOne.Recompiler.Config;
using RecompOne.Recompiler.Disasm;
using RecompOne.Recompiler.Elf;
using RecompOne.Recompiler.Map;
using RecompOne.Recompiler.Symbols;
using RecompOne.Recompiler.Psx;
using RecompOne.Runtime.Cdrom;

namespace RecompOne.Recompiler.CodeGen;

public static class OverlayWriter
{
    record OverlayResult(string Name, List<MipsFunction> Functions, int LbaStart, uint Base, uint Size, MipsInstruction[] Instructions);

    public static void Write(RecompOneConfig config, DiscFs fs, string outDir)
    {
        string className = SafeIdentifier(config.Game.Name);

        Console.WriteLine("[Recompiler] reading SYSTEM.CNF");
        var sysCfg = SystemCfg.Parse(fs);
        Console.WriteLine($"[Recompiler] SYSTEM.CNF: BOOT={sysCfg.BootExe}  TCB={sysCfg.Tcb}  EVENT={sysCfg.Event}  STACK=0x{sysCfg.Stack:X8}");

        var mainExe = Parser.ParseExe(fs, sysCfg.BootExe);
        Console.WriteLine($"[Recompiler] PS-EXE: {mainExe.Region}");
        Console.WriteLine($"[Recompiler] PS-EXE: PC=0x{mainExe.InitialPC:X8}  GP=0x{mainExe.InitialGP:X8}  SP=0x{mainExe.InitialSP:X8}  load=0x{mainExe.Destination:X8} ");

        var overlayResults = new List<OverlayResult>();

        {
            List<MipsFunction> funcs;
            MipsInstruction[] mainInstrs;
            FunctionInfo? elfInfo = null;

            FunctionInfo? rawElf = null;
            if (config.Elf != null)
            {
                if (!File.Exists(config.Elf))
                    throw new FileNotFoundException($"Main ELF not found: {config.Elf}");

                Console.WriteLine($"[Recompiler] Processing main executable with ELF: {config.Elf} (WARNING: 'elf' is deprecated, prefer 'map'/'funcMap')");
                rawElf = ElfReader.Read(config.Elf);
            }

            FunctionInfo? rawMap = null;
            if (config.Map != null)
            {
                if (!File.Exists(config.Map))
                    throw new FileNotFoundException($"Main map not found: {config.Map}");

                Console.WriteLine($"[Recompiler] Processing main executable with map: {config.Map}");
                rawMap = MapReader.Read(config.Map);
            }

            FunctionInfo? rawFuncMap = null;
            if (config.FuncMap != null && !config.LinearSweep)
            {
                if (!File.Exists(config.FuncMap))
                    throw new FileNotFoundException($"Main function map not found: {config.FuncMap}");

                Console.WriteLine($"[Recompiler] Processing main executable with function map: {config.FuncMap}");
                uint funcMapBase = rawElf?.TextBase ?? rawMap?.LoadAddress ?? mainExe.Destination;
                rawFuncMap = FunctionMapLoader.Load(config.FuncMap, funcMapBase, mainExe.Code);
            }

            if (rawElf != null || rawMap != null || rawFuncMap != null)
            {
                elfInfo = FunctionMapLoader.Merge(rawElf, rawMap, rawFuncMap);
                if (elfInfo.TextData.Length == 0) elfInfo.TextData = mainExe.Code;
                Console.WriteLine($"[Recompiler] main function info: TextBase=0x{elfInfo.TextBase:X8} Functions={elfInfo.Functions.Count}");

                mainInstrs = MipsDisasm.Disassemble(mainExe.Code, elfInfo.TextBase);

                funcs = elfInfo.Functions.Count > 0 ? FunctionDetector.DetectFromElf(mainInstrs, elfInfo, "main") : FunctionDetector.DetectFromScan(mainInstrs, elfInfo.LoadAddress, "main");
            }
            else
            {
                Console.WriteLine("[Recompiler] processing main executable");
                mainInstrs = MipsDisasm.Disassemble(mainExe.Code, mainExe.Destination);
                funcs = FunctionDetector.DetectFromScan(mainInstrs, mainExe.InitialPC, "main");
                elfInfo = new FunctionInfo
                {
                    TextBase = mainExe.Destination,
                    LoadAddress = mainExe.Destination,
                    TextData = mainExe.Code,
                };
            }

            if (funcs.All(f => f.Start != mainExe.InitialPC))
            {
                var entry = FunctionDetector.DetectFromAddresses(mainInstrs, [(mainExe.InitialPC, null)], funcs, "main");
                funcs.AddRange(entry);
                Console.WriteLine($"[Recompiler] added entry point function at 0x{mainExe.InitialPC:X8}");
            }

            var mainDiscovered = FunctionDetector.DiscoverCalls(mainInstrs, funcs, elfInfo?.NoTypeSymbols ?? [], "main");
            if (mainDiscovered.Count > 0)
            {
                funcs.AddRange(mainDiscovered);
                Console.WriteLine($"[Recompiler] discovered {mainDiscovered.Count} called function(s) in main");
            }

            AddConfigFunctions(funcs, config.Functions, mainInstrs, elfInfo?.NoTypeSymbols ?? [], "main");

            if (config.LinearSweep)
                SweepFunctions(funcs, mainInstrs, elfInfo?.NoTypeSymbols ?? [], "main");

            AnalyzeJumpTables(funcs, elfInfo!, "main");

            ApplyStubsAndIgnored(funcs, config.Stubs, config.Ignored);
            overlayResults.Add(new OverlayResult("main", funcs, -1, 0, 0, mainInstrs));
        }

        foreach (var overlayConfig in config.Overlays)
        {
            var analysis = AnalyzeOverlay(config, overlayConfig, fs);
            if (analysis == null) continue;
            overlayResults.Add(new OverlayResult(overlayConfig.Name, analysis.Functions, analysis.Lba,
                analysis.Base, (uint)analysis.DiscBin.Length, analysis.Instructions));
        }

        var allFuncs = overlayResults.SelectMany(o => o.Functions).ToList();
        ResolveCollisions(allFuncs);
        ApplyPatches(allFuncs, config.Patches);
        SdkPatches.Apply(allFuncs);
        WriteAll(config, outDir, className, mainExe, sysCfg, overlayResults, allFuncs);
    }

    public sealed record OverlayAnalysis(List<MipsFunction> Functions, MipsInstruction[] Instructions, FunctionInfo ElfInfo, byte[] DiscBin, int Lba, uint Base);

    public static OverlayAnalysis? AnalyzeOverlay(RecompOneConfig config, OverlayConfig overlayConfig, DiscFs fs)
    {
        {
            bool noSymbols = overlayConfig.Elf == null && overlayConfig.Map == null && overlayConfig.FuncMap == null;
            if (noSymbols && !((overlayConfig.LinearSweep ?? config.LinearSweep) && overlayConfig.Base != null))
            {
                Console.WriteLine($"[Recompiler] WARNING: Overlay '{overlayConfig.Name}' has no source defined, this will be skiped");
                return null;
            }
            if (overlayConfig.Elf != null && !File.Exists(overlayConfig.Elf))
            {
                Console.WriteLine($"[Recompiler] WARNING: ELF file not found for overlay '{overlayConfig.Name}' ({overlayConfig.Elf}), this will be skiped.");
                return null;
            }
            if (overlayConfig.Map != null && !File.Exists(overlayConfig.Map))
            {
                Console.WriteLine($"[Recompiler] WARNING: map file not found for overlay '{overlayConfig.Name}' ({overlayConfig.Map}), this will be skiped.");
                return null;
            }
            if (overlayConfig.FuncMap != null)
            {
                if (!File.Exists(overlayConfig.FuncMap))
                {
                    Console.WriteLine($"[Recompiler] WARNING: function map not found for overlay '{overlayConfig.Name}' ({overlayConfig.FuncMap}), this will be skiped.");
                    return null;
                }
                if (overlayConfig.Elf == null && overlayConfig.Map == null && overlayConfig.Base == null)
                {
                    Console.WriteLine($"[Recompiler] WARNING: overlay '{overlayConfig.Name}' uses 'funcMap' alone but has no 'base' address defined, this will be skiped.");
                    return null;
                }
            }

            if (overlayConfig.Elf != null)
                Console.WriteLine($"[Recompiler] processing the overlay {overlayConfig.Name} (WARNING: 'elf' is deprecated, prefer 'map'/'funcMap')");
            else
                Console.WriteLine($"[Recompiler] processing the overlay {overlayConfig.Name}");

            var (discBin, overlayLba) = ResolveOverlay(fs, overlayConfig);
            if (discBin == null)
            {
                Console.WriteLine($"[Recompiler] WARNING: could not resolve disc data for overlay '{overlayConfig.Name}', skipping");
                return null;
            }

            var rawElf = overlayConfig.Elf != null ? ElfReader.Read(overlayConfig.Elf) : null;
            var rawMap = overlayConfig.Map != null ? MapReader.Read(overlayConfig.Map) : null;

            FunctionInfo? rawFuncMap = null;
            if (overlayConfig.FuncMap != null)
            {
                uint funcMapBase = rawElf?.TextBase ?? rawMap?.LoadAddress ?? Convert.ToUInt32(overlayConfig.Base, 16);
                rawFuncMap = FunctionMapLoader.Load(overlayConfig.FuncMap, funcMapBase, discBin);
            }

            var elfInfo = FunctionMapLoader.Merge(rawElf, rawMap, rawFuncMap);
            if (elfInfo.TextData.Length == 0) elfInfo.TextData = discBin;

            if (noSymbols)
            {
                elfInfo.TextBase = Convert.ToUInt32(overlayConfig.Base, 16);
                elfInfo.LoadAddress = elfInfo.TextBase;
            }

            if (overlayConfig.Rebase != 0)
                RebaseElf(elfInfo, overlayConfig.Rebase, discBin);

            var instrs = MipsDisasm.Disassemble(discBin, elfInfo.TextBase);

            //elf is weird and doest properly provide all functions (specially asm) so resort to checking it
            var funcs = elfInfo.Functions.Count > 0 ? FunctionDetector.DetectFromElf(instrs, elfInfo, overlayConfig.Name) : FunctionDetector.DetectFromScan(instrs, elfInfo.LoadAddress, overlayConfig.Name);

            var discovered = FunctionDetector.DiscoverCalls(instrs, funcs, elfInfo.NoTypeSymbols, overlayConfig.Name);
            if (discovered.Count > 0)
            {
                funcs.AddRange(discovered);
                Console.WriteLine($"[Recompiler] discovered {discovered.Count} called funs in {overlayConfig.Name}");
            }

            AddConfigFunctions(funcs, overlayConfig.Functions, instrs, elfInfo.NoTypeSymbols, overlayConfig.Name);

            if (overlayConfig.LinearSweep ?? config.LinearSweep)
                SweepFunctions(funcs, instrs, elfInfo.NoTypeSymbols, overlayConfig.Name);

            AnalyzeJumpTables(funcs, elfInfo, overlayConfig.Name);

            ApplyStubsAndIgnored(funcs, overlayConfig.Stubs.Concat(config.Stubs), overlayConfig.Ignored.Concat(config.Ignored));
            
            uint ovlBase = overlayConfig.Base != null ? Convert.ToUInt32(overlayConfig.Base, 16) + (uint)overlayConfig.Rebase : 0;
            return new OverlayAnalysis(funcs, instrs, elfInfo, discBin, overlayLba, ovlBase);
        }
    }

    static void WriteAll(RecompOneConfig config, string outDir, string className, PsxExe mainExe, SystemCfg sysCfg, List<OverlayResult> overlayResults, List<MipsFunction> allFuncs)
    {
        var uniqueAddrs = allFuncs.GroupBy(f => f.Start).Where(g => g.Count() == 1).Select(g => g.Key).ToHashSet();
        var knownFuncs = allFuncs.Where(f => uniqueAddrs.Contains(f.Start)).ToDictionary(f => f.Start, f => $"{className}.{f.EmittedName}");

        int conflictCount = allFuncs.Count - knownFuncs.Count;
        Console.WriteLine($"[Recompiler] total functions: {allFuncs.Count}");

        string? mainCall = null;
        if (config.Main != null)
        {
            uint mainAddr = Convert.ToUInt32(config.Main, 16);
            var mainFunc = allFuncs.FirstOrDefault(f => f.Start == mainAddr);
            if (mainFunc == null) throw new InvalidOperationException($"[recompiler] the main function not found at 0x{mainAddr:X8}");
            mainCall = $"{className}.{mainFunc.EmittedName}";
            Console.WriteLine($"[Recompiler] main: {mainCall} @ 0x{mainAddr:X8}");
        }

        foreach (var result in overlayResults)
        {
            Console.WriteLine($"[Recompiler] emiting {result.Name}.cs ({result.Functions.Count} functions)");
            EmitOverlayFile(result.Name, result.Functions, className, knownFuncs, config.Debug, config.AddressComments, config.DisasmComments, result.LbaStart,  result.Base, result.Size, result.Instructions, outDir);
        }

        Console.WriteLine("[Recompiler] Emitting Entry.cs");
        var overlayNames = overlayResults.Select(o => o.Name).ToList();
        EntryWriter.Write(mainExe, sysCfg.BootExe, className, mainCall, overlayNames, outDir);

        Console.WriteLine("[Recompiler] finished "); //maybe add time it took
    }
    
    static void AddConfigFunctions(List<MipsFunction> funcs, Config.FunctionEntry[] entries, MipsInstruction[] instrs, IEnumerable<Symbols.FunctionEntry> noTypeSymbols, string overlayName)
    {
        if (entries.Length == 0) return;

        var byStart = funcs.GroupBy(f => f.Start).ToDictionary(g => g.Key, g => g.First());
        var missing = new List<(uint Addr, string? Name)>();
        int renamed = 0;
        foreach (var entry in entries)
        {
            uint addr = Convert.ToUInt32(entry.Address, 16);
            if (!byStart.TryGetValue(addr, out var existing))
            {
                missing.Add((addr, entry.Name));
                continue;
            }
            if (string.IsNullOrEmpty(entry.Name) || existing.Name == entry.Name) continue;
            existing.Name = entry.Name;
            renamed++;
        }
        if (renamed > 0)
            Console.WriteLine($"[Recompiler] renamed {renamed} detected function(s) from config in {overlayName}");
        if (missing.Count == 0) return;

        var extras = FunctionDetector.DetectFromAddresses(instrs, missing.Select(e => (e.Addr, e.Name)), funcs, overlayName);
        funcs.AddRange(extras);
        var callees = FunctionDetector.DiscoverCalls(instrs, funcs, noTypeSymbols, overlayName);
        funcs.AddRange(callees);
        Console.WriteLine($"[Recompiler] added {extras.Count} config function(s) (+{callees.Count} callees) to {overlayName}");
    }

    static void SweepFunctions(List<MipsFunction> funcs, MipsInstruction[] instrs, IEnumerable<Symbols.FunctionEntry> noTypeSymbols, string overlayName)
    {
        var swept = FunctionDetector.LinearSweep(instrs, funcs, noTypeSymbols, overlayName);
        if (swept.Count == 0) return;
        funcs.AddRange(swept);
        var callees = FunctionDetector.DiscoverCalls(instrs, funcs, noTypeSymbols, overlayName);
        funcs.AddRange(callees);
        Console.WriteLine($"[Recompiler] linear sweep found {swept.Count} function(s) (+{callees.Count} callees) in {overlayName}");
    }

    static void EmitOverlayFile(string overlayName, List<MipsFunction> funcs, string className, Dictionary<uint, string> knownFuncs, bool debug, bool addressComments, bool disasmComments, int lbaStart, uint ovlBase, uint ovlSize, MipsInstruction[] instrs, string outDir)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using RecompOne.Runtime.Context;");
        sb.AppendLine("using RecompOne.Runtime.Dispatch;");
        sb.AppendLine("using RecompOne.Runtime.Memory;");
        sb.AppendLine();
        sb.AppendLine("namespace Recompiled;");
        sb.AppendLine();
        sb.AppendLine($"public static partial class {className}");
        sb.AppendLine("{");

        foreach (var func in funcs.OrderBy(f => f.Start))
        {
            var labels = LabelManager.Collect(func);
            var ctx = new FunctionContext
            {
                FuncStart = func.Start,
                FuncEnd = func.End,
                KnownFunctions = knownFuncs,
                Labels = labels,
                Debug = debug,
                AddressComments = addressComments,
                DisasmComments = disasmComments,
                JumpTablesByJr = func.JumpTables.ToDictionary(j => j.JrVram),
                RaReturnJrs = FunctionDetector.ComputeRaReturnJrs(func),
                AllInstructions = instrs
            };
            sb.Append(FunctionEmitter.Emit(func, ctx));
        }

        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine($"public sealed class {DispatchTableName(overlayName)} : IOverlay");
        sb.AppendLine("{");
        sb.AppendLine($"    public string Name => \"{overlayName}\";");
        sb.AppendLine($"    public int LbaStart => {lbaStart};");
        sb.AppendLine($"    public uint Base => 0x{ovlBase:X8}u;");
        sb.AppendLine($"    public uint Size => 0x{ovlSize:X}u;");
        sb.AppendLine("    public IReadOnlyDictionary<uint, Action<CpuContext, IMemory>> Functions { get; } =");
        sb.AppendLine("        new Dictionary<uint, Action<CpuContext, IMemory>>");
        sb.AppendLine("        {");
        foreach (var func in funcs.Where(f => !f.IsStub).OrderBy(f => f.Start))
            sb.AppendLine($"            [0x{func.Start:X8}u] = {className}.{func.EmittedName},");
        sb.AppendLine("        };");
        sb.AppendLine("}");

        File.WriteAllText(Path.Combine(outDir, $"{overlayName}.cs"), sb.ToString());
    }

    static string DispatchTableName(string name) => $"{char.ToUpperInvariant(name[0])}{name[1..]}DispatchTable";

    static void ResolveCollisions(List<MipsFunction> allFuncs)
    {
        var crossOverlayDups = allFuncs.GroupBy(f => f.Name)
            .Where(g => g.Select(f => f.OverlayName).Distinct().Count() > 1)
            .Select(g => g.Key).ToHashSet();

        foreach (var func in allFuncs)
            func.EmittedName = SafeFuncName(crossOverlayDups.Contains(func.Name) && !string.IsNullOrEmpty(func.OverlayName)
                ? $"{func.Name}_{SafeIdentifier(func.OverlayName)}"
                : func.Name);

        foreach (var group in allFuncs.GroupBy(f => (f.OverlayName, f.EmittedName)).Where(g => g.Count() > 1))
            foreach (var func in group)
                func.EmittedName = $"{func.EmittedName}_{func.Start:X8}";
    }

    static string SafeFuncName(string s) => Regex.Replace(s, @"[^A-Za-z0-9_]", "_");

    static (byte[]? data, int lba) ResolveOverlay(DiscFs fs, Config.OverlayConfig cfg)
    {
        try
        {
            if (cfg.Lba >= 0)
            {
                int sz = cfg.Size ?? throw new InvalidOperationException($"'size' is required when using 'lba' for overlay '{cfg.Name}'");
                return (Decrypt(fs.ReadSectors(cfg.Lba, sz), cfg.Decrypt), cfg.Lba);
            }
            if (cfg.File != null)
            {
                if (!fs.Locate(cfg.File, out int lba, out uint fileSize))
                {
                    Console.WriteLine($"[Recompiler] WARNING: disc file not found: {cfg.File}");
                    return (null, -1);
                }
                int absLba = lba + (cfg.Offset + cfg.Skip) / 2048;
                byte[] full = fs.ReadFile(cfg.File);
                int start = cfg.Offset + cfg.Skip;
                int length = cfg.Size ?? (full.Length - start);
                return (Decrypt(full.AsSpan(start, length).ToArray(), cfg.Decrypt), absLba);
            }
            Console.WriteLine($"[Recompiler] WARNING: overlay '{cfg.Name}' has no 'file' or 'lba' source defined");
            return (null, -1);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Recompiler] WARNING: failed to resolve disc data for '{cfg.Name}': {ex.Message}");
            return (null, -1);
        }
    }

    
    static void RebaseElf(FunctionInfo elf, int delta, byte[] discBin)
    {
        uint d = (uint)delta;
        elf.TextBase += d;
        elf.LoadAddress += d;
        foreach (var f in elf.Functions) f.Address += d;
        foreach (var f in elf.NoTypeSymbols) f.Address += d;
        foreach (var s in elf.DataSections) s.Va += d;
        elf.TextData = discBin;
    }

    static byte[] Decrypt(byte[] data, bool decrypt)
    {
        if (!decrypt) return data;
        uint seed = 0;
        for (int i = 0; i + 4 <= data.Length; i += 4)
        {
            seed = (seed + 0x01309125u) * 0x03A452F7u;
            uint w = BitConverter.ToUInt32(data, i) ^ seed;
            BitConverter.GetBytes(w).CopyTo(data, i);
        }
        return data;
    }
    static void AnalyzeJumpTables(List<MipsFunction> funcs, FunctionInfo elf, string name)
    {
        int funcsWithTables = 0, totalEntries = 0;
        foreach (var func in funcs)
        {
            func.JumpTables = JumpTableAnalyzer.Analyze(func, elf);
            if (func.JumpTables.Count == 0) continue;
            funcsWithTables++;
            foreach (var jt in func.JumpTables) totalEntries += jt.Entries.Length;
        }
        if (funcsWithTables > 0)
            Console.WriteLine($"[Recompiler] {name}: found jump tables in {funcsWithTables} function(s), {totalEntries} entries in total");
    }
    
    static bool PatchNameMatches(MipsFunction func, string? patchFunction)
    {
        if (string.IsNullOrEmpty(patchFunction)) return false;
        if (string.Equals(func.Name, patchFunction, StringComparison.Ordinal)) return true;
        if (string.IsNullOrEmpty(func.OverlayName)) return false;
        return string.Equals(func.Name, $"{func.OverlayName.ToUpperInvariant()}_{patchFunction}", StringComparison.Ordinal);
    }

    static void ApplyPatches(List<MipsFunction> funcs, Config.PatchEntry[] patches)
    {
        if (patches.Length == 0) return;
        int applied = 0;
        foreach (var patch in patches)
        {
            uint? addr = string.IsNullOrEmpty(patch.Address) ? null : Convert.ToUInt32(patch.Address, 16);
            int matched = 0;
            foreach (var func in funcs)
            {
                if (!patch.MatchesOverlay(func.OverlayName)) continue;
                bool hit = addr.HasValue ? func.Start == addr.Value : PatchNameMatches(func, patch.Function);
                if (!hit) continue;
                matched++;
                switch (patch.Mode.ToLowerInvariant())
                {
                    case "pre":
                        if (!func.PreHookTargets.Contains(patch.Target))
                            func.PreHookTargets.Add(patch.Target);
                        break;
                    case "post":
                        if (!func.PostHookTargets.Contains(patch.Target))
                            func.PostHookTargets.Add(patch.Target);
                        break;
                    default:
                        if (func.IsPatch && !string.Equals(func.PatchTarget, patch.Target, StringComparison.Ordinal))
                        {
                            Console.WriteLine($"[Recompiler] WARNING: '{func.Name}' @ {func.OverlayName} already replaced by '{func.PatchTarget}', ignoring '{patch.Target}'"); //logeg
                            continue;
                        }
                        func.IsPatch = true;
                        func.PatchTarget = patch.Target;
                        break;
                }
                applied++;
            }
            if (matched == 0)
                Console.WriteLine($"[Recompiler] WARNING: patch '{patch.Target}' matched nothing (overlay='{patch.OverlayLabel}' function='{patch.Function}' address='{patch.Address}')");
        }
        Console.WriteLine($"[Recompiler] applied {applied} patches");
    }

    static void ApplyStubsAndIgnored(List<MipsFunction> funcs, IEnumerable<string> stubs, IEnumerable<string> ignored)
    {
        var stubSet = new HashSet<string>(stubs,   StringComparer.OrdinalIgnoreCase);
        var ignoredSet = new HashSet<string>(ignored, StringComparer.OrdinalIgnoreCase);
        foreach (var func in funcs)
        {
            if (ignoredSet.Contains(func.Name)) { func.IsStub = true; func.Name = "__ignored__"; }
            else if (stubSet.Contains(func.Name)) func.IsStub = true;
        }
        funcs.RemoveAll(f => f.Name == "__ignored__");
    }
    
    
    
    static string SafeIdentifier(string s) => Regex.Replace(s, @"[^a-zA-Z0-9_]", "_").TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
    
    
}
