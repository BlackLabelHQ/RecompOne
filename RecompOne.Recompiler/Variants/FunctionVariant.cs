using RecompOne.Recompiler.CodeGen;
using RecompOne.Recompiler.Disasm;

namespace RecompOne.Recompiler.Variants;

//note: if has no maches must dispatch an error to warn about
public sealed class VariantRule(string description)
{
    public string Description { get; } = description;
    public bool Applied { get; set; }
    public int Matches { get; set; }
}

public sealed record CallRule(string Function, string? Replacement, string? Guard, VariantRule Use);
public sealed record ImmediateRule(string Op, int Value, string With, int Occurrence, VariantRule Use);

public sealed class FunctionVariant
{
    public required string Condition { get; init; }
    public required string View { get; init; }
    public List<string> OnEnter { get; } = [];
    public List<(string Code, VariantRule Use)> OnExit { get; } = [];
    public List<CallRule> Calls { get; } = [];
    public List<ImmediateRule> Immediates { get; } = [];

    public string Rewrite(MipsInstruction instr, MipsInstruction? delay, string code, FunctionContext ctx, string indent)
    {
        foreach (var rule in Immediates)
        {
            code = ReplaceImmediate(rule, instr, code);
            if (delay != null) code = ReplaceImmediate(rule, delay, code);
        }
        
        if (TargetName(instr, ctx) is { } name)
            foreach (var rule in Calls)
                code = RewriteCall(rule, name, code);
        
        if (instr.IsReturn)
            foreach (var (exit, use) in OnExit)
            {
                var at = code.LastIndexOf($"{indent}return;", StringComparison.Ordinal);
                if (at < 0) continue;
                code = code.Insert(at, $"{indent}{exit}{Environment.NewLine}");
                use.Applied = true;
            }
        
        return code;
    }
    
    private static string? TargetName(MipsInstruction instr, FunctionContext ctx)
    {
        if (!instr.HasDelaySlot || instr.IsRegisterJump) return null;
        var target = instr.IsFunctionCall || instr.IsJump ? instr.JumpTarget : instr.BranchTarget;
        return ctx.KnownFunctions.GetValueOrDefault(target);
    }
    
    private static string RewriteCall(CallRule rule, string name, string code)
    {
        if (name[(name.LastIndexOf('.') + 1)..] != rule.Function) return code;
        
        var call = $"{name}(c, m);";
        if (!code.Contains(call, StringComparison.Ordinal)) return code;
        rule.Use.Applied = true;
        return code.Replace(call, rule.Replacement ?? $"if ({rule.Guard}) {call}", StringComparison.Ordinal);
    }
    
    private static string ReplaceImmediate(ImmediateRule rule, MipsInstruction instr, string code)
    {
        if (instr.UniqueId.ToString() != $"cpu_{rule.Op}" || instr.ImmS != rule.Value) return code;
        
        rule.Use.Matches++;
        if (rule.Occurrence != 0 && rule.Use.Matches != rule.Occurrence) return code;
        
        var literal = $"0x{rule.Value:X}u";
        if (!code.Contains(literal, StringComparison.Ordinal))
            throw new InvalidOperationException($"{rule.Use.Description}: 0x{instr.Vram:X8} hasnt no literal {literal}");
        
        rule.Use.Applied = true;
        return code.Replace(literal, rule.With, StringComparison.Ordinal);
    }
}
