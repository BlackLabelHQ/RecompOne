using System.Text;
using RecompOne.Recompiler.Analysis;
using RecompOne.Recompiler.Disasm;

namespace RecompOne.Recompiler.CodeGen;

public static class FunctionEmitter
{
    public static string Emit(MipsFunction func, FunctionContext ctx)
    {
        var sb = new StringBuilder();
        var instrs = func.Instructions;
        
        //a delay slot of an unconditional transfer is emitted only inline (before the
        // jump) and skipped here but  for the edge case where that same instruction is also a branch target it needs to
        // also be emitted at its ""natural(?)"" position (and the label too) so jumps to it need to be into the following
        //  instructions instead of into the preceding jump in that case it shouldnt be skiped, otherwise it will be emmited in the wrong location and cause crashes
        // on the functions with this edge case
        var dsIdx = new HashSet<int>();
        for (int i = 0; i < instrs.Length - 1; i++)
            if (instrs[i].HasDelaySlot && InstructionEmitter.SkipDelaySlot(instrs[i])
                && !ctx.Labels.Contains(instrs[i + 1].Vram))
                dsIdx.Add(i + 1);

        string name = func.EmittedName;
        const string ind = "        ";
        const string noInline = "    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]";

        if (func.IsStub)
        {
            sb.AppendLine(noInline);
            sb.AppendLine($"    public static void {name}(CpuContext c, IMemory m) {{ }}");
            return sb.ToString();
        }
        bool hooked = func.PreHookTargets.Count > 0 || func.PostHookTargets.Count > 0;

        if (func.IsPatch)
        {
            sb.AppendLine(noInline);
            if (!hooked)
            {
                sb.AppendLine($"    public static void {name}(CpuContext c, IMemory m) => {func.PatchTarget}(c, m);");
                return sb.ToString();
            }
            sb.AppendLine($"    public static void {name}(CpuContext c, IMemory m)");
            sb.AppendLine("    {");
            EmitHooks(sb, func, $"        {func.PatchTarget}(c, m);");
            sb.AppendLine("    }");
            return sb.ToString();
        }
        if (hooked)
        {
            sb.AppendLine(noInline);
            sb.AppendLine($"    public static void {name}(CpuContext c, IMemory m)");
            sb.AppendLine("    {");
            EmitHooks(sb, func, $"        {name}_Impl(c, m);");
            sb.AppendLine("    }");
            name += "_Impl";
        }

        sb.AppendLine(noInline);
        sb.AppendLine($"    public static void {name}(CpuContext c, IMemory m)");
        sb.AppendLine("    {");
        if (ctx.Debug)
            sb.AppendLine($"        System.Console.WriteLine(\"{func.EmittedName} @ {func.OverlayName} @ 0x{func.Start:X8}\");");

        for (int i = 0; i < instrs.Length; i++)
        {
            if (dsIdx.Contains(i)) continue;

            var instr = instrs[i];

            if (ctx.Labels.Contains(instr.Vram))
            {
                sb.AppendLine($"        L{instr.Vram:X8}: ;");
                if (ctx.BackEdges.Contains(instr.Vram))
                    sb.AppendLine("        RecompOne.Runtime.Interrupts.Poll(c, m);");
            }

            if (instr.HasDelaySlot)
            {
                var delaySlot = i + 1 < instrs.Length ? instrs[i + 1] : null;
                InstructionEmitter.EmitWithDelaySlot(sb, instr, delaySlot, ctx, ind);
            }else
            {
                string line = InstructionEmitter.EmitSingle(instr);
                if (!string.IsNullOrEmpty(line))
                    sb.AppendLine(ctx.Trail(instr, $"{ind}{line}"));
            }
        }

        if (FallsThrough(instrs))
        {
            uint target = ctx.SkipNopPadding(func.End);
            if (ctx.KnownFunctions.TryGetValue(target, out var fallthroughName))
                sb.AppendLine($"{ind}{fallthroughName}(c, m);");
            else
                sb.AppendLine($"{ind}Dispatcher.Call(c, m, 0x{target:X8}u);");
        }

        sb.AppendLine("    }");
        return sb.ToString();
    }

    static void EmitHooks(StringBuilder sb, MipsFunction func, string body)
    {
        foreach (var pre in func.PreHookTargets)
            sb.AppendLine($"        if (!RecompOne.Runtime.Context.PreHook.Run({pre}, c, m)) return;");
        sb.AppendLine(body);
        foreach (var post in func.PostHookTargets)
            sb.AppendLine($"        {post}(c, m);");
    }

    //some hand-written asm (crt0 stubs, etc) has no jr/j/branch at its declared end at all and just
    // runs straight into the next symbol if a jal at the end is a call, not a fall-through, so its left alone
    static bool FallsThrough(MipsInstruction[] instrs)
    {
        if (instrs.Length == 0) return false;

        int idx = instrs.Length - 1;
        if (instrs.Length >= 2 && instrs[idx - 1].HasDelaySlot) idx--;

        var ctrl = instrs[idx];
        if (ctrl.IsReturn || ctrl.IsJump || ctrl.IsRegisterJump || ctrl.IsUnconditionalBranch) return false;
        if (ctrl.IsFunctionCall) return false;
        return true;
    }
}
