using Silk.NET.OpenGL;

namespace RecompOne.Runtime.Hle;

//gl21 has no blitframebuffer so every copy goes tru a textured quad, i hate this
public sealed class Gl21Vram : IGlVram
{
    private readonly GL _gl;
    private uint _tex, _fbo;
    private uint _stageTex;
    private uint _destVramTex, _destVramFbo;
    private uint _destRtTex, _destRtFbo;
    private int _destRtW, _destRtH;
    private uint _scratchTex, _scratchFbo;

    private uint _blitProg, _blitVao, _blitVbo;
    private int _uDstRect, _uSrcRect;
    private bool _hasVao;

    private byte[] _readBuf = [];

    public uint Texture => _tex;
    public uint Fbo => _fbo;

    public Gl21Vram(GL gl)
    {
        _gl = gl;
    }

    public void Init()
    {
        _tex = CreateTex(GlVram.Width, GlVram.Height);
        _fbo = CreateFbo(_tex);
        _stageTex = CreateTex(VramShadow.Width, VramShadow.Height);
        _destVramTex = CreateTex(GlVram.Width, GlVram.Height);
        _destVramFbo = CreateFbo(_destVramTex);
        _scratchTex = CreateTex(GlVram.Width, GlVram.Height);
        _scratchFbo = CreateFbo(_scratchTex);
        InitBlit();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private unsafe void InitBlit()
    {
        _blitProg = GlShaders.Build(_gl, GlShaders.BlitVs120, GlShaders.BlitFs120, "blit21", [(0u, "aPos")]);
        _uDstRect = _gl.GetUniformLocation(_blitProg, "uDstRect");
        _uSrcRect = _gl.GetUniformLocation(_blitProg, "uSrcRect");
        _gl.UseProgram(_blitProg);
        _gl.Uniform1(_gl.GetUniformLocation(_blitProg, "uSrc"), 0);

        try
        {
            _blitVao = _gl.GenVertexArray();
            _gl.BindVertexArray(_blitVao);
            _hasVao = true;
        }
        catch
        {
            _hasVao = false;
        }

        _blitVbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _blitVbo);
        float[] quad = { -1f, -1f, 1f, -1f, -1f, 1f, 1f, 1f };
        fixed (float* q = quad)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(quad.Length * sizeof(float)), q,
                BufferUsageARB.StaticDraw);
        }

        if (_hasVao)
        {
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), (void*)0);
            _gl.BindVertexArray(0);
        }

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
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

    private unsafe void BlitQuad(uint srcTex, int srcW, int srcH, uint dstFbo, int dstW, int dstH, int sx, int sy,
        int dx, int dy, int w, int h)
    {
        if (w <= 0 || h <= 0) return;

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, dstFbo);
        _gl.Viewport(0, 0, (uint)dstW, (uint)dstH);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.DepthTest);

        _gl.UseProgram(_blitProg);
        _gl.Uniform4(_uSrcRect, sx / (float)srcW, sy / (float)srcH, w / (float)srcW, h / (float)srcH);
        _gl.Uniform4(_uDstRect, dx / (float)dstW, dy / (float)dstH, w / (float)dstW, h / (float)dstH);

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, srcTex);

        if (_hasVao)
        {
            _gl.BindVertexArray(_blitVao);
        }
        else
        {
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _blitVbo);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), (void*)0);
        }

        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        if (_hasVao) _gl.BindVertexArray(0);
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

        BlitQuad(targetTex, targetW, targetH, destFbo, targetW, targetH,
            x0, y0, x0, y0, x1 - x0, y1 - y0);
        _gl.Enable(EnableCap.ScissorTest);
        return destTex;
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

    public void SampleBarrier()
    {
    }

    public void WriteRect(int x, int y, int w, int h, ReadOnlySpan<ushort> px)
    {
        _gl.ActiveTexture(TextureUnit.Texture7);
        _gl.BindTexture(TextureTarget.Texture2D, _stageTex);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 2);
        _gl.TexSubImage2D(TextureTarget.Texture2D, 0, x, y, (uint)w, (uint)h,
            PixelFormat.Rgba, PixelType.UnsignedShort1555Rev, px);
        _gl.ActiveTexture(TextureUnit.Texture0);

        var s = GlVram.Scale;
        BlitScaled(_stageTex, VramShadow.Width, VramShadow.Height, _fbo, GlVram.Width, GlVram.Height,
            x, y, w, h, x * s, y * s, w * s, h * s);
    }

    private unsafe void BlitScaled(uint srcTex, int srcW, int srcH, uint dstFbo, int dstW, int dstH,
        int sx, int sy, int sw, int sh, int dx, int dy, int dw, int dh)
    {
        if (sw <= 0 || sh <= 0 || dw <= 0 || dh <= 0) return;

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, dstFbo);
        _gl.Viewport(0, 0, (uint)dstW, (uint)dstH);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.DepthTest);

        _gl.UseProgram(_blitProg);
        _gl.Uniform4(_uSrcRect, sx / (float)srcW, sy / (float)srcH, sw / (float)srcW, sh / (float)srcH);
        _gl.Uniform4(_uDstRect, dx / (float)dstW, dy / (float)dstH, dw / (float)dstW, dh / (float)dstH);

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, srcTex);

        if (_hasVao)
        {
            _gl.BindVertexArray(_blitVao);
        }
        else
        {
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _blitVbo);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), (void*)0);
        }

        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        if (_hasVao) _gl.BindVertexArray(0);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
    }

    public void Fill(int x, int y, int w, int h, ushort color15)
    {
        float r = (color15 & 0x1F) / 31f, g = ((color15 >> 5) & 0x1F) / 31f, b = ((color15 >> 10) & 0x1F) / 31f;
        var a = (color15 & 0x8000) != 0 ? 1f : 0f;
        var s = GlVram.Scale;
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.Enable(EnableCap.ScissorTest);
        _gl.Scissor(x * s, y * s, (uint)Math.Max(0, w * s), (uint)Math.Max(0, h * s));
        _gl.ClearColor(r, g, b, a);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _gl.Disable(EnableCap.ScissorTest);
    }

    public void CopyRect(int sx, int sy, int dx, int dy, int w, int h)
    {
        var s = GlVram.Scale;
        BlitQuad(_tex, GlVram.Width, GlVram.Height, _scratchFbo, GlVram.Width, GlVram.Height,
            sx * s, sy * s, sx * s, sy * s, w * s, h * s);
        BlitQuad(_scratchTex, GlVram.Width, GlVram.Height, _fbo, GlVram.Width, GlVram.Height,
            sx * s, sy * s, dx * s, dy * s, w * s, h * s);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
    }

    public void ReadRect(int x, int y, int w, int h, Span<ushort> dst)
    {
        if (w <= 0 || h <= 0) return;

        var s = GlVram.Scale;
        var rowBytes = w * s * 4;
        if (_readBuf.Length < rowBytes) _readBuf = new byte[rowBytes];

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.PixelStore(PixelStoreParameter.PackAlignment, 1);

        for (var row = 0; row < h; row++)
        {
            _gl.ReadPixels(x * s, (y + row) * s, (uint)(w * s), 1, PixelFormat.Rgba, PixelType.UnsignedByte,
                _readBuf.AsSpan(0, rowBytes));

            for (var col = 0; col < w; col++)
            {
                var o = col * s * 4;
                int r5 = _readBuf[o] >> 3, g5 = _readBuf[o + 1] >> 3, b5 = _readBuf[o + 2] >> 3;
                var a = _readBuf[o + 3] >= 128 ? 1 : 0;
                dst[row * w + col] = (ushort)(r5 | (g5 << 5) | (b5 << 10) | (a << 15));
            }
        }
    }

    public void Dispose()
    {
        if (_fbo != 0) _gl.DeleteFramebuffer(_fbo);
        if (_destVramFbo != 0) _gl.DeleteFramebuffer(_destVramFbo);
        if (_destRtFbo != 0) _gl.DeleteFramebuffer(_destRtFbo);
        if (_scratchFbo != 0) _gl.DeleteFramebuffer(_scratchFbo);
        if (_tex != 0) _gl.DeleteTexture(_tex);
        if (_stageTex != 0) _gl.DeleteTexture(_stageTex);
        if (_destVramTex != 0) _gl.DeleteTexture(_destVramTex);
        if (_destRtTex != 0) _gl.DeleteTexture(_destRtTex);
        if (_scratchTex != 0) _gl.DeleteTexture(_scratchTex);
        if (_blitProg != 0) _gl.DeleteProgram(_blitProg);
        if (_blitVbo != 0) _gl.DeleteBuffer(_blitVbo);
        if (_hasVao && _blitVao != 0) _gl.DeleteVertexArray(_blitVao);
    }
}