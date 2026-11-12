using System.Reflection;
using System.Runtime.InteropServices;

namespace RecompOne.UI;

public static class FontSet
{
    private const string IconResource = "RecompOne.UI.Assets.fa-solid-900.ttf";
    private const string BaseFontResource = "RecompOne.UI.Assets.NotoSans-Regular.ttf";
    private const string CjkFontResource = "RecompOne.UI.Assets.NotoSansCJK-Regular.otf";
    private const ushort RangeMin = 0xE005;
    private const ushort RangeMax = 0xF8FF;

    public const string Gear = "";
    public const string Ellipsis = "";
    public const string Folder = "";
    public const string FolderOpen = "";
    public const string Rotate = "";
    public const string Trash = "";
    public const string Check = "";
    public const string Xmark = "";
    public const string Play = "";
    public const string Pause = "";
    public const string Warning = "";
    public const string Circle = "";
    public const string Puzzle = "";
    public const string Download = "";
    public const string Pen = "\uf304";
    public const string Palette = "\uf53f";
    public const string Language = "\uf1ab";
    public const string Gamepad = "\uf11b";
    public const string Desktop = "\uf108";
    public const string Volume = "\uf028";
    public const string Wand = "\uf0d0";
    public const string Bug = "\uf188";
    public const string Sliders = "\uf1de";
    public const string Power = "\uf011";
    public const string Microchip = "\uf2db";
    public const string ChevronDown = "\uf078"; //
    public const string Plus = "\uf067";
    public const string FileImport = "\uf56f";
    public const string Grip = "\uf58e";
    public const string Star = "\uf005";
    public const string ExternalLink = "\uf35d";
    public const string Info = ""; //need to pull the rest later?

    public const float BaseSize = 16f;

    private static readonly List<GCHandle> _pinned = [];
    private static readonly List<IntPtr> _unmanaged = [];

    public static bool Loaded { get; private set; }

    public static unsafe void Load()
    {
        try
        {
            var io = ImGui.GetIO();

            var baseData = LoadResource(BaseFontResource);
            if (baseData != null)
                io.Fonts.AddFontFromMemoryTTF((void*)Pin(baseData), baseData.Length, BaseSize, NewConfig(merge: false, snap: false));
            else
                io.Fonts.AddFontDefault();

            var iconData = LoadResource(IconResource);
            if (iconData != null)
            {
                var iconCfg = NewConfig(merge: true, snap: true);
                iconCfg->GlyphMinAdvanceX = BaseSize;
                iconCfg->GlyphOffset = new System.Numerics.Vector2(0f, 1f);
                io.Fonts.AddFontFromMemoryTTF((void*)Pin(iconData), iconData.Length, BaseSize, iconCfg, IconRange());
            }

            var cjkData = LoadResource(CjkFontResource);
            if (cjkData != null)
                io.Fonts.AddFontFromMemoryTTF((void*)Pin(cjkData), cjkData.Length, BaseSize, NewConfig(merge: true, snap: true));

            Loaded = true;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[fonts] failed to load fonts: {e.Message}");
        }
    }

    public static unsafe ImFontPtr LoadIcons()
    {
        var io = ImGui.GetIO();
        var data = LoadResource(IconResource);
        if (data == null) return io.Fonts.Fonts.Data[0];
        return io.Fonts.AddFontFromMemoryTTF((void*)Pin(data), data.Length, BaseSize, NewConfig(merge: false, snap: true), IconRange());
    }

    private static unsafe ImFontConfig* NewConfig(bool merge, bool snap)
    {
        var config = ImGui.ImFontConfig();
        config.MergeMode = merge;
        config.PixelSnapH = snap;
        config.FontDataOwnedByAtlas = false;
        return config.Handle;
    }

    private static unsafe uint* IconRange()
    {
        var range = GCHandle.Alloc(new uint[] { RangeMin, RangeMax, 0 }, GCHandleType.Pinned);
        _pinned.Add(range);
        return (uint*)range.AddrOfPinnedObject();
    }

    private static byte[]? LoadResource(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var s = asm.GetManifestResourceStream(name);
        if (s == null)
        {
            Console.Error.WriteLine($"[fonts] {name} not found");
            return null;
        }

        var bytes = new byte[s.Length];
        s.ReadExactly(bytes);
        return bytes;
    }

    private static IntPtr Pin(byte[] bytes)
    {
        var ptr = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        _unmanaged.Add(ptr);
        return ptr;
    }


    public static string With(string icon, string label)
    {
        return Loaded ? $"{icon}  {label}" : label;
    }

    public static string Or(string icon, string fallback)
    {
        return Loaded ? icon : fallback;
    }
}