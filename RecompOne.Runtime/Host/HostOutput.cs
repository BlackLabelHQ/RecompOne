using Silk.NET.OpenGL;

namespace RecompOne.Runtime.Host;

public readonly record struct OutputTexture(uint Id, int Width, int Height, float Aspect = 4f / 3f)
{
    public bool IsValid => Id != 0 && Width > 0 && Height > 0;
}

public static class HostOutput
{
    private static uint _displayTex;
    private static uint _vramTex;
    private static uint _ramTex;
    
    private static byte[] _rgbDisplay = [];
    private static byte[] _rgbVram = [];
    private static byte[] _ramFront = new byte[Memory.RamLogger.Width * Memory.RamLogger.Height * 4];
    private static byte[] _ramBack = new byte[Memory.RamLogger.Width * Memory.RamLogger.Height * 4];
    private static ushort[] _vramView = new ushort[Gpu.VramWidth * Gpu.VramHeight];
    private static Task? _ramTask;
    private static volatile bool _ramReady;
    private static int _ramFrame;
    
    public static OutputTexture Display { get; private set; }
    public static OutputTexture Vram { get; private set; }
    public static OutputTexture Ram { get; private set; }
    
    public static int ViewWidth { get; set; }
    public static int ViewHeight { get; set; }
    
    public static bool VramEnabled { get; set; }
    public static bool RamEnabled { get; set; }
    
    public static bool MemoryTracking
    {
        get => Memory.RamLogger.TrackReads;
        set
        {
            Memory.RamLogger.TrackReads = value;
            Memory.RamLogger.TrackWrites = value;
        }
    }
    
    internal static void Load(GL gl)
    {
        _displayTex = HostWindow.CreateTexture(gl);
        _vramTex = HostWindow.CreateTexture(gl);
        _ramTex = HostWindow.CreateTexture(gl);
    }
    
    internal static void Unload(GL gl)
    {
        gl.DeleteTexture(_displayTex);
        gl.DeleteTexture(_vramTex);
        gl.DeleteTexture(_ramTex);
        Display = Vram = Ram = default;
    }
    
    internal static void Update(GL gl, Gpu? gpu, Hle.GlCore? backend, int windowWidth, int windowHeight)
    {
        if (gpu != null)
        {
            var display = new Interp.DisplayState(gpu.DisplayX, gpu.DisplayY, gpu.DisplayWidth, gpu.DisplayHeight,
                gpu.Display24Bit, gpu.DisplayEnabled);
            var interp = Interp.Interp.Backend;
            var recorded = default(Interp.DisplayState);
            var shown = interp != null && interp.TryShownDisplay(out recorded) &&
                        (recorded.Width != display.Width || recorded.Height != display.Height ||
                         recorded.Rgb24 != display.Rgb24 || recorded.Enabled != display.Enabled);
            if (shown) display = recorded;
            
            if (Hle.GpuHle.Active && backend is { Ready: true } && display.Enabled)
            {
                var dispX = display.X;
                var dispY = display.Y;
                if (!shown) interp?.MapDisplay(ref dispX, ref dispY, display.Width, display.Height);
                var (tex, tw, th, aspect) = backend.PresentDisplay(
                    dispX, dispY,
                    display.Width, display.Height,
                    display.Rgb24,
                    ViewWidth, ViewHeight);
                if (tex != 0) Display = new OutputTexture(tex, tw, th, aspect > 0f ? aspect : 4f / 3f);
                gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                gl.Viewport(0, 0, (uint)windowWidth, (uint)windowHeight);
            }
            else
            {
                UploadDisplayTexture(gl, gpu);
            }
            
            if (VramEnabled) UploadVramTexture(gl, gpu, backend);
        }
        
        if (RamEnabled)
        {
            QueueRamConvert();
            if (_ramReady) FlushRamTexture(gl);
        }
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
        Display = new OutputTexture(_displayTex, w, h);
    }

    private static void UploadVramTexture(GL gl, Gpu gpu, Hle.GlCore? backend)
    {
        const int sz = Gpu.VramWidth * Gpu.VramHeight * 3;
        if (_rgbVram.Length < sz) _rgbVram = new byte[sz];
        ushort[] src;
        if (Hle.GpuHle.Active && backend is { Ready: true })
        {
            backend.ReadVram(0, 0, Gpu.VramWidth, Gpu.VramHeight, _vramView);
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
        Vram = new OutputTexture(_vramTex, Gpu.VramWidth, Gpu.VramHeight);
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
        _ramTask = Task.Run(() => Runtime.RamLog.BuildTexture(ram.Span, back))
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
        Ram = new OutputTexture(_ramTex, Memory.RamLogger.Width, Memory.RamLogger.Height);
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
