using RecompOne.Recompiler.Disasm;
using RecompOne.Recompiler.Symbols;

namespace RecompOne.Recompiler.Analysis;

public sealed record ImageFunctions(string Name, List<MipsFunction> Functions, MipsInstruction[] Instructions);

public sealed record PipelineOptions(
    Config.FunctionEntry[] ConfigFunctions,
    bool LinearSweep,
    bool PointerScan,
    IEnumerable<string> Stubs,
    IEnumerable<string> Ignored);

public static class FunctionPipeline
{
    public static void Run(List<MipsFunction> funcs, MipsInstruction[] instrs, FunctionInfo elfInfo, string name,
        PipelineOptions options)
    {
        DiscoverCalls(funcs, instrs, elfInfo.NoTypeSymbols, name);
        AddConfigFunctions(funcs, options.ConfigFunctions, instrs, elfInfo.NoTypeSymbols, name);

        if (options.LinearSweep) Sweep(funcs, instrs, elfInfo.NoTypeSymbols, name);
        if (options.PointerScan) ScanPointers(funcs, instrs, elfInfo.NoTypeSymbols, name);

        ScanEscapes(funcs, instrs, name);
        AnalyzeJumpTables(funcs, elfInfo, name);
        ApplyStubsAndIgnored(funcs, options.Stubs, options.Ignored);
    }

    //a call from another ovl landing inside this one
    public static void ScanCrossImage(IReadOnlyList<ImageFunctions> images)
    {
        foreach (var owner in images)
        {
            if (owner.Instructions.Length == 0) continue;

            var lo = owner.Instructions[0].Vram;
            var hi = owner.Instructions[^1].Vram + 4;
            var starts = new HashSet<uint>(owner.Functions.Select(f => f.Start));
            var bodies = owner.Functions.Where(f => f.End > f.Start).ToList();
            var targets = new SortedSet<uint>();

            foreach (var other in images)
            {
                if (ReferenceEquals(other, owner) || other.Instructions.Length == 0) continue;

                var otherLo = other.Instructions[0].Vram;
                var otherHi = other.Instructions[^1].Vram + 4;
                if (otherLo < hi && lo < otherHi) continue;

                foreach (var instr in other.Instructions)
                {
                    var op = instr.Word >> 26;
                    if (op != 2 && op != 3) continue;

                    var t = instr.JumpTarget;
                    if (t < lo || t >= hi || starts.Contains(t) || targets.Contains(t)) continue;
                    if (!bodies.Any(f => t > f.Start && t < f.End)) continue;

                    targets.Add(t);
                }
            }

            if (targets.Count == 0) continue;

            var extras = FunctionDetector.DetectFromAddresses(owner.Instructions,
                targets.Select(t => (t, (string?)null)), owner.Functions, owner.Name);
            owner.Functions.AddRange(extras);
            Console.WriteLine($"[Recompiler] cross-image scan found {extras.Count} entry point(s) in {owner.Name}");
        }
    }

    public static void ScanEscapesToFixpoint(IReadOnlyList<ImageFunctions> images)
    {
        foreach (var image in images)
            while (ScanEscapes(image.Functions, image.Instructions, image.Name))
            {
            }
    }

    private static void DiscoverCalls(List<MipsFunction> funcs, MipsInstruction[] instrs,
        IEnumerable<FunctionEntry> noTypeSymbols, string name)
    {
        var found = FunctionDetector.DiscoverCalls(instrs, funcs, noTypeSymbols, name);
        if (found.Count == 0) return;
        funcs.AddRange(found);
        Console.WriteLine($"[Recompiler] discovered {found.Count} called function(s) in {name}");
    }

    private static void AddConfigFunctions(List<MipsFunction> funcs, Config.FunctionEntry[] entries,
        MipsInstruction[] instrs, IEnumerable<FunctionEntry> noTypeSymbols, string name)
    {
        if (entries.Length == 0) return;

        var byStart = funcs.GroupBy(f => f.Start).ToDictionary(g => g.Key, g => g.First());
        var missing = new List<(uint Addr, string? Name)>();
        var renamed = 0;

        foreach (var entry in entries)
        {
            var addr = Convert.ToUInt32(entry.Address, 16);
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
            Console.WriteLine($"[Recompiler] renamed {renamed} detected function(s) from config in {name}");
        if (missing.Count == 0) return;

        var extras = FunctionDetector.DetectFromAddresses(instrs, missing.Select(e => (e.Addr, e.Name)), funcs, name);
        funcs.AddRange(extras);

        var callees = FunctionDetector.DiscoverCalls(instrs, funcs, noTypeSymbols, name);
        funcs.AddRange(callees);
        Console.WriteLine($"[Recompiler] added {extras.Count} config function(s) (+{callees.Count} callees) to {name}");
    }

    private static void Sweep(List<MipsFunction> funcs, MipsInstruction[] instrs,
        IEnumerable<FunctionEntry> noTypeSymbols, string name)
    {
        var swept = FunctionDetector.LinearSweep(instrs, funcs, noTypeSymbols, name);
        if (swept.Count == 0) return;
        funcs.AddRange(swept);

        var callees = FunctionDetector.DiscoverCalls(instrs, funcs, noTypeSymbols, name);
        funcs.AddRange(callees);
        Console.WriteLine(
            $"[Recompiler] linear sweep found {swept.Count} function(s) (+{callees.Count} callees) in {name}");
    }

    private static void ScanPointers(List<MipsFunction> funcs, MipsInstruction[] instrs,
        IEnumerable<FunctionEntry> noTypeSymbols, string name)
    {
        var found = FunctionDetector.DiscoverPointers(instrs, funcs, noTypeSymbols, name);
        if (found.Count > 0)
        {
            funcs.AddRange(found);
            Console.WriteLine($"[Recompiler] pointer scan found {found.Count} entry point(s) in {name}");
        }

        var fell = FunctionDetector.DiscoverFallThroughs(instrs, funcs, name);
        if (fell.Count == 0) return;
        funcs.AddRange(fell);
        Console.WriteLine($"[Recompiler] fall-through scan found {fell.Count} entry point(s) in {name}");
    }

    private static bool ScanEscapes(List<MipsFunction> funcs, MipsInstruction[] instrs, string name)
    {
        var found = FunctionDetector.DiscoverEscapes(instrs, funcs, name);
        if (found.Count == 0) return false;
        funcs.AddRange(found);
        Console.WriteLine($"[Recompiler] escape scan found {found.Count} entry point(s) in {name}");
        return true;
    }

    private static void AnalyzeJumpTables(List<MipsFunction> funcs, FunctionInfo elfInfo, string name)
    {
        int withTables = 0, entries = 0;
        foreach (var func in funcs)
        {
            func.JumpTables = JumpTableAnalyzer.Analyze(func, elfInfo);
            if (func.JumpTables.Count == 0) continue;
            withTables++;
            foreach (var jt in func.JumpTables) entries += jt.Entries.Length;
        }

        if (withTables > 0)
            Console.WriteLine(
                $"[Recompiler] {name}: found jump tables in {withTables} function(s), {entries} entries in total");
    }

    private static void ApplyStubsAndIgnored(List<MipsFunction> funcs, IEnumerable<string> stubs,
        IEnumerable<string> ignored)
    {
        var stubSet = new HashSet<string>(stubs, StringComparer.OrdinalIgnoreCase);
        var ignoredSet = new HashSet<string>(ignored, StringComparer.OrdinalIgnoreCase);

        foreach (var func in funcs)
            if (ignoredSet.Contains(func.Name))
            {
                func.IsStub = true;
                func.Name = "__ignored__";
            }
            else if (stubSet.Contains(func.Name))
            {
                func.IsStub = true;
            }

        funcs.RemoveAll(f => f.Name == "__ignored__");
    }
}