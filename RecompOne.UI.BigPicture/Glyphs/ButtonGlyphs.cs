namespace RecompOne.UI.BigPicture.Glyphs;

public static class ButtonGlyphs
{
    private const string ComboSeparator = " + ";
    private const string ChoiceSeparator = " / ";
    
    private static readonly Dictionary<string, InlineIcon> _inline = new(StringComparer.OrdinalIgnoreCase);
    
    public static PadStyle Style => InputManager.MenuPadStyle;
    
    public static string MenuCombo => Style switch
    {
        PadStyle.SteamDeck or PadStyle.SteamController => Combo(PadButton.L3, PadButton.R3),
        _ => $"{Token(PadButton.Home)}{ChoiceSeparator}{Combo(PadButton.L3, PadButton.R3)}"
    };
    
    public static void Install()
    {
        _inline.Clear();
        foreach (var button in Enum.GetValues<PadButton>())
            _inline[GlyphCatalog.Name(button)] = new InlineIcon(size => Width(GlyphCatalog.Path(Style, button), GlyphCatalog.Label(Style, button), size),
                (draw, pos, size, color) => Pad(draw, pos, size, button, color));
        
        foreach (var key in Enum.GetValues<KeyButton>())
            _inline[GlyphCatalog.Name(key)] = new InlineIcon(size => Width(GlyphCatalog.Path(key), GlyphCatalog.Label(key), size),
                (draw, pos, size, color) => Key(draw, pos, size, key, color));
        
        InlineIcons.Resolver = static name => _inline.GetValueOrDefault(name);
    }
    
    public static string Token(PadButton button) => InlineIcons.Token(GlyphCatalog.Name(button));
    
    public static string Token(KeyButton key) => InlineIcons.Token(GlyphCatalog.Name(key));
    
    public static string Combo(params PadButton[] buttons) => string.Join(ComboSeparator, buttons.Select(Token));
    
    public static float Pad(ImDrawListPtr draw, Vector2 pos, float size, PadButton button, uint color)
    {
        return Icon(draw, pos, size, GlyphCatalog.Path(Style, button), GlyphCatalog.Label(Style, button), color);
    }
    
    public static float Key(ImDrawListPtr draw, Vector2 pos, float size, KeyButton key, uint color)
    {
        return Icon(draw, pos, size, GlyphCatalog.Path(key), GlyphCatalog.Label(key), color);
    }
    
    private static float Icon(ImDrawListPtr draw, Vector2 pos, float size, string? path, string fallback, uint color)
    {
        var px = (int)MathF.Round(size);
        var texture = path == null ? 0u : SvgIcons.Texture(path, px);
        if (texture == 0) return KeyCap.Draw(draw, pos, size, fallback, color);
        
        var at = Snap(pos);
        draw.AddImage(ImGuiUtils.Tex((nint)texture), at, at + new Vector2(px, px), Vector2.Zero, Vector2.One, color);
        return px;
    }
    
    private static float Width(string? path, string fallback, float size)
    {
        if (path != null && SvgIcons.Exists(path)) return MathF.Round(size);
        return KeyCap.Width(size, fallback);
    }
}
