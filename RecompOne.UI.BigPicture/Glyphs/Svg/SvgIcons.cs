namespace RecompOne.UI.BigPicture.Glyphs.Svg;

public static class SvgIcons
{
    private const string Prefix = "Glyphs/";

    private static readonly Dictionary<string, string> _resources = Index();
    private static readonly Dictionary<string, SvgIcon?> _icons = new();
    private static readonly Dictionary<(string Name, int Size), uint> _textures = new();

    public static bool Exists(string name) => _resources.ContainsKey(name);

    public static uint Texture(string name, int size)
    {
        if (size <= 0) return 0;
        if (_textures.TryGetValue((name, size), out var cached)) return cached;

        var icon = Load(name);
        var texture = icon == null ? 0u : HostWindow.UploadTexture(icon.Rasterize(size, size), size, size);
        _textures[(name, size)] = texture;
        return texture;
    }

    private static SvgIcon? Load(string name)
    {
        if (_icons.TryGetValue(name, out var icon)) return icon;
        icon = null;
        if (_resources.TryGetValue(name, out var resource))
        {
            try
            {
                using var stream = typeof(SvgIcons).Assembly.GetManifestResourceStream(resource);
                if (stream != null) icon = SvgIcon.Parse(new StreamReader(stream).ReadToEnd());
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"[SvgIcons] {name}: {e.Message}");
            }
        }

        _icons[name] = icon;
        return icon;
    }

    private static Dictionary<string, string> Index()
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in typeof(SvgIcons).Assembly.GetManifestResourceNames())
        {
            var name = resource.Replace('\\', '/');
            if (!name.StartsWith(Prefix, StringComparison.Ordinal) || !name.EndsWith(".svg", StringComparison.Ordinal)) continue;
            index[name[Prefix.Length..^4]] = resource;
        }

        return index;
    }
}
