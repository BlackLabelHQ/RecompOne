using System.Reflection;
using System.Runtime.InteropServices;
using ImGuiNET;

namespace RecompOne.Runtime.Host.Window;

public static class FontSet
{
    private const string IconResource = "RecompOne.Runtime.Host.Window.Assets.fa-solid-900.ttf";
    private const string BaseFontResource = "RecompOne.Runtime.Host.Window.Assets.NotoSans-Regular.ttf";
    private const string CjkFontResource = "RecompOne.Runtime.Host.Window.Assets.NotoSansCJK-Regular.otf";
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
    public const string Info = ""; //need to pull the rest later?

    private static readonly List<GCHandle> _pinned = [];
    private static readonly List<IntPtr> _unmanaged = [];

    public static bool Loaded { get; private set; }

    public static unsafe void Load(float sizePixels)
    {
        try
        {
            var io = ImGui.GetIO();

            var baseCfg = ImGuiNative.ImFontConfig_ImFontConfig();
            baseCfg->SizePixels = sizePixels;
            baseCfg->FontDataOwnedByAtlas = 0;

            var baseRange = BuildRange(io.Fonts.GetGlyphRangesDefault(), io.Fonts.GetGlyphRangesCyrillic(),
                io.Fonts.GetGlyphRangesGreek(), io.Fonts.GetGlyphRangesVietnamese());
            var baseData = LoadResource(BaseFontResource);
            if (baseData != null)
                io.Fonts.AddFontFromMemoryTTF(Pin(baseData), baseData.Length, sizePixels, baseCfg, baseRange);
            else
                io.Fonts.AddFontDefault(baseCfg);

            var iconData = LoadResource(IconResource);
            if (iconData != null)
            {
                var iconCfg = ImGuiNative.ImFontConfig_ImFontConfig();
                iconCfg->MergeMode = 1;
                iconCfg->PixelSnapH = 1;
                iconCfg->FontDataOwnedByAtlas = 0;
                iconCfg->GlyphMinAdvanceX = sizePixels;
                iconCfg->GlyphOffset = new System.Numerics.Vector2(0f, 1f);

                var iconRange = GCHandle.Alloc(new ushort[] { RangeMin, RangeMax, 0 }, GCHandleType.Pinned);
                _pinned.Add(iconRange);
                io.Fonts.AddFontFromMemoryTTF(Pin(iconData), iconData.Length, sizePixels, iconCfg,
                    iconRange.AddrOfPinnedObject());
            }

            var cjkData = LoadResource(CjkFontResource);
            if (cjkData != null)
            {
                var cjkCfg = ImGuiNative.ImFontConfig_ImFontConfig();
                cjkCfg->MergeMode = 1;
                cjkCfg->PixelSnapH = 1;
                cjkCfg->FontDataOwnedByAtlas = 0;

                var cjkRange = BuildRange(io.Fonts.GetGlyphRangesJapanese(),
                    io.Fonts.GetGlyphRangesChineseSimplifiedCommon());
                io.Fonts.AddFontFromMemoryTTF(Pin(cjkData), cjkData.Length, sizePixels, cjkCfg, cjkRange);
            }

            io.Fonts.Build();
            Loaded = true;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[fonts] failed to load fonts: {e.Message}");
        }
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


    private static unsafe IntPtr BuildRange(params IntPtr[] ranges)
    {
        var merged = new List<ushort>();
        foreach (var range in ranges)
        {
            var cursor = (ushort*)range;
            while (cursor[0] != 0 || cursor[1] != 0)
            {
                merged.Add(cursor[0]);
                merged.Add(cursor[1]);
                cursor += 2;
            }
        }

        merged.Add(0);

        var handle = GCHandle.Alloc(merged.ToArray(), GCHandleType.Pinned);
        _pinned.Add(handle);
        return handle.AddrOfPinnedObject();
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