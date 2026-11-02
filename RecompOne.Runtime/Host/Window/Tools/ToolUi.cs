using System.Collections.Concurrent;
using NativeFileDialogNET;
using RecompOne.Runtime.Assets.Textures;
using RecompOne.Runtime.Hle;
using Silk.NET.OpenGL;

namespace RecompOne.Runtime.Host.Window;

internal static class ToolUi
{
    //sum thread shit, windows doent like running a file dialogue on the same thread apaprently
    //https://stackoverflow.com/questions/32634243/threadstateexception-on-openfiledialog-with-no-threads seens to be related
    private static readonly BlockingCollection<Action> _requests = new();
    private static readonly ConcurrentQueue<Action> _results = new();
    private static Thread? _thread;
    private static volatile bool _busy;

    public static bool DialogOpen => _busy;

    public static void PickFile(string filterName, string extensions, Action<string> picked, string? directory = null)
    {
        Show(picked, "file", () =>
        {
            using var dialog = new NativeFileDialog().SelectFile().AddFilter(filterName, extensions);
            return dialog.Open(out string? path, directory, null) == DialogResult.Okay ? path : null;
        });
    }

    public static void PickFolder(Action<string> picked, string? directory = null)
    {
        Show(picked, "folder", () =>
        {
            using var dialog = new NativeFileDialog().SelectFolder();
            return dialog.Open(out string? path, directory, null) == DialogResult.Okay ? path : null;
        });
    }

    public static void SaveFile(string filterName, string extension, string defaultName, Action<string> picked,
        string? directory = null)
    {
        Show(picked, "save", () =>
        {
            using var dialog = new NativeFileDialog().SaveFile().AddFilter(filterName, extension);
            if (dialog.Open(out string? path, directory, defaultName) != DialogResult.Okay || string.IsNullOrWhiteSpace(path))
                return null;
            return Path.HasExtension(path) ? path : path + "." + extension;
        });
    }

    public static void Show(Action<string> picked, string kind, Func<string?> open)
    {
        if (_busy) return;
        _busy = true;
        if (!OperatingSystem.IsWindows())
        {
            Run(picked, kind, open);
            return;
        }

        Start();
        _requests.Add(() => Run(picked, kind, open));
    }

    private static void Run(Action<string> picked, string kind, Func<string?> open)
    {
        try
        {
            if (open() is { Length: > 0 } path) _results.Enqueue(() => picked(path));
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[tools] {kind} dialog failed: {e.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    public static void Pump()
    {
        while (_results.TryDequeue(out var result))
            try
            {
                result();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"[tools] dialog result failed: {e.Message}");
            }
    }
    //psh thread
    private static void Start()
    {
        if (_thread != null) return;
        _thread = new Thread(() =>
        {
            foreach (var request in _requests.GetConsumingEnumerable()) request();
        }) { IsBackground = true, Name = "file-dialog" };
        if (OperatingSystem.IsWindows()) _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
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
