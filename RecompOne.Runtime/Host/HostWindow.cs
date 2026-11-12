using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using RecompOne.Runtime.Config;
using RecompOne.Runtime.Hardware;

namespace RecompOne.Runtime.Host;

public static class HostWindow
{
    private static IWindow? _window;
    private static GL? _gl;
    private static bool _headless;
    private static Gpu? _gpu;

    private static Hle.GlCore? _glBackend;

    private static bool _closed;

    private static float _dpiScale = 1f;

    public static float DpiScale => _dpiScale;
    
    public static HostContext? Context { get; private set; }
    
    public static Vector3 ClearColor { get; set; } = new(0.08f, 0.08f, 0.08f);

    private static unsafe float QueryDpiScale()
    {
        try
        {
            var glfw = Silk.NET.GLFW.Glfw.GetApi();
            var monitor = glfw.GetPrimaryMonitor();
            if (monitor != null)
            {
                glfw.GetMonitorContentScale(monitor, out var xs, out var ys);
                var s = MathF.Max(xs, ys);
                if (s >= 0.5f && s <= 8f) return s;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Host] cant read scale: {e.Message}");
        }

        try
        {
            var fb = _window!.FramebufferSize;
            var size = _window.Size;
            if (size.X > 0 && fb.X > 0)
            {
                var s = (float)fb.X / size.X;
                if (s >= 0.5f && s <= 8f) return s;
            }
        }
        catch
        {
        }

        return 1f;
    }

    private static GraphicsAPI[] ApiChain()
    {
        if (OperatingSystem.IsMacOS())
            return
            [
                new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible,
                    new APIVersion(4, 1))
            ];

        var requested = Hle.GpuBackendFactory.Parse(DisplayOptions.Backend);
        var core45 = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default,
            new APIVersion(4, 5));
        var core33 = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default,
            new APIVersion(3, 3));
        var compat21 = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Compatability, ContextFlags.Default,
            new APIVersion(2, 1));

        return requested switch
        {
            Hle.GlBackendKind.Gl21 => [compat21],
            Hle.GlBackendKind.Gl33 => [core33, compat21],
            _ => [core45, core33, compat21]
        };
    }

    public static void Initialize(string title)
    {
        Settings.EnsureLoaded();
        Interp.Interp.Load();

        foreach (var api in ApiChain())
            try
            {
                var options = WindowOptions.Default with
                {
                    Size = new Vector2D<int>(1280, 720),
                    Title = title,
                    VSync = DisplayOptions.VSync,
                    UpdatesPerSecond = 0,
                    FramesPerSecond = 0,
                    WindowState = DisplayOptions.Fullscreen ? WindowState.Fullscreen : WindowState.Normal,
                    API = api
                };
                _window = Silk.NET.Windowing.Window.Create(options);
                FrameClock.VSync = DisplayOptions.VSync;
                Interp.Interp.VSync = DisplayOptions.VSync;
                Interp.Interp.RefreshRate = QueryRefreshRate();
                Console.WriteLine($"[Host] monitor refresh: {Interp.Interp.RefreshRate} hz");
                _window.Load += OnLoad;
                _window.Render += OnRender;
                _window.Closing += OnClosing;
                _window.Initialize();
                Console.WriteLine($"[Host] gl context {api.Version.MajorVersion}.{api.Version.MinorVersion} {api.Profile}");
                return;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(
                    $"[Host] context {api.Version.MajorVersion}.{api.Version.MinorVersion} unavailable: {e.Message}");
                _window = null;
            }

        Console.Error.WriteLine("[Host] no usable gl context were found");
        _headless = true;
    }

    public static string Title
    {
        get => _window?.Title ?? "";
        set
        {
            if (_window != null) _window.Title = value ?? "";
        }
    }

    public static void SetTitle(string title)
    {
        Title = title;
    }

    private static Silk.NET.Core.RawImage? _pendingIcon;

    public static void SetIcon(byte[] data)
    {
        try
        {
            var rgba = Decode(data, out var w, out var h);
            if (rgba == null)
            {
                Console.Error.WriteLine("[Host] icon format not supported");
                return;
            }

            SetIcon(rgba, w, h);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[Host] failed to set icon: {e.Message}");
        }
    }

    public static void SetIcon(byte[] rgba, int width, int height)
    {
        if (width <= 0 || height <= 0 || rgba.Length < width * height * 4)
        {
            Console.Error.WriteLine("[Host] icon pixel buffer does not match its size");
            return;
        }

        var image = new Silk.NET.Core.RawImage(width, height, rgba);
        _pendingIcon = image;
        Apply(image);
    }

    public static void ClearIcon()
    {
        _pendingIcon = null;
        if (_window == null) return;
        try
        {
            _window.SetWindowIcon(ReadOnlySpan<Silk.NET.Core.RawImage>.Empty);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[Host] failed to clear icon: {e.Message}");
        }
    }

    private static void Apply(Silk.NET.Core.RawImage image)
    {
        if (_window == null) return;
        try
        {
            var icons = new[] { image };
            _window.SetWindowIcon(icons);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[Host] failed to set icon: {e.Message}");
        }
    }

    private static byte[]? Decode(byte[] data, out int width, out int height)
    {
        width = height = 0;
        if (data.Length < 4) return null;

        if (data[0] == 0 && data[1] == 0 && data[2] == 1 && data[3] == 0)
        {
            var best = LargestIcoEntry(data);
            if (best == null) return null;
            data = best;
        }

        var img = StbImageSharp.ImageResult.FromMemory(data, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        if (img == null) return null;
        width = img.Width;
        height = img.Height;
        return img.Data;
    }

    private static byte[]? LargestIcoEntry(byte[] ico)
    {
        int count = BitConverter.ToUInt16(ico, 4);
        var bestArea = -1;
        byte[]? best = null;

        for (var i = 0; i < count; i++)
        {
            var e = 6 + i * 16;
            if (e + 16 > ico.Length) break;

            var w = ico[e] == 0 ? 256 : ico[e];
            var h = ico[e + 1] == 0 ? 256 : ico[e + 1];
            var size = BitConverter.ToInt32(ico, e + 8);
            var offset = BitConverter.ToInt32(ico, e + 12);
            if (size <= 0 || offset < 0 || offset + size > ico.Length) continue;

            var png = size > 8 && ico[offset] == 0x89 && ico[offset + 1] == 0x50 &&
                      ico[offset + 2] == 0x4E && ico[offset + 3] == 0x47;
            if (!png) continue;

            var area = w * h;
            if (area <= bestArea) continue;
            bestArea = area;
            best = ico.AsSpan(offset, size).ToArray();
        }

        return best;
    }

    public static void Present(Gpu? gpu)
    {
        _gpu = gpu;
        if (_headless || _window == null) return;
        try
        {
            _window.DoEvents();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }

        if (_window.IsClosing)
        {
            Runtime.Shutdown();
            Environment.Exit(0);
        }

        InputManager.Poll();
        HandleShortcuts();

        _window.DoRender();
        FrameClock.MarkPresent();
    }

    public static bool Ready => !_headless && _window != null;
    
    public static void PumpEvents()
    {
        if (_headless || _window == null) return;
        
        try
        {
            _window.DoEvents();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
        
        if (_window.IsClosing)
        {
            Runtime.Shutdown();
            Environment.Exit(0);
        }
        
        InputManager.Poll();
        
        HandleShortcuts();
    }

    private static void HandleShortcuts()
    {
        if (InputManager.ConsumeTopBarToggle() && !InterfaceOptions.AutoHideMenuBar)
            InterfaceOptions.HideMenuBar.Value = !InterfaceOptions.HideMenuBar;

        if (InputManager.ConsumeFullscreenToggle())
        {
            DisplayOptions.Fullscreen.Value = !DisplayOptions.Fullscreen;
            SetFullscreen(DisplayOptions.Fullscreen);
        }
    }
    
    public static void Compose(Gpu? gpu, bool counts = true)
    {
        _gpu = gpu;
        if (_headless || _window == null) return;
        
        try
        {
            _window.DoRender();
        }
        catch (NotImplementedException) //doesnt fucking work
        {
        }
        
        if (counts) FrameClock.MarkPresent();
    }
    
    public static void Pump()
    {
        if (_headless || _window == null) return;
        try
        {
            _window.DoEvents();
        }
        catch
        {
        }

        if (_window.IsClosing)
        {
            Runtime.Shutdown();
            Environment.Exit(0);
        }

        _window.DoRender();
    }

    public static void Shutdown()
    {
        if (!_headless && _window != null && !_window.IsClosing)
            _window.Close();
        InputManager.Shutdown();
    }

    public static void SetFullscreen(bool on)
    {
        if (_window == null) return;
        _window.WindowState = on ? WindowState.Fullscreen : WindowState.Normal;
        if (on) SetAutoIconify(false);
    }

    private static unsafe void SetAutoIconify(bool on)
    {
        try
        {
            var handle = _window?.Native?.Glfw;
            if (handle is not { } h) return;
            Silk.NET.GLFW.Glfw.GetApi().SetWindowAttrib(
                (Silk.NET.GLFW.WindowHandle*)h,
                Silk.NET.GLFW.WindowAttributeSetter.AutoIconify, on);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Host] auto-iconify unavailable: {e.Message}");
        }
    }

    public static bool IsKeyDown(Key k)
    {
        return InputManager.IsKeyDown(k);
    }

    public static void WaitForValidDisc() // wait for disc path to be valid before running it!!
    {
        if (_headless || _window == null || Runtime.UserInterface is not { } ui) return;

        while (ui.BlocksStartup)
            HandOverFrame();

        while (true)
        {
            var path = Settings.System.DiscPath;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) && Runtime.ValidateDisc(path) == null)
                return;

            HandOverFrame();
        }
    }
    
    public static string PickDisc(string missing)
    {
        if (_headless || _window == null || Runtime.UserInterface is not { } ui) return missing;

        while (true)
        {
            GpuJobs.Run(() => ui.PickDisc(missing));
            do HandOverFrame();
            while (ui.PickingDisc);

            var path = Settings.System.DiscPath;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) && Runtime.ValidateDisc(path) == null)
                return path;
        }
    }
    
    private static void HandOverFrame()
    {
        var closing = false;

        GpuJobs.Run(() =>
        {
            try
            {
                _window!.DoEvents();
            }
            catch
            {
            }

            if (_window!.IsClosing)
            {
                closing = true;
                return;
            }

            InputManager.Poll();
            _window.DoRender();
        });

        if (!closing) return;

        Runtime.Shutdown();
        Environment.Exit(0);
    }

    private static void OnLoad()
    {
        var input = _window!.CreateInput();
        InputManager.Initialize(input);

        if (_pendingIcon is { } icon) Apply(icon);

        _gl = GL.GetApi(_window);
        _gl.ClearColor(0.08f, 0.08f, 0.08f, 1f);

        var fb = _window!.FramebufferSize;
        _gl.Viewport(0, 0, (uint)fb.X, (uint)fb.Y);
        _window.FramebufferResize += size => _gl?.Viewport(0, 0, (uint)size.X, (uint)size.Y);
        HostOutput.Load(_gl);

        Hle.GlVram.Scale = DisplayOptions.RenderScale;
        Hle.GlVram.TrueColor = DisplayOptions.TrueColor;
        _glBackend = (Hle.GlCore)Hle.GpuBackendFactory.Create(_gl,
            Hle.GpuBackendFactory.Parse(DisplayOptions.Backend));
        _glBackend.InitGl();
        Hle.GpuHle.Active = _glBackend.Ready;
        Hle.GpuHle.Backend = new Interp.InterpBackend(_glBackend);
        ApplySwapInterval();

        Interp.InterpOptions.TargetFps.Changed += _ => ApplySwapInterval();
        
        _dpiScale = QueryDpiScale();
        Console.WriteLine($"[Host] display scale: {_dpiScale:0.##}x");
        if (DisplayOptions.Fullscreen) SetAutoIconify(false);
        
        Cdrom.DiscFs.MissingDisc = PickDisc;
        Context = new HostContext(_gl, _window, input);
        Runtime.UserInterface?.Load(Context);
    }

    public static void SetVSync(bool on)
    {
        if (_window != null) _window.VSync = on;
        FrameClock.VSync = on;
        Interp.Interp.VSync = on;
        ApplySwapInterval();
        FrameClock.Resync();
    }
    
    public static void AdvanceFrame()
    {
        _glBackend?.AdvanceFrame();
    }
    
    public static void RefreshVSync()
    {
        ApplySwapInterval();
    }
    
    private static void ApplySwapInterval()
    {
        if (_headless || _window == null) return;
        
        var refresh = QueryRefreshRate();
        if (refresh > 0 && refresh != Interp.Interp.RefreshRate)
        {
            Interp.Interp.RefreshRate = refresh;
            Console.WriteLine($"[Host] monitor refresh: {refresh} hz");
        }
        
        var target = Features.FrameInterpolation ? Interp.InterpOptions.TargetFps.Value : 0;
        var divisor = target > 0 && refresh > target && refresh % target == 0 ? refresh / target : 1;
        var interval = Interp.Interp.VSync ? -divisor : 0;
        if (!SetSwapInterval(interval)) interval = SetSwapInterval(divisor) ? divisor : 0;
        
        Console.WriteLine($"[Host] swap interval: {interval}" +
                          (interval < 0 ? " (adaptive)" : ""));
    }
    
    private static bool SetSwapInterval(int interval)
    {
        try
        {
            Silk.NET.GLFW.Glfw.GetApi().SwapInterval(interval);
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    private static unsafe int QueryRefreshRate()
    {
        try
        {
            var glfw = Silk.NET.GLFW.Glfw.GetApi();
            var monitor = glfw.GetPrimaryMonitor();
            if (monitor == null) return 0;
            
            var mode = glfw.GetVideoMode(monitor);
            if (mode == null) return 0;
            
            var rate = mode->RefreshRate;
            return rate is > 0 and <= 1000 ? rate : 0;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Host] cant read refresh rate: {e.Message}");
            return 0;
        }
    }

    private static void OnRender(double dt)
    {
        var gl = _gl!;
        var ui = Runtime.UserInterface;
        ui?.BeginFrame(dt);
        
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        var size = _window!.FramebufferSize;
        gl.Viewport(0, 0, (uint)size.X, (uint)size.Y);
        var clear = ClearColor;
        gl.ClearColor(clear.X, clear.Y, clear.Z, 1f);
        gl.Clear(ClearBufferMask.ColorBufferBit);
        
        Runtime.RamLog.Tick();
        Settings.SaveIfDue();
        Assets.AssetReplacerManager.Instance.Pump();
        HostOutput.Update(gl, _gpu, _glBackend, size.X, size.Y);
        
        ui?.Draw();
        
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        gl.Viewport(0, 0, (uint)size.X, (uint)size.Y);
        ui?.EndFrame();
    }
    
    private static void OnClosing()
    {
        if (_closed) return;
        _closed = true;
        Runtime.UserInterface?.Close();
        Settings.SaveIfDirty();
        _glBackend?.Dispose();
        if (_gl != null) HostOutput.Unload(_gl);
    }

    public static uint UploadPng(byte[] png)
    {
        try
        {
            var img = StbImageSharp.ImageResult.FromMemory(png, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            if (img == null || img.Width <= 0 || img.Height <= 0) return 0;

            int w = img.Width, h = img.Height;
            if (w == h) return UploadTexture(img.Data, w, h);

            var s = Math.Min(w, h);
            var ox = (w - s) / 2;
            var oy = (h - s) / 2;
            var square = new byte[s * s * 4];
            for (var y = 0; y < s; y++)
                Array.Copy(img.Data, ((oy + y) * w + ox) * 4, square, y * s * 4, s * 4);
            return UploadTexture(square, s, s);
        }
        catch
        {
            return 0;
        }
    }

    public static uint UploadTexture(byte[] rgba, int width, int height)
    {
        if (_gl == null || width <= 0 || height <= 0) return 0;
        var needed = width * height * 4;
        if (rgba.Length < needed) return 0;
        var tex = CreateTexture(_gl);
        _gl.BindTexture(TextureTarget.Texture2D, tex);
        _gl.TexImage2D<byte>(TextureTarget.Texture2D, 0, InternalFormat.Rgba,
            (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, rgba.AsSpan(0, needed));
        return tex;
    }

    internal static uint CreateTexture(GL gl)
    {
        var tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, tex);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        return tex;
    }
}
