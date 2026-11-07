using System.Text.Json.Serialization;

namespace RecompOne.Recompiler.Variants;

public sealed class VariantGroupConfig
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("condition")] public string Condition { get; set; } = "";
    [JsonPropertyName("view")] public string View { get; set; } = "";
    [JsonPropertyName("vars")] public Dictionary<string, string> Vars { get; set; } = [];
    [JsonPropertyName("rules")] public VariantRulesConfig Rules { get; set; } = new();
    [JsonPropertyName("roles")] public Dictionary<string, VariantRulesConfig> Roles { get; set; } = [];
    [JsonPropertyName("functions")] public Dictionary<string, string[]> Functions { get; set; } = [];
    [JsonPropertyName("overlays")] public Dictionary<string, VariantOverlayConfig> Overlays { get; set; } = [];
}

public sealed class VariantOverlayConfig
{
    [JsonPropertyName("vars")] public Dictionary<string, string> Vars { get; set; } = [];
    [JsonPropertyName("functions")] public Dictionary<string, string[]> Functions { get; set; } = [];
    [JsonPropertyName("without")] public string[] Without { get; set; } = [];
}

public sealed class VariantRulesConfig
{
    [JsonPropertyName("vars")] public Dictionary<string, string> Vars { get; set; } = [];
    [JsonPropertyName("onEnter")] public string? OnEnter { get; set; }
    [JsonPropertyName("onExit")] public string? OnExit { get; set; }
    [JsonPropertyName("replaceCall")] public Dictionary<string, string> ReplaceCall { get; set; } = [];
    [JsonPropertyName("guardCall")] public Dictionary<string, string> GuardCall { get; set; } = [];
    [JsonPropertyName("replaceImmediate")] public ImmediateRuleConfig[] ReplaceImmediate { get; set; } = [];
}

public sealed class ImmediateRuleConfig
{
    [JsonPropertyName("op")] public string Op { get; set; } = "";
    [JsonPropertyName("value")] public int Value { get; set; }
    [JsonPropertyName("with")] public string With { get; set; } = "";
    [JsonPropertyName("occurrence")] public int Occurrence { get; set; }
}
