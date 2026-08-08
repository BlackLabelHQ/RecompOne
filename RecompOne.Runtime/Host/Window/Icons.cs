using System.Reflection;
using System.Runtime.InteropServices;
using ImGuiNET;

namespace RecompOne.Runtime.Host.Window;
public static class Icons
{
    const string Resource = "RecompOne.Runtime.Host.Window.Assets.fa-solid-900.ttf";
    const ushort RangeMin = 0xE005;
    const ushort RangeMax = 0xF8FF;

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

    static GCHandle _range;
    static IntPtr _data;

    public static bool Loaded { get; private set; }
    
    public static unsafe void Load(float sizePixels)
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var s = asm.GetManifestResourceStream(Resource);
            if (s == null)
            {
                Console.Error.WriteLine($"[Icons] {Resource} not found, icons disabled");
                return;
            }

            var bytes = new byte[s.Length];
            s.ReadExactly(bytes);

            //note to self: imgui keep both pointers alive until the atlas is destroyed, so they cannot be temporaries
            _data = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, _data, bytes.Length);
            _range = GCHandle.Alloc(new ushort[] { RangeMin, RangeMax, 0 }, GCHandleType.Pinned);

            var cfg = ImGuiNative.ImFontConfig_ImFontConfig();
            cfg->MergeMode = 1;
            cfg->PixelSnapH = 1;
            cfg->FontDataOwnedByAtlas = 0;
            cfg->GlyphMinAdvanceX = sizePixels;
            cfg->GlyphOffset = new System.Numerics.Vector2(0f, 1f);

            var io = ImGui.GetIO();
            io.Fonts.AddFontFromMemoryTTF(_data, bytes.Length, sizePixels, cfg, _range.AddrOfPinnedObject());
            io.Fonts.Build();
            Loaded = true;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[Icons] failed to load icon font: {e.Message}");
        }
    }
    
    public static string With(string icon, string label) => Loaded ? $"{icon}  {label}" : label;

    public static string Or(string icon, string fallback) => Loaded ? icon : fallback;
}
