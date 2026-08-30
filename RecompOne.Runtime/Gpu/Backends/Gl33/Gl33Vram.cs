using Silk.NET.OpenGL;

namespace RecompOne.Runtime.Hle;

//usess ping pong text
public sealed class Gl33Vram : IGlVram
{
    private readonly GL _gl;
    private uint _tex, _fbo;
    private uint _stageTex, _stageFbo;
    private uint _scratchFbo;
    private uint _destVramTex, _destVramFbo;
    private uint _destRtTex, _destRtFbo;
    private int _destRtW, _destRtH;

    public uint Texture => _tex;
    public uint Fbo => _fbo;

    public Gl33Vram(GL gl)
    {
        _gl = gl;
    }

    public void Init()
    {
        _tex = CreateTex(GlVram.Width, GlVram.Height);
        _fbo = CreateFbo(_tex);
        _stageTex = CreateTex(VramShadow.Width, VramShadow.Height);
        _stageFbo = CreateFbo(_stageTex);
        _destVramTex = CreateTex(GlVram.Width, GlVram.Height);
        _destVramFbo = CreateFbo(_destVramTex);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private uint CreateTex(int w, int h)
    {
        _gl.ActiveTexture(TextureUnit.Texture7);
        var t = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, t);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.TexImage2D<ushort>(TextureTarget.Texture2D, 0, InternalFormat.Rgb5A1, (uint)w, (uint)h, 0,
            PixelFormat.Rgba, PixelType.UnsignedShort1555Rev, new ushort[w * h].AsSpan());
        _gl.ActiveTexture(TextureUnit.Texture0);
        return t;
    }

    private uint CreateFbo(uint tex)
    {
        var f = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, f);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, tex, 0);
        return f;
    }

    public void BindDraw()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.Viewport(0, 0, (uint)GlVram.Width, (uint)GlVram.Height);
    }

    public uint BeginDestRead(uint targetTex, int targetW, int targetH, int x, int y, int w, int h)
    {
        var isVram = targetTex == _tex;
        uint destTex, destFbo;
        if (isVram)
        {
            destTex = _destVramTex;
            destFbo = _destVramFbo;
        }
        else
        {
            EnsureRtDest(targetW, targetH);
            destTex = _destRtTex;
            destFbo = _destRtFbo;
        }

        int x1 = Math.Min(x + w, targetW), y1 = Math.Min(y + h, targetH);
        int x0 = Math.Max(x, 0), y0 = Math.Max(y, 0);
        if (x1 <= x0 || y1 <= y0) return destTex;

        var targetFbo = FboFor(targetTex, isVram);

        _gl.Disable(EnableCap.ScissorTest);
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, targetFbo);
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, destFbo);
        _gl.BlitFramebuffer(x0, y0, x1, y1, x0, y0, x1, y1,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);

        _gl.Enable(EnableCap.ScissorTest);
        return destTex;
    }

    public void SampleBarrier()
    {
    }

    private uint FboFor(uint tex, bool isVram)
    {
        if (isVram) return _fbo;
        if (_scratchFbo == 0) _scratchFbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _scratchFbo);
        _gl.FramebufferTexture2D(FramebufferTarget.ReadFramebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, tex, 0);
        return _scratchFbo;
    }

    private void EnsureRtDest(int w, int h)
    {
        if (_destRtTex != 0 && _destRtW == w && _destRtH == h) return;
        if (_destRtFbo != 0) _gl.DeleteFramebuffer(_destRtFbo);
        if (_destRtTex != 0) _gl.DeleteTexture(_destRtTex);
        _destRtTex = CreateTex(w, h);
        _destRtFbo = CreateFbo(_destRtTex);
        _destRtW = w;
        _destRtH = h;
    }

    public void WriteRect(int x, int y, int w, int h, ReadOnlySpan<ushort> px)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.BindTexture(TextureTarget.Texture2D, _stageTex);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 2);
        _gl.TexSubImage2D(TextureTarget.Texture2D, 0, x, y, (uint)w, (uint)h,
            PixelFormat.Rgba, PixelType.UnsignedShort1555Rev, px);

        _gl.Disable(EnableCap.ScissorTest);
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _stageFbo);
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _fbo);
        _gl.BlitFramebuffer(x, y, x + w, y + h, x * GlVram.Scale, y * GlVram.Scale, (x + w) * GlVram.Scale,
            (y + h) * GlVram.Scale,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
    }

    public void Fill(int x, int y, int w, int h, ushort color15)
    {
        float r = (color15 & 0x1F) / 31f, g = ((color15 >> 5) & 0x1F) / 31f, b = ((color15 >> 10) & 0x1F) / 31f;
        var a = (color15 & 0x8000) != 0 ? 1f : 0f;
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.Enable(EnableCap.ScissorTest);
        _gl.Scissor(x * GlVram.Scale, y * GlVram.Scale, (uint)Math.Max(0, w * GlVram.Scale),
            (uint)Math.Max(0, h * GlVram.Scale));
        _gl.ClearColor(r, g, b, a);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _gl.Disable(EnableCap.ScissorTest);
    }

    public void CopyRect(int sx, int sy, int dx, int dy, int w, int h)
    {
        var s = GlVram.Scale;
        _gl.Disable(EnableCap.ScissorTest);
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _fbo);
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _destVramFbo);
        _gl.BlitFramebuffer(sx * s, sy * s, (sx + w) * s, (sy + h) * s, sx * s, sy * s, (sx + w) * s, (sy + h) * s,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _destVramFbo);
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _fbo);
        _gl.BlitFramebuffer(sx * s, sy * s, (sx + w) * s, (sy + h) * s, dx * s, dy * s, (dx + w) * s, (dy + h) * s,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
    }

    public void ReadRect(int x, int y, int w, int h, Span<ushort> dst)
    {
        var s = GlVram.Scale;
        _gl.Disable(EnableCap.ScissorTest);
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _fbo);
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _stageFbo);
        _gl.BlitFramebuffer(x * s, y * s, (x + w) * s, (y + h) * s, x, y, x + w, y + h, ClearBufferMask.ColorBufferBit,
            BlitFramebufferFilter.Nearest);

        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _stageFbo);
        _gl.PixelStore(PixelStoreParameter.PackAlignment, 2);
        _gl.ReadPixels(x, y, (uint)w, (uint)h, PixelFormat.Rgba, PixelType.UnsignedShort1555Rev, dst);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
    }

    public void Dispose()
    {
        if (_fbo != 0) _gl.DeleteFramebuffer(_fbo);
        if (_stageFbo != 0) _gl.DeleteFramebuffer(_stageFbo);
        if (_scratchFbo != 0) _gl.DeleteFramebuffer(_scratchFbo);
        if (_destVramFbo != 0) _gl.DeleteFramebuffer(_destVramFbo);
        if (_destRtFbo != 0) _gl.DeleteFramebuffer(_destRtFbo);
        if (_tex != 0) _gl.DeleteTexture(_tex);
        if (_stageTex != 0) _gl.DeleteTexture(_stageTex);
        if (_destVramTex != 0) _gl.DeleteTexture(_destVramTex);
        if (_destRtTex != 0) _gl.DeleteTexture(_destRtTex);
    }
}