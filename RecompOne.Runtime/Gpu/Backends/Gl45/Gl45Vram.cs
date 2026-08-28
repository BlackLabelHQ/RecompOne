using Silk.NET.OpenGL;

namespace RecompOne.Runtime.Hle;

public sealed class Gl45Vram : IGlVram
{
    readonly GL _gl;
    uint _tex, _fbo;
    uint _stageTex, _stageFbo;
    uint _scratchTex;

    public uint Texture => _tex;
    public uint Fbo => _fbo;

    public Gl45Vram(GL gl) => _gl = gl;

    public void Init()
    {
        _tex = CreateTex(GlVram.Width, GlVram.Height);
        _fbo = CreateFbo(_tex);
        _stageTex = CreateTex(VramShadow.Width, VramShadow.Height);
        _stageFbo = CreateFbo(_stageTex);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    uint CreateTex(int w, int h)
    {
        uint t = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, t);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.TexImage2D<ushort>(TextureTarget.Texture2D, 0, InternalFormat.Rgb5A1, (uint)w, (uint)h, 0,
            PixelFormat.Rgba, PixelType.UnsignedShort1555Rev, new ushort[w * h].AsSpan());
        return t;
    }

    uint CreateFbo(uint tex)
    {
        uint f = _gl.GenFramebuffer();
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
        _gl.TextureBarrier();
        return targetTex;
    }

    public void SampleBarrier() => _gl.TextureBarrier();

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
        _gl.BlitFramebuffer(x, y, x + w, y + h,
            x * GlVram.Scale, y * GlVram.Scale, (x + w) * GlVram.Scale, (y + h) * GlVram.Scale,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
    }

    public void Fill(int x, int y, int w, int h, ushort color15)
    {
        float r = (color15 & 0x1F) / 31f, g = ((color15 >> 5) & 0x1F) / 31f, b = ((color15 >> 10) & 0x1F) / 31f;
        float a = (color15 & 0x8000) != 0 ? 1f : 0f;
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.Enable(EnableCap.ScissorTest);
        _gl.Scissor(x * GlVram.Scale, y * GlVram.Scale, (uint)Math.Max(0, w * GlVram.Scale), (uint)Math.Max(0, h * GlVram.Scale));
        _gl.ClearColor(r, g, b, a);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _gl.Disable(EnableCap.ScissorTest);
    }

    public void CopyRect(int sx, int sy, int dx, int dy, int w, int h)
    {
        int sw = w * GlVram.Scale, sh = h * GlVram.Scale;
        bool overlap = sx < dx + w && dx < sx + w && sy < dy + h && dy < sy + h;
        if (!overlap)
        {
            _gl.CopyImageSubData(_tex, CopyImageSubDataTarget.Texture2D, 0, sx * GlVram.Scale, sy * GlVram.Scale, 0,
                _tex, CopyImageSubDataTarget.Texture2D, 0, dx * GlVram.Scale, dy * GlVram.Scale, 0, (uint)sw, (uint)sh, 1);
            return;
        }

        EnsureScratch();
        _gl.CopyImageSubData(_tex, CopyImageSubDataTarget.Texture2D, 0, sx * GlVram.Scale, sy * GlVram.Scale, 0,
            _scratchTex, CopyImageSubDataTarget.Texture2D, 0, 0, 0, 0, (uint)sw, (uint)sh, 1);
        _gl.CopyImageSubData(_scratchTex, CopyImageSubDataTarget.Texture2D, 0, 0, 0, 0,
            _tex, CopyImageSubDataTarget.Texture2D, 0, dx * GlVram.Scale, dy * GlVram.Scale, 0, (uint)sw, (uint)sh, 1);
    }

    public void ReadRect(int x, int y, int w, int h, Span<ushort> dst)
    {
        _gl.Disable(EnableCap.ScissorTest);
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _fbo);
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _stageFbo);
        _gl.BlitFramebuffer(x * GlVram.Scale, y * GlVram.Scale, (x + w) * GlVram.Scale, (y + h) * GlVram.Scale,
            x, y, x + w, y + h, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);

        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _stageFbo);
        _gl.PixelStore(PixelStoreParameter.PackAlignment, 2);
        _gl.ReadPixels(x, y, (uint)w, (uint)h, PixelFormat.Rgba, PixelType.UnsignedShort1555Rev, dst);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
    }

    void EnsureScratch()
    {
        if (_scratchTex != 0) return;
        _scratchTex = CreateTex(GlVram.Width, GlVram.Height);
    }

    public void Dispose()
    {
        if (_fbo != 0) _gl.DeleteFramebuffer(_fbo);
        if (_stageFbo != 0) _gl.DeleteFramebuffer(_stageFbo);
        if (_tex != 0) _gl.DeleteTexture(_tex);
        if (_stageTex != 0) _gl.DeleteTexture(_stageTex);
        if (_scratchTex != 0) _gl.DeleteTexture(_scratchTex);
    }
}
