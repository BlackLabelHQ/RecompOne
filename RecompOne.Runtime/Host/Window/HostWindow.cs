using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using RecompOne.Runtime.Config;
using RecompOne.Runtime.Hardware;
using RecompOne.Runtime.Host.Window;

namespace RecompOne.Runtime.Host;

public static class HostWindow
{
    private static IWindow? _window;
    private static GL? _gl;
    private static ImGuiController? _imgui;
    private static bool _headless;
    private static Gpu? _gpu;

    private static uint _displayTex;
    private static uint _vramTex;
    private static uint _ramTex;
    private static Hle.GlCore? _glBackend;

    private static byte[] _rgbDisplay = [];
    private static byte[] _rgbVram = [];
    private static byte[] _ramFront = new byte[Memory.RamLogger.Width * Memory.RamLogger.Height * 4];
    private static byte[] _ramBack = new byte[Memory.RamLogger.Width * Memory.RamLogger.Height * 4];
    private static Task? _ramTask;
    private static volatile bool _ramReady;
    private static int _ramFrame;

    private static bool _layoutPending = true;
    private static bool _closed;

    private const int RedockCooldownFrames = 8;
    private static int _redockCooldown;

    public static void RequestLayout()
    {
        _layoutPending = true;
    }

    private static float _dpiScale = 1f;

    public static float DpiScale => _dpiScale;

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

        var requested = Hle.GpuBackendFactory.Parse(ConfigManager.View.GpuBackend);
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
        ConfigManager.Load();
        Pgxp.Pgxp.Load();

        foreach (var api in ApiChain())
            try
            {
                var options = WindowOptions.Default with
                {
                    Size = new Vector2D<int>(1280, 720),
                    Title = title,
                    VSync = ConfigManager.View.VSync,
                    UpdatesPerSecond = 0,
                    FramesPerSecond = 0,
                    WindowState = ConfigManager.View.Fullscreen ? WindowState.Fullscreen : WindowState.Normal,
                    API = api
                };
                _window = Silk.NET.Windowing.Window.Create(options);
                FrameClock.VSync = ConfigManager.View.VSync;
                _window.Load += OnLoad;
                _window.Render += OnRender;
                _window.Closing += OnClosing;
                _window.Initialize();
                Console.WriteLine(
                    $"[Host] gl context {api.Version.MajorVersion}.{api.Version.MinorVersion} {api.Profile}");
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
        if (InputManager.ConsumeTopBarToggle())
        {
            ConfigManager.View.HideTopBar = !ConfigManager.View.HideTopBar;
            ConfigManager.SaveView(PanelManager.Panels);
        }

        if (InputManager.ConsumeFullscreenToggle())
        {
            ConfigManager.View.Fullscreen = !ConfigManager.View.Fullscreen;
            SetFullscreen(ConfigManager.View.Fullscreen);
            ConfigManager.SaveView(PanelManager.Panels);
        }

        _window.DoRender();
    }

    internal static void Pump()
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

    public static void RequestDiscPath()
    {
        PopupManager.Open<DiscPickerPopup>();
    }

    public static void WaitForValidDisc() // wait for disc path to be valid before running it!!
    {
        if (_headless || _window == null) return;

        while (StartupNoticePopup.NeedsAck)
        {
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

            InputManager.Poll();
            _window.DoRender();
        }

        while (true)
        {
            var path = ConfigManager.Game.CdPath;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) && Runtime.ValidateDisc(path) == null)
                return;

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

            InputManager.Poll();
            _window.DoRender();
        }
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
        _displayTex = CreateTexture(_gl);
        _vramTex = CreateTexture(_gl);
        _ramTex = CreateTexture(_gl);

        Hle.GlVram.Scale = ConfigManager.View.RenderScale;
        _glBackend = (Hle.GlCore)Hle.GpuBackendFactory.Create(_gl,
            Hle.GpuBackendFactory.Parse(ConfigManager.View.GpuBackend));
        _glBackend.InitGl();
        Hle.GpuHle.Active = _glBackend.Ready;
        Hle.GpuHle.Backend = _glBackend;

        _imgui = new ImGuiController(_gl, _window, input, null, ConfigureImGui);

        PanelManager.Register(new OutputPanel());
        PanelManager.Register(new VramViewerPanel());
        PanelManager.Register(new TextureInspectorPanel());
        PanelManager.Register(new CpuStatePanel());
        PanelManager.Register(new RamMapPanel());
        PanelManager.Register(new MemoryEditorPanel());
        PanelManager.Register(new SpuViewerPanel());
        PanelManager.Register(new CdDebugPanel());
        PanelManager.Register(new ConsolePanel());
        PanelManager.Register(new OverlayEventsPanel());

        PopupManager.Register(new SettingsPopup());
        PopupManager.Register(new ModsPopup());
        PopupManager.Register(new ModLoadingPopup());
        PopupManager.Register(new NoticePopup());
        PopupManager.Register(new StartupNoticePopup());
        PopupManager.Register(new DiscPickerPopup());

        MainMenuBar.RegisterBuiltins();

        SettingsRegistry.Register(new InterfaceSettingsSection());
        SettingsRegistry.Register(new InputSettingsSection());
        SettingsRegistry.Register(new DisplaySettingsSection());
        SettingsRegistry.Register(new PathsSettingsSection());
        SettingsRegistry.Register(new AudioSettingsSection());

        ConfigManager.ApplyViewToPanels(PanelManager.Panels);

        var cdPath = ConfigManager.Game.CdPath;
        if (string.IsNullOrWhiteSpace(cdPath) || !File.Exists(cdPath) || Runtime.ValidateDisc(cdPath) != null)
            PopupManager.Open<DiscPickerPopup>();
    }

    private static void ConfigureImGui()
    {
        _dpiScale = QueryDpiScale();
        Console.WriteLine($"[Host] display scale: {_dpiScale:0.##}x");

        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.ConfigWindowsMoveFromTitleBarOnly = true;
        io.FontGlobalScale = ConfigManager.View.UiScale;
        unsafe
        {
            io.NativePtr->IniFilename = null;
        }

        FontSet.Load(16f * _dpiScale);
        Localization.Load();
        Theme.Load();

        if (ConfigManager.ApplyImGuiLayout())
            _layoutPending = false;

        if (ConfigManager.View.Fullscreen) SetAutoIconify(false);
    }

    public static void SetVSync(bool on)
    {
        if (_window != null) _window.VSync = on;
        FrameClock.VSync = on;
        FrameClock.Resync();
    }

    private static void OnRender(double dt)
    {
        var gl = _gl!;
        _imgui!.Update((float)dt);

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        var fbDef = _window!.FramebufferSize;
        gl.Viewport(0, 0, (uint)fbDef.X, (uint)fbDef.Y);
        var clear = Theme.Background;
        gl.ClearColor(clear.X, clear.Y, clear.Z, 1f);
        gl.Clear(ClearBufferMask.ColorBufferBit);

        Runtime.RamLog.Tick();
        Memory.RamLogger.TrackReads =
            PanelManager.Get<RamMapPanel>()?.IsOpen == true ||
            PanelManager.Get<MemoryEditorPanel>()?.IsOpen == true;
        Memory.RamLogger.TrackWrites = Memory.RamLogger.TrackReads;

        var gpu = _gpu;
        if (gpu != null)
        {
            if (Hle.GpuHle.Active && _glBackend is { Ready: true } && gpu.DisplayEnabled)
            {
                var wf = _window!.FramebufferSize;
                var (tex, tw, th, aspect) = _glBackend.PresentDisplay(
                    gpu.DisplayX, gpu.DisplayY,
                    gpu.DisplayWidth, gpu.DisplayHeight,
                    gpu.Display24Bit,
                    wf.X, wf.Y);
                if (tex != 0) OutputPanel.SetTexture(tex, tw, th, aspect);
                gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                gl.Viewport(0, 0, (uint)wf.X, (uint)wf.Y);
            }
            else
            {
                UploadDisplayTexture(gl, gpu);
            }

            if (PanelManager.Get<VramViewerPanel>()?.IsOpen == true)
                UploadVramTexture(gl, gpu);
        }

        if (PanelManager.Get<RamMapPanel>()?.IsOpen == true)
        {
            QueueRamConvert();
            if (_ramReady) FlushRamTexture(gl);
        }

        if (!ConfigManager.View.HideTopBar)
            MainMenuBar.Draw();

        DrawDockspace();
        PanelManager.DrawPanels();
        PopupManager.Draw();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        gl.Viewport(0, 0, (uint)fbDef.X, (uint)fbDef.Y);
        _imgui.Render();
    }

    private static void DrawDockspace()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.WorkPos);
        ImGui.SetNextWindowSize(viewport.WorkSize);
        ImGui.SetNextWindowViewport(viewport.ID);

        const ImGuiWindowFlags hostFlags = ImGuiWindowFlags.NoDocking |
                                           ImGuiWindowFlags.NoTitleBar |
                                           ImGuiWindowFlags.NoCollapse |
                                           ImGuiWindowFlags.NoResize |
                                           ImGuiWindowFlags.NoMove |
                                           ImGuiWindowFlags.NoBringToFrontOnFocus |
                                           ImGuiWindowFlags.NoBackground;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.Begin("##DockHost", hostFlags);
        ImGui.PopStyleVar(3);
        var dockId = ImGui.GetID("##MainDock");
        var openCount = PanelManager.Panels.Count(p => p.IsOpen && p is not IFloatingPanel);
        var dockFlags = openCount <= 1 ? (ImGuiDockNodeFlags)4096 : ImGuiDockNodeFlags.None;
        ImGui.DockSpace(dockId, Vector2.Zero, dockFlags);

        if (openCount <= 1 && !OutputPanel.IsDocked && _redockCooldown == 0)
            _layoutPending = true;

        if (_redockCooldown > 0) _redockCooldown--;

        if (_layoutPending)
        {
            _layoutPending = false;
            _redockCooldown = RedockCooldownFrames;
            if (PanelManager.Get<OutputPanel>() is { } output)
                DockBuilder.SetupCenterLayout(dockId, viewport.WorkSize, output.Title());
        }

        ImGui.End();
    }

    private static void OnClosing()
    {
        if (_closed) return;
        _closed = true;
        ConfigManager.SaveView(PanelManager.Panels);
        ConfigManager.SaveGame();
        PanelManager.Shutdown();
        PopupManager.Shutdown();
        _glBackend?.Dispose();
        _imgui?.Dispose();
        _gl?.DeleteTexture(_displayTex);
        _gl?.DeleteTexture(_vramTex);
        _gl?.DeleteTexture(_ramTex);
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

    private static uint CreateTexture(GL gl)
    {
        var tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, tex);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        return tex;
    }

    private static void UploadDisplayTexture(GL gl, Gpu gpu)
    {
        int w = gpu.DisplayWidth, h = gpu.DisplayHeight;
        if (!gpu.DisplayEnabled || w <= 0 || h <= 0) return;
        var needed = w * h * 3;
        if (_rgbDisplay.Length < needed) _rgbDisplay = new byte[needed];
        ConvertDisplay(gpu, w, h);
        gl.BindTexture(TextureTarget.Texture2D, _displayTex);
        gl.TexImage2D<byte>(TextureTarget.Texture2D, 0, InternalFormat.Rgb, (uint)w, (uint)h, 0,
            PixelFormat.Rgb, PixelType.UnsignedByte, _rgbDisplay.AsSpan(0, needed));
        OutputPanel.SetTexture(_displayTex, w, h);
    }

    private static ushort[] _vramView = new ushort[Gpu.VramWidth * Gpu.VramHeight];

    private static void UploadVramTexture(GL gl, Gpu gpu)
    {
        const int sz = Gpu.VramWidth * Gpu.VramHeight * 3;
        if (_rgbVram.Length < sz) _rgbVram = new byte[sz];
        ushort[] src;
        if (Hle.GpuHle.Active && _glBackend is { Ready: true })
        {
            _glBackend.ReadVram(0, 0, Gpu.VramWidth, Gpu.VramHeight, _vramView);
            src = _vramView;
        }
        else
        {
            src = gpu.Vram;
        }

        ConvertVramToBuffer(src, _rgbVram);
        gl.BindTexture(TextureTarget.Texture2D, _vramTex);
        gl.TexImage2D<byte>(TextureTarget.Texture2D, 0, InternalFormat.Rgb, Gpu.VramWidth, Gpu.VramHeight, 0,
            PixelFormat.Rgb, PixelType.UnsignedByte, _rgbVram.AsSpan(0, sz));
        VramViewerPanel.SetTexture(_vramTex, Gpu.VramWidth, Gpu.VramHeight);
    }

    private static void QueueRamConvert()
    {
        if (_ramTask is { IsCompleted: false }) return;
        if (++_ramFrame < 6) return;
        _ramFrame = 0;
        var psMem = Runtime.Mem as Memory.PSMemory;
        if (psMem == null) return;
        var ram = psMem.RamBuffer;
        var back = _ramBack;
        _ramTask = Task.Run(() => Runtime.RamLog.BuildTexture(ram, back))
            .ContinueWith(_ =>
            {
                (_ramFront, _ramBack) = (_ramBack, _ramFront);
                _ramReady = true;
            }, TaskContinuationOptions.ExecuteSynchronously);
    }

    private static void FlushRamTexture(GL gl)
    {
        _ramReady = false;
        gl.BindTexture(TextureTarget.Texture2D, _ramTex);
        gl.TexImage2D<byte>(TextureTarget.Texture2D, 0, InternalFormat.Rgba,
            Memory.RamLogger.Width, Memory.RamLogger.Height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, _ramFront);
        RamMapPanel.SetTexture(_ramTex);
    }

    private static void ConvertDisplay(Gpu gpu, int w, int h)
    {
        var vram = gpu.Vram;
        int dx = gpu.DisplayX, dy = gpu.DisplayY;
        var o = 0;
        if (gpu.Display24Bit)
            for (var y = 0; y < h; y++)
            {
                var lineByte = ((dy + y) * Gpu.VramWidth + dx) * 2;
                for (var x = 0; x < w; x++)
                {
                    var bo = lineByte + x * 3;
                    _rgbDisplay[o++] = VramByte(vram, bo);
                    _rgbDisplay[o++] = VramByte(vram, bo + 1);
                    _rgbDisplay[o++] = VramByte(vram, bo + 2);
                }
            }
        else
            for (var y = 0; y < h; y++)
            {
                var line = ((dy + y) & (Gpu.VramHeight - 1)) * Gpu.VramWidth;
                for (var x = 0; x < w; x++)
                {
                    var px = vram[line + ((dx + x) & (Gpu.VramWidth - 1))];
                    _rgbDisplay[o++] = (byte)((px & 0x1F) << 3);
                    _rgbDisplay[o++] = (byte)(((px >> 5) & 0x1F) << 3);
                    _rgbDisplay[o++] = (byte)(((px >> 10) & 0x1F) << 3);
                }
            }
    }

    private static void ConvertVramToBuffer(ushort[] vram, byte[] output)
    {
        var o = 0;
        for (var y = 0; y < Gpu.VramHeight; y++)
        for (var x = 0; x < Gpu.VramWidth; x++)
        {
            var px = vram[y * Gpu.VramWidth + x];
            output[o++] = (byte)((px & 0x1F) << 3);
            output[o++] = (byte)(((px >> 5) & 0x1F) << 3);
            output[o++] = (byte)(((px >> 10) & 0x1F) << 3);
        }
    }

    private static byte VramByte(ushort[] vram, int byteOffset)
    {
        var hw = (byteOffset >> 1) & (Gpu.VramWidth * Gpu.VramHeight - 1);
        var v = vram[hw];
        return (byte)((byteOffset & 1) == 0 ? v & 0xFF : v >> 8);
    }
}