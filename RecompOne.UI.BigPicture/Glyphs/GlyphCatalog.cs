namespace RecompOne.UI.BigPicture.Glyphs;

public static class GlyphCatalog
{
    private const string KeyboardFolder = "keyboard";
    
    private static readonly string[] _fallback = ["xbox"];
    
    private static readonly Dictionary<PadButton, string> _pad = new()
    {
        [PadButton.South] = "south",
        [PadButton.East] = "east",
        [PadButton.West] = "west",
        [PadButton.North] = "north",
        [PadButton.Dpad] = "dpad",
        [PadButton.DpadUp] = "dpad_up",
        [PadButton.DpadDown] = "dpad_down",
        [PadButton.DpadLeft] = "dpad_left",
        [PadButton.DpadRight] = "dpad_right",
        [PadButton.L1] = "l1",
        [PadButton.R1] = "r1",
        [PadButton.L2] = "l2",
        [PadButton.R2] = "r2",
        [PadButton.L3] = "l3",
        [PadButton.R3] = "r3",
        [PadButton.LeftStick] = "ls",
        [PadButton.RightStick] = "rs",
        [PadButton.Start] = "start",
        [PadButton.Select] = "select",
        [PadButton.Home] = "home"
    };
    
    private static readonly Dictionary<KeyButton, (string Name, string Label)> _keys = new()
    {
        [KeyButton.Enter] = ("enter", "Enter"),
        [KeyButton.Escape] = ("escape", "Esc")
    };
    
    private static readonly Dictionary<PadStyle, string[]> _folders = new()
    {
        [PadStyle.Xbox] = ["xbox"],
        [PadStyle.PlayStation] = ["playstation"],
        [PadStyle.PlayStation5] = ["playstation5", "playstation"],
        [PadStyle.Nintendo] = ["nintendo"],
        [PadStyle.SteamDeck] = ["steamdeck"],
        [PadStyle.SteamController] = ["steamcontroller"]
    };
    
    private static readonly Dictionary<(PadStyle Style, PadButton Button), string> _labels = new()
    {
        [(PadStyle.PlayStation, PadButton.Home)] = "PS",
        [(PadStyle.PlayStation5, PadButton.Home)] = "PS"
    };
    
    public static string Name(PadButton button) => _pad[button];
    
    public static string Name(KeyButton key) => _keys[key].Name;
    
    public static string Label(PadStyle style, PadButton button)
    {
        return _labels.GetValueOrDefault((style, button)) ?? button.ToString();
    }
    
    public static string Label(KeyButton key) => _keys[key].Label;
    
    public static void Folders(PadStyle style, params string[] folders)
    {
        _folders[style] = folders;
    }
    
    public static void Label(PadStyle style, PadButton button, string label)
    {
        _labels[(style, button)] = label;
    }
    
    public static string? Path(PadStyle style, PadButton button)
    {
        var name = Name(button);
        foreach (var folder in _folders.GetValueOrDefault(style, _fallback))
            if (SvgIcons.Exists($"{folder}/{name}"))
                return $"{folder}/{name}";
        return null;
    }
    
    public static string Path(KeyButton key) => $"{KeyboardFolder}/{Name(key)}";
}
