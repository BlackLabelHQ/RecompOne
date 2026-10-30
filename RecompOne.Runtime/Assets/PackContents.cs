namespace RecompOne.Runtime.Assets;

public sealed class PackContents
{
    public sealed record Texture(string File, ulong Index, ulong Clut, TextureMode Mode);

    public sealed record Sound(string File, ulong Hash, SfxEntryDto? Options);

    public List<Texture> Textures { get; } = [];
    public List<Sound> Sounds { get; } = [];
    public List<XaEntryDto> Music { get; } = [];

    public static bool TryParseTexture(string file, out ulong index, out ulong clut, out TextureMode mode)
    {
        index = clut = 0;
        mode = TextureMode.Rgba;

        var name = Path.GetFileName(file);
        if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return false;

        var stem = name[..^4];
        if (stem.EndsWith(".idx", StringComparison.OrdinalIgnoreCase))
        {
            stem = stem[..^4];
            mode = TextureMode.Indexed;
        }

        var parts = stem.Split('_');
        if (!AssetHash.TryParseHex(parts[0], out index) || parts[0].Length != 16) return false;
        return parts.Length < 2 || (parts[1].Length == 16 && AssetHash.TryParseHex(parts[1], out clut));
    }

    public static bool TryParseSound(string file, out ulong hash)
    {
        hash = 0;
        var ext = Path.GetExtension(file);
        if (!ext.Equals(".wav", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase))
            return false;
        var stem = Path.GetFileNameWithoutExtension(file);
        return stem.Length == 16 && AssetHash.TryParseHex(stem, out hash);
    }

    public static PackContents Read(AssetPack pack)
    {
        var contents = new PackContents();

        foreach (var file in pack.ListAssets("textures"))
            if (TryParseTexture(file, out var index, out var clut, out var mode))
                contents.Textures.Add(new Texture(file, index, clut, mode));

        if (pack.Manifest.Textures != null)
            foreach (var dto in pack.Manifest.Textures)
            {
                if (string.IsNullOrWhiteSpace(dto.File) || !AssetHash.TryParseHex(dto.Index ?? dto.Hash, out var index))
                    continue;
                ulong clut = 0;
                if (!string.IsNullOrWhiteSpace(dto.Clut) && !dto.Clut!.Equals("any", StringComparison.OrdinalIgnoreCase))
                    AssetHash.TryParseHex(dto.Clut, out clut);
                var mode = dto.Mode?.ToLowerInvariant() == "indexed" ? TextureMode.Indexed : TextureMode.Rgba;
                contents.Textures.RemoveAll(t => t.File == dto.File);
                contents.Textures.Add(new Texture(dto.File!, index, clut, mode));
            }

        var options = new Dictionary<ulong, SfxEntryDto>();
        if (pack.Manifest.Sfx != null)
            foreach (var dto in pack.Manifest.Sfx)
                if (AssetHash.TryParseHex(dto.Hash, out var hash))
                    options[hash] = dto;

        foreach (var file in pack.ListAssets("sfx"))
            if (TryParseSound(file, out var hash))
                contents.Sounds.Add(new Sound(file, hash, options.GetValueOrDefault(hash)));

        foreach (var (hash, dto) in options)
            if (!string.IsNullOrWhiteSpace(dto.File) && contents.Sounds.All(s => s.Hash != hash))
                contents.Sounds.Add(new Sound(dto.File!, hash, dto));

        if (pack.Manifest.Xa != null) contents.Music.AddRange(pack.Manifest.Xa);
        return contents;
    }
}
