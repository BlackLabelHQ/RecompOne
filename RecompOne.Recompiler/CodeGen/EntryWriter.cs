using System.Text;
using RecompOne.Recompiler.Psx;

namespace RecompOne.Recompiler.CodeGen;

public static class EntryWriter
{
    public static void Write(PsxExe exe, string bootExe, string className, string? mainCall, List<string> overlays, string outDir)
    {
        var entry = new StringBuilder();
        entry.AppendLine("using RecompOne.Runtime.Cdrom;");
        entry.AppendLine("using RecompOne.Runtime.Context;");
        entry.AppendLine("using RecompOne.Runtime.Dispatch;");
        entry.AppendLine("using RecompOne.Runtime.Memory;");
        entry.AppendLine("using BiosKernel = RecompOne.Runtime.Bios.Bios;");
        entry.AppendLine();
        entry.AppendLine("namespace Recompiled;");
        entry.AppendLine();
        entry.AppendLine("public static class Entry");
        entry.AppendLine("{");
        entry.AppendLine("    public static void Run(IMemory m, string? cuePath = null, string? title = null)");
        entry.AppendLine("    {");
        entry.AppendLine($"        RecompOne.Runtime.Runtime.Initialize(title ?? \"{className}\");");
        entry.AppendLine("        RecompOne.Runtime.Runtime.WaitForValidDisc();");
        entry.AppendLine("        using var fs = DiscFs.Open(cuePath ?? RecompOne.Runtime.Runtime.CdPath);");
        entry.AppendLine("        var cd = new CdController(fs, m);");
        entry.AppendLine("        m.SetCd(cd);");
        foreach (var name in overlays)
            entry.AppendLine($"        Dispatcher.Register(\"{name}\", new {DispatchTableName(name)}());");
        entry.AppendLine("        RecompOne.Runtime.Modding.ModLoader.LoadAll();");
        entry.AppendLine($"        cd.LoadToMemory(\"{bootExe}\", 0x{exe.Destination:X8}u, 0x800, {exe.TextSize});");
        entry.AppendLine("        Dispatcher.Load(\"main\");");
        entry.AppendLine("        var c = new CpuContext();");
        entry.AppendLine($"        c.GP = 0x{exe.InitialGP:X8}u;");
        entry.AppendLine($"        c.SP = 0x{exe.InitialSP:X8}u;");
        entry.AppendLine("        c.FP = c.SP;");
        entry.AppendLine("        c.RA = 0u;");
        entry.AppendLine("        RecompOne.Runtime.Runtime.SetContext(c, m);");
        entry.AppendLine("        BiosKernel.Init(m);");
        entry.AppendLine(mainCall != null
            ? $"        {mainCall}(c, m);"
            : $"        Dispatcher.Call(c, m, 0x{exe.InitialPC:X8}u);");
        entry.AppendLine("    }");
        entry.AppendLine("}");

        var stubs = new StringBuilder();
        stubs.AppendLine("using RecompOne.Runtime.Context;");
        stubs.AppendLine("using RecompOne.Runtime.Memory;");
        stubs.AppendLine();
        stubs.AppendLine("namespace Recompiled;");
        stubs.AppendLine();
        stubs.AppendLine("public static class Bios");
        stubs.AppendLine("{");
        stubs.AppendLine("    public static void Syscall(CpuContext c, IMemory m) { }");
        stubs.AppendLine("    public static void Break(CpuContext c, IMemory m) { }");
        stubs.AppendLine("}");

        File.WriteAllText(Path.Combine(outDir, "Entry.cs"),   entry.ToString());
        File.WriteAllText(Path.Combine(outDir, "Stubs.cs"),   stubs.ToString());
    }

    static string DispatchTableName(string name) => $"{char.ToUpperInvariant(name[0])}{name[1..]}DispatchTable";
}
