using RecompOne.Recompiler.Analysis;

namespace RecompOne.Recompiler.Variants;

public sealed class VariantBinder //hooks in the variant
{
    private readonly List<VariantRule> _rules = [];
    
    public void Apply(List<MipsFunction> funcs, VariantGroupConfig[] groups)
    {
        foreach (var group in groups)
        foreach (var (overlay, config) in group.Overlays)
        {
            var functions = new Dictionary<string, string[]>(group.Functions);
            foreach (var name in config.Without) functions.Remove(name);
            foreach (var (name, roles) in config.Functions) functions[name] = roles;
            
            var shared = SharedRules(group, overlay);
            foreach (var (name, roles) in functions)
            {
                var func = Find(funcs, overlay, name) ?? throw new InvalidOperationException($"variant '{group.Name}': {name} was not found in {overlay}");
                if (func.Variant != null)
                    throw new InvalidOperationException($"variant '{group.Name}': {func.Name} was already has a variant");
                func.Variant = Bind(group, config, overlay, func, roles, shared);
            }
        }
        
        Console.WriteLine($"[Recompiler] bound {funcs.Count(f => f.Variant != null)} function variants");
    }
    
    public void Verify()
    {
        var unused = _rules.Where(r => !r.Applied).Select(r => r.Description).ToArray();
        if (unused.Length > 0)
            throw new InvalidOperationException($"variant rules that matched nothing:{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", unused)}");
    }
    
    private static MipsFunction? Find(List<MipsFunction> funcs, string overlay, string name)
    {
        uint? address = name.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToUInt32(name, 16) : null;
        var prefixed = $"{overlay.ToUpperInvariant()}_{name}";
        var suffixed = $"{name}_{overlay}";
        foreach (var func in funcs)
        {
            if (!string.Equals(func.OverlayName, overlay, StringComparison.OrdinalIgnoreCase)) continue;
            if (address == func.Start || func.Name == name || func.Name == prefixed || func.Name == suffixed) return func;
        }
        
        return null;
    }
    
    //group rules only have to apply somewhere in the overlay
    private List<CallRule> SharedRules(VariantGroupConfig group, string overlay)
    {
        var rules = new List<CallRule>();
        AddCalls(rules, group.Rules, $"{group.Name}/{overlay}", s => s);
        return rules;
    }
    
    private void AddCalls(List<CallRule> rules, VariantRulesConfig config, string where, Func<string, string> expand)
    {
        foreach (var (function, code) in config.ReplaceCall)
            rules.Add(new CallRule(function, expand(code), null, Track($"{where}: replaceCall {function}")));
        foreach (var (function, code) in config.GuardCall)
            rules.Add(new CallRule(function, null, expand(code), Track($"{where}: guardCall {function}")));
    }
    
    private VariantRule Track(string description)
    {
        var rule = new VariantRule(description);
        _rules.Add(rule);
        return rule;
    }
    
    private FunctionVariant Bind(VariantGroupConfig group, VariantOverlayConfig overlay, string overlayName, MipsFunction func,
        string[] roles, List<CallRule> shared)
    {
        var where = $"{group.Name}/{overlayName}/{func.Name}";
        var vars = new Dictionary<string, string>(group.Vars) { ["overlay"] = overlayName };
        foreach (var (key, value) in overlay.Vars) vars[key] = value;
        
        var rules = roles.Select(role => group.Roles.TryGetValue(role, out var found)
            ? found
            : throw new InvalidOperationException($"{where}: unknown role for '{role}'")).ToArray();
        foreach (var rule in rules)
        foreach (var (key, value) in rule.Vars)
            vars[key] = value;
        
        string Expand(string text)
        {
            foreach (var (key, value) in vars) text = text.Replace($"{{{key}}}", value);
            return text;
        }
        
        var variant = new FunctionVariant { Condition = Expand(group.Condition), View = Expand(group.View) };
        foreach (var call in shared)
            variant.Calls.Add(call with
            {
                Replacement = call.Replacement == null ? null : Expand(call.Replacement),
                Guard = call.Guard == null ? null : Expand(call.Guard)
            });
        
        foreach (var rule in rules)
        {
            if (rule.OnEnter != null) variant.OnEnter.Add(Expand(rule.OnEnter));
            if (rule.OnExit != null) variant.OnExit.Add((Expand(rule.OnExit), Track($"{where}: onExit")));
            AddCalls(variant.Calls, rule, where, Expand);
            foreach (var immediate in rule.ReplaceImmediate)
                variant.Immediates.Add(new ImmediateRule(immediate.Op, immediate.Value, Expand(immediate.With), immediate.Occurrence,
                    Track($"{where}: replaceImmediate {immediate.Op} {immediate.Value}")));
        }
        
        return variant;
    }
}
