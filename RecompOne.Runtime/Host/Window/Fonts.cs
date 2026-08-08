using System.Reflection;
using System.Runtime.InteropServices;
using ImGuiNET;

namespace RecompOne.Runtime.Host.Window;

// Loads the primary UI text font (Inter, OFL-licensed) directly into the atlas at the
// target scaled pixel size, instead of leaning on io.FontGlobalScale to bitmap-stretch
// ImGui's built-in low-res default font. Must run before Icons.Load, since that merges
// icon glyphs into whichever font was added most recently.
public static class Fonts
{
    const string Resource = "RecompOne.Runtime.Host.Window.Assets.Inter-Regular.ttf";

    static IntPtr _data;

    public static bool Loaded { get; private set; }

    public static unsafe void LoadBody(float sizePixels)
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var s = asm.GetManifestResourceStream(Resource);
            if (s == null)
            {
                Console.Error.WriteLine($"[Fonts] {Resource} not found, falling back to default font");
                ImGui.GetIO().Fonts.AddFontDefault();
                return;
            }

            var bytes = new byte[s.Length];
            s.ReadExactly(bytes);

            //same lifetime note as Icons.Load: imgui keeps this pointer past the call, so it must not be a temporary
            _data = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, _data, bytes.Length);

            var cfg = ImGuiNative.ImFontConfig_ImFontConfig();
            cfg->FontDataOwnedByAtlas = 0;

            var io = ImGui.GetIO();
            io.Fonts.AddFontFromMemoryTTF(_data, bytes.Length, sizePixels, cfg);
            Loaded = true;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[Fonts] failed to load body font: {e.Message}");
            ImGui.GetIO().Fonts.AddFontDefault();
        }
    }
}
