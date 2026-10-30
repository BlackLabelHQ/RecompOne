using Silk.NET.OpenGL;

namespace RecompOne.Runtime.Hle;

public sealed class GlDisplayRt
{
    public int X, Y, W, H;
    public int Margin;
    public uint Tex, Fbo;
    public uint UiTex, UiFbo;
    private long _uiScene = -1;
    private uint _depth;
    private long _depthScene = -1;
    public long LastScene = -2;
    public long GeometryScene = -2;

    public uint DepthTex => _depth;
    public bool DepthCurrent => _depth != 0 && _depthScene == LastScene;
    public bool UiCurrent => UiTex != 0 && _uiScene == LastScene;
    public bool HasUi(long scene) => UiTex != 0 && _uiScene == scene;
    private uint _uiDepth;
    public bool Dirty;
    public long Stamp;
    public long LastDrawFrame;
    public long LastMarginFrame = -1;

    public int Wide1x => W + Margin * 2;
    public int TexW => Wide1x * GlVram.Scale;
    public int TexH => H * GlVram.Scale;

    public bool Contains(int cx0, int cy0, int cx1, int cy1)
    {
        return cx0 >= X && cx1 <= X + W - 1 && cy0 >= Y && cy1 <= Y + H - 1;
    }

    public bool Covers(int cx0, int cy0, int cx1, int cy1)
    {
        return cx0 <= X && cx1 >= X + W - 1 && cy0 <= Y && cy1 >= Y + H - 1;
    }

    public bool Intersects(int rx, int ry, int rw, int rh)
    {
        return rx < X + W && X < rx + rw && ry < Y + H && Y < ry + rh;
    }

    public void Create(GL gl)
    {
        Tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, Tex);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        gl.TexImage2D<ushort>(TextureTarget.Texture2D, 0, GlVram.Storage, (uint)TexW, (uint)TexH, 0,
            PixelFormat.Rgba, PixelType.UnsignedShort1555Rev, new ushort[TexW * TexH].AsSpan());

        Fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, Fbo);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, Tex, 0);
        gl.ClearColor(0f, 0f, 0f, 0f);
        gl.Disable(EnableCap.ScissorTest);
        gl.Clear(ClearBufferMask.ColorBufferBit);
    }

    public unsafe void PrepareDepth(GL gl, long scene)
    {
        if (_depth == 0)
        {
            gl.GetInteger(GetPName.TextureBinding2D, out var previous);
            _depth = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, _depth);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Depth24Stencil8, (uint)TexW, (uint)TexH, 0,
                PixelFormat.DepthStencil, PixelType.UnsignedInt248, (void*)0);
            gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment,
                TextureTarget.Texture2D, _depth, 0);
            gl.BindTexture(TextureTarget.Texture2D, (uint)previous);
            _depthScene = -1;
        }
        if (_depthScene == scene) return;
        gl.Disable(EnableCap.ScissorTest);
        gl.DepthMask(true);
        gl.StencilMask(0xFF);
        gl.ClearDepth(1.0);
        gl.ClearStencil(0);
        gl.Clear(ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
        _depthScene = scene;
    }
    
    public void InvalidateDepth() => _depthScene = -1;

    public unsafe void PrepareUi(GL gl, long scene)
    {
        if (UiTex == 0)
        {
            gl.GetInteger(GetPName.TextureBinding2D, out var previous);
            UiTex = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, UiTex);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f, (uint)TexW, (uint)TexH, 0,
                PixelFormat.Rgba, PixelType.HalfFloat, (void*)0);
            gl.BindTexture(TextureTarget.Texture2D, (uint)previous);
            UiFbo = gl.GenFramebuffer();
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, UiFbo);
            gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, UiTex, 0);
            _uiScene = -1;
        }
        else
        {
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, UiFbo);
        }

        if (_depth != 0 && _uiDepth != _depth)
        {
            gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment,
                TextureTarget.Texture2D, _depth, 0);
            _uiDepth = _depth;
        }

        if (_uiScene == scene) return;
        gl.Disable(EnableCap.ScissorTest);
        gl.ColorMask(true, true, true, true);
        gl.ClearColor(0f, 0f, 0f, 1f);
        gl.Clear(ClearBufferMask.ColorBufferBit);
        _uiScene = scene;
    }
    
    public void Destroy(GL gl)
    {
        if (_depth != 0) gl.DeleteTexture(_depth);
        _depth = 0;
        _depthScene = -1;
        if (Fbo != 0) gl.DeleteFramebuffer(Fbo);
        if (Tex != 0) gl.DeleteTexture(Tex);
        if (UiFbo != 0) gl.DeleteFramebuffer(UiFbo);
        if (UiTex != 0) gl.DeleteTexture(UiTex);
        Fbo = Tex = UiFbo = UiTex = 0;
        _uiScene = -1;
        _uiDepth = 0;
    }
}