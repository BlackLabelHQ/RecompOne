using NativeFileDialogNET;
using RecompOne.Runtime.Assets.Textures;
using RecompOne.Runtime.Hle;
using Silk.NET.OpenGL;

namespace RecompOne.Runtime.Host.Window;

internal static class ToolUi
{
    public static string? PickFile(string filterName, string extensions, string? directory = null)
    {
        try
        {
            using var dialog = new NativeFileDialog().SelectFile().AddFilter(filterName, extensions);
            return dialog.Open(out string? picked, directory, null) == DialogResult.Okay && !string.IsNullOrWhiteSpace(picked)
                ? picked
                : null;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[tools] file dialog failed: {e.Message}");
            return null;
        }
    }

    public static string? PickFolder()
    {
        try
        {
            using var dialog = new NativeFileDialog().SelectFolder();
            return dialog.Open(out string? picked, null, null) == DialogResult.Okay && !string.IsNullOrWhiteSpace(picked)
                ? picked
                : null;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[tools] folder dialog failed: {e.Message}");
            return null;
        }
    }

    public static string? SaveFile(string filterName, string extension, string defaultName)
    {
        try
        {
            using var dialog = new NativeFileDialog().SaveFile().AddFilter(filterName, extension);
            if (dialog.Open(out string? picked, null, defaultName) != DialogResult.Okay || string.IsNullOrWhiteSpace(picked))
                return null;
            return Path.HasExtension(picked) ? picked : picked + "." + extension;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[tools] save dialog failed: {e.Message}");
            return null;
        }
    }
}

internal sealed class ImageCache
{
    private readonly record struct Entry(uint Tex, int W, int H);

    private readonly Dictionary<string, Entry> _entries = [];
    private readonly HashSet<string> _failed = [];

    public (uint Tex, int W, int H) Get(string key, Func<byte[]?> load)
    {
        if (_entries.TryGetValue(key, out var hit)) return (hit.Tex, hit.W, hit.H);
        if (_failed.Contains(key)) return (0, 0, 0);

        try
        {
            var data = load();
            if (data == null) throw new FileNotFoundException(key);
            var img = StbImageSharp.ImageResult.FromMemory(data, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            var tex = HostWindow.UploadTexture(img.Data, img.Width, img.Height);
            if (tex == 0) throw new InvalidOperationException("upload failed");
            _entries[key] = new Entry(tex, img.Width, img.Height);
            return (tex, img.Width, img.Height);
        }
        catch
        {
            _failed.Add(key);
            return (0, 0, 0);
        }
    }

    public void Clear()
    {
        var gl = GpuGlAccess.Gl;
        if (gl != null)
            foreach (var entry in _entries.Values)
                gl.DeleteTexture(entry.Tex);
        _entries.Clear();
        _failed.Clear();
    }

    public static uint Seen(SeenTexture t)
    {
        var gl = GpuGlAccess.Gl;
        if (gl == null || t.Thumb.Length == 0) return 0;
        if (t.GlTex != 0) return t.GlTex;

        t.GlTex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, t.GlTex);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        gl.TexImage2D<byte>(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)t.W, (uint)t.H, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, t.Thumb);
        gl.BindTexture(TextureTarget.Texture2D, 0);
        return t.GlTex;
    }

    public static void ReleaseOrphans()
    {
        var gl = GpuGlAccess.Gl;
        var ids = TextureRegistry.TakeOrphanTextures();
        if (gl == null || ids.Length == 0) return;
        foreach (var id in ids) gl.DeleteTexture(id);
    }
}
