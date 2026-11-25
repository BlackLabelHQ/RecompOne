using System.Text;
using RecompOne.Recompiler.Variants;
using RecompOne.Recompiler.Analysis;
using RecompOne.Recompiler.Disasm;

namespace RecompOne.Recompiler.CodeGen;

public static class FunctionEmitter
{
    public static string Emit(MipsFunction func, FunctionContext ctx)
    {
        var sb = new StringBuilder();
        var instrs = func.Instructions;

        //delay slots on condition blocks are emmited in line (before the jump) and skiped, but for the edge case where the same instruction
        //is also the target of a branch it needs to be coorectly emmited in the right position (not in delay slot), otherwise this runs on the wrong time and jumps to the condition when it shouldnt
        var dsIdx = new HashSet<int>();
        for (var i = 0; i < instrs.Length - 1; i++)
            if (instrs[i].HasDelaySlot && InstructionEmitter.SkipDelaySlot(instrs[i])
                                       && !ctx.Labels.Contains(instrs[i + 1].Vram))
                dsIdx.Add(i + 1);

        var name = func.EmittedName;
        const string ind = "        ";
        const string noInline = "    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]";


        if (func.IsStub)
        {
            sb.AppendLine(noInline);
            sb.AppendLine($"    public static void {name}(CpuContext c, IMemory m) {{ }}");
            return sb.ToString();
        }

        if (func.Variant != null && (func.IsStub || func.IsPatch))
            throw new InvalidOperationException($"{func.Name} has a variant but is stubbed or replaced, it has no code to generate one from");

        var hooked = func.Variant != null || func.PreHookTargets.Count > 0 || func.LatePreHookTargets.Count > 0 || func.PostHookTargets.Count > 0;

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

        var text = EmitBody(func, ctx, dsIdx, ind, null);

        sb.AppendLine($"    public static void {name}(CpuContext c, IMemory m)");
        sb.AppendLine("    {");
        if (text.Contains("mem."))
            sb.AppendLine(
                "        var mem = (PSMemory)m;"); //turns out the interface causes lag acess bruh, im too lazy to fix a bunc
        sb.Append(text);
        sb.AppendLine("    }");
        
        if (func.Variant != null)
        {
            sb.AppendLine(noInline);
            sb.AppendLine($"    private static void {func.EmittedName}_Variant(CpuContext c, IMemory m)");
            sb.AppendLine("    {");
            sb.AppendLine($"{ind}var mem = {func.Variant.View};");
            foreach (var code in func.Variant.OnEnter) sb.AppendLine($"{ind}{code}");
            sb.Append(EmitBody(func, ctx, dsIdx, ind, func.Variant));
            sb.AppendLine("    }");
        }
        
        return sb.ToString();
    }
    
    private static string EmitBody(MipsFunction func, FunctionContext ctx, HashSet<int> dsIdx, string ind, FunctionVariant? variant)
    {
        var instrs = func.Instructions;
        var body = new StringBuilder();
        if (ctx.Debug)
            body.AppendLine(
                $"        System.Console.WriteLine(\"{func.EmittedName} @ {func.OverlayName} @ 0x{func.Start:X8}\");");

        for (var i = 0; i < instrs.Length; i++)
        {
            if (dsIdx.Contains(i)) continue;

            var instr = instrs[i];

            if (ctx.Labels.Contains(instr.Vram))
            {
                body.AppendLine($"        L{instr.Vram:X8}: ;");
                if (ctx.BackEdges.Contains(instr.Vram))
                    body.AppendLine("        RecompOne.Runtime.Interrupts.Poll(c, m);");
            }

            var code = new StringBuilder();
            MipsInstruction? delaySlot = null;
            if (instr.HasDelaySlot)
            {
                delaySlot = i + 1 < instrs.Length ? instrs[i + 1] : null;
                InstructionEmitter.EmitWithDelaySlot(code, instr, delaySlot, ctx, ind);
            }
            else
            {
                var line = InstructionEmitter.EmitSingle(instr, ctx.Relocations, ctx.Precise);
                if (!string.IsNullOrEmpty(line))
                    code.AppendLine(ctx.Trail(instr, $"{ind}{line}"));
            }
            
            body.Append(variant == null ? code.ToString() : variant.Rewrite(instr, delaySlot, code.ToString(), ctx, ind));
        }

        if (FallsThrough(instrs))
        {
            var target = ctx.SkipNopPadding(func.End);
            if (ctx.KnownFunctions.TryGetValue(target, out var fallthroughName))
                body.AppendLine($"{ind}{fallthroughName}(c, m);");
            else
                body.AppendLine($"{ind}Dispatcher.Call(c, m, 0x{target:X8}u);");
        }

        return body.ToString();
    }

    private static void EmitHooks(StringBuilder sb, MipsFunction func, string body)
    {
        foreach (var pre in func.PreHookTargets)
            sb.AppendLine($"        if (!RecompOne.Runtime.Context.PreHook.Run({pre}, c, m)) return;");
        if (func.Variant != null)
            sb.AppendLine($"        if ({func.Variant.Condition}) {{ {func.EmittedName}_Variant(c, m); return; }}");
        foreach (var pre in func.LatePreHookTargets)
            sb.AppendLine($"        if (!RecompOne.Runtime.Context.PreHook.Run({pre}, c, m)) return;");
        sb.AppendLine(body);
        foreach (var post in func.PostHookTargets)
            sb.AppendLine($"        {post}(c, m);");
    }

    // hand written assembly sometimes doesnt have a return and expects to "fall" the execution onto the next 
    private static bool FallsThrough(MipsInstruction[] instrs)
    {
        if (instrs.Length == 0) return false;

        var idx = instrs.Length - 1;
        if (instrs.Length >= 2 && instrs[idx - 1].HasDelaySlot) idx--;

        var ctrl = instrs[idx];
        if (ctrl.IsReturn || ctrl.IsJump || ctrl.IsRegisterJump || ctrl.IsUnconditionalBranch) return false;
        if (ctrl.IsFunctionCall) return false;
        return true;
    }
}
