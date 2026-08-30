namespace RecompOne.Runtime.Assets;

public sealed class PackManifest
{
    public int FormatVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string? Preview { get; set; }
    public int Priority { get; set; }
    public PackGame? Game { get; set; }
    public PackDefaults? Defaults { get; set; }
    public List<XaEntryDto>? Xa { get; set; }
    public XaOptionsDto? XaOptions { get; set; }
    public List<TextureEntryDto>? Textures { get; set; }
    public List<ClutEntryDto>? Cluts { get; set; }
    public List<TextureRuleDto>? TextureRules { get; set; }
}

public sealed class TextureRuleDto
{
    public List<int>? Texpages { get; set; }
    public int? Bpp { get; set; }
    public int? MinWidth { get; set; }
    public int? MinHeight { get; set; }
    public int? MaxWidth { get; set; }
    public int? MaxHeight { get; set; }
    public string? File { get; set; }
    public string? Note { get; set; }
}

public sealed class TextureEntryDto
{
    public string? Index { get; set; }
    public string? Hash { get; set; }
    public string? Clut { get; set; }
    public string? Mode { get; set; }
    public string? File { get; set; }
    public string? Note { get; set; }
}

public sealed class ClutEntryDto
{
    public string? Clut { get; set; }
    public string? Hash { get; set; }
    public string? File { get; set; }
    public string? Note { get; set; }
}

public sealed class PackGame
{
    public string? Id { get; set; }
    public List<string>? Ids { get; set; }
    public bool Strict { get; set; } = true;

    public IEnumerable<string> All()
    {
        if (!string.IsNullOrWhiteSpace(Id)) yield return Id.Trim();
        if (Ids == null) yield break;
        foreach (var s in Ids)
            if (!string.IsNullOrWhiteSpace(s))
                yield return s.Trim();
    }
}

public sealed class PackDefaults
{
    public XaEntryDto? Xa { get; set; }
}

public sealed class XaOptionsDto
{
    public bool? AllowStr { get; set; }
    public int? SeekToleranceMs { get; set; }
}

public sealed class XaEntryDto
{
    public string? File { get; set; }
    public int? FileNumber { get; set; }
    public int? Channel { get; set; }
    public int? StartLba { get; set; }
    public string? Hash { get; set; }
    public string? Audio { get; set; }
    public float? Gain { get; set; }
    public bool? Loop { get; set; }
    public double? LoopStart { get; set; }
    public double? LoopEnd { get; set; }
    public bool? Extend { get; set; }
    public int? ExtendMaxMs { get; set; }
    public string? OnShorter { get; set; }
    public int? TailMs { get; set; }
    public bool? AllowStr { get; set; }
    public string? Note { get; set; }
}