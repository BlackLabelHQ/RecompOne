namespace RecompOne.Runtime.Assets.Textures;

public static class TextureStretch //config to allow expanding text
{
    private static readonly (string Name, TextureExpand Side)[] Sides =
    [
        ("left", TextureExpand.Left),
        ("right", TextureExpand.Right),
        ("top", TextureExpand.Top),
        ("bottom", TextureExpand.Bottom)
    ];

    public static TextureFilter ParseFilter(string? name)
    {
        return name?.ToLowerInvariant() switch
        {
            "nearest" => TextureFilter.Nearest,
            "linear" => TextureFilter.Linear,
            _ => TextureFilter.Auto
        };
    }

    public static string? FilterName(TextureFilter filter)
    {
        return filter switch
        {
            TextureFilter.Nearest => "nearest",
            TextureFilter.Linear => "linear",
            _ => null
        };
    }

    public static TextureExpand ParseExpand(IEnumerable<string>? names)
    {
        var expand = TextureExpand.None;
        if (names == null) return expand;

        foreach (var name in names)
        foreach (var side in Sides)
            if (side.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                expand |= side.Side;

        return expand;
    }

    public static List<string>? ExpandNames(TextureExpand expand)
    {
        if (expand == TextureExpand.None) return null;

        var names = new List<string>();
        foreach (var side in Sides)
            if ((expand & side.Side) != 0)
                names.Add(side.Name);

        return names;
    }

    public static void Apply(TextureAsset asset, ReplacementTexture tex, in TileRect rect)
    {
        if (asset.Expand == TextureExpand.None || tex.Mode != TextureMode.Rgba) return;

        var horizontal = (asset.Expand & (TextureExpand.Left | TextureExpand.Right)) != 0;
        var vertical = (asset.Expand & (TextureExpand.Top | TextureExpand.Bottom)) != 0;
        var scale = horizontal && vertical ? asset.Scale
            : horizontal ? tex.Height / (float)rect.H
            : tex.Width / (float)rect.W;

        if (horizontal)
            Split(tex.Width / scale - rect.W, asset.Expand, TextureExpand.Left, TextureExpand.Right,
                out tex.GrowLeft, out tex.GrowRight);

        if (vertical)
            Split(tex.Height / scale - rect.H, asset.Expand, TextureExpand.Top, TextureExpand.Bottom,
                out tex.GrowTop, out tex.GrowBottom);
    }

    private static void Split(float extra, TextureExpand expand, TextureExpand first, TextureExpand second,
        out float growFirst, out float growSecond)
    {
        var both = (expand & first) != 0 && (expand & second) != 0;
        growFirst = both ? extra * 0.5f : (expand & first) != 0 ? extra : 0f;
        growSecond = both ? extra * 0.5f : (expand & second) != 0 ? extra : 0f;
    }
}
