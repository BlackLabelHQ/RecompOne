using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace RecompOne.Runtime.Hle;

public sealed class GlCore : IGpuBackend
{
    [StructLayout(LayoutKind.Sequential)]
    private struct GlVertex
    {
        public float X, Y;
        public float R, G, B;
        public float Clut, Texpage;
        public float U, V;
        public float W;
    }

    private const int MaxVerts = 0x40000;

    private readonly GL _gl;
    private readonly IGlVram _vram;
    private readonly Dictionary<int, uint> _images = [];
    private readonly System.Collections.Concurrent.ConcurrentQueue<(int Id, byte[] Pixels, int Width, int Height)> _pendingImages = new();
    private int _imageId;
    private readonly HashSet<int> _released = [];
    private readonly System.Collections.Concurrent.ConcurrentQueue<int> _pendingReleases = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, CapturedImage> _captures = new();
    private byte[] _captureBuffer = [];
    private readonly GlDisplayRt?[] _rts = new GlDisplayRt?[2];
    private long _rtStamp;
    private long _frame;
    private long _depthScene;
    private bool _kDepth;
    private byte _kMaterial;
    private bool _drewSincePresent;
    private float _surfaceArea;
    private Vector4 _surfaceSum;
    private uint _progPresentMaterial, _materialPresentTex, _materialPresentFbo;
    private int _materialPresentW, _materialPresentH;
    private int _uMaterialOrigin, _uMaterialSize, _uMaterialTexSize;
    private bool _stencilTexturing;
    private const int DepthStencilTextureMode = 0x90EA;
    private bool _kStamp;
    private bool _kLayer;
    private uint _progPresentLayer, _layerPresentTex, _layerPresentFbo;
    private int _layerPresentW, _layerPresentH;
    private int _uLayerOrigin, _uLayerSize, _uLayerTexSize;
    private int _uDepthEnabled;
    private int _uSurface;
    private int _uLayerReset;
    private uint _progPresentDepth, _depthPresentTex, _depthPresentFbo;
    private int _depthPresentW, _depthPresentH;
    private int _uDepthOrigin, _uDepthSize, _uDepthTexSize;
    
    public void AdvanceFrame()
    {
        BeginScene();
        _frame++;
    }


    public void BeginScene()
    {
        Flush();
        _depthScene++;
    }

    private uint _vao, _vbo, _presentVao, _presentVbo, _progPrim, _progPresent, _progPresent24;
    private uint _presentFbo, _presentTex;
    private int _presentW, _presentH;
    private bool _presentNearest;

    private SlangChain? _postChain;

    private readonly GlVertex[] _verts = new GlVertex[MaxVerts];
    private int _count;
    private float _drawMinX, _drawMinY, _drawMaxX, _drawMaxY;

    private HleDrawEnv _env;

    private GlDisplayRt? _kTarget;
    private bool _kSamplesVram;
    private int _kTexX0, _kTexY0, _kTexX1, _kTexY1;
    private bool _vramDirty;
    private int _vdX0, _vdY0, _vdX1, _vdY1;
    private bool _kTransparent;
    private int _kImage = -1;
    private bool _kQuantizedAlpha;
    private int _kBlend, _kSetMask, _kCheckMask;
    private int _kTwAndX, _kTwAndY, _kTwOrX, _kTwOrY;
    private int _kClipX0, _kClipY0, _kClipX1, _kClipY1;
    private uint _kRepTex, _kRepClut;
    private float _kRepX, _kRepY, _kRepW, _kRepH;
    private int _kRepClutCount;
    private int _uTexWindow, _uBlend, _uBlendOpaque, _uSetMask, _uCheckMask, _uPosBias, _uFbInv, _uTrueColor;
    private int _uRepRect, _uRepClutCount;
    private int _uPresentOrigin, _uPresentSize, _uPresentTexSize, _uPresent24Origin, _uPresent24Size;

    public bool Ready { get; private set; }

    private readonly bool _legacy;
    private int _uVramSize, _uDestSize, _uSemiTrans, _uBlendMode;

    public GlCore(GL gl, IGlVram vram, bool legacy = false)
    {
        _gl = gl;
        _vram = vram;
        _legacy = legacy;
    }

    public unsafe void InitGl()
    {
        _vram.Init();

        var primVs = _legacy ? GlShaders.PrimVs120 : GlShaders.PrimVs;
        var primFs = _legacy ? GlShaders.PrimFs120 : GlShaders.PrimFs;
        var fullVs = _legacy ? GlShaders.FullscreenVs120 : GlShaders.FullscreenVs;
        var presentFs = _legacy ? GlShaders.PresentFs120 : GlShaders.PresentFs;
        var present24Fs = _legacy ? GlShaders.Present24Fs120 : GlShaders.Present24Fs;

        _progPrim = GlShaders.BuildPrim(_gl, primVs, primFs, "prim");
        _progPresent = GlShaders.BuildFullscreen(_gl, fullVs, presentFs, "present");
        _progPresent24 = GlShaders.BuildFullscreen(_gl, fullVs, present24Fs, "present24");
        if (_progPrim == 0 || _progPresent == 0 || _progPresent24 == 0) return;

        _uVramSize = _gl.GetUniformLocation(_progPrim, "uVramSize");
        _uDestSize = _gl.GetUniformLocation(_progPrim, "uDestSize");
        _uSemiTrans = _gl.GetUniformLocation(_progPrim, "uSemiTrans");
        _uBlendMode = _gl.GetUniformLocation(_progPrim, "uBlendMode");

        _uTexWindow = _gl.GetUniformLocation(_progPrim, "uTexWindow");
        _uBlend = _gl.GetUniformLocation(_progPrim, "uBlend");
        _uBlendOpaque = _gl.GetUniformLocation(_progPrim, "uBlendOpaque");
        _uSetMask = _gl.GetUniformLocation(_progPrim, "uSetMask");
        _uTrueColor = _gl.GetUniformLocation(_progPrim, "uTrueColor");
        _uCheckMask = _gl.GetUniformLocation(_progPrim, "uCheckMask");
        _uPosBias = _gl.GetUniformLocation(_progPrim, "uPosBias");
        _uDepthEnabled = _gl.GetUniformLocation(_progPrim, "uDepthEnabled");
        _uSurface = _gl.GetUniformLocation(_progPrim, "uSurface");
        _uLayerReset = _gl.GetUniformLocation(_progPrim, "uLayerReset");
        _uFbInv = _gl.GetUniformLocation(_progPrim, "uFbInv");
        _uRepRect = _gl.GetUniformLocation(_progPrim, "uRepRect");
        _uRepClutCount = _gl.GetUniformLocation(_progPrim, "uRepClutCount");

        _gl.UseProgram(_progPrim);
        _gl.Uniform1(_gl.GetUniformLocation(_progPrim, "uVram"), 0);
        _gl.Uniform1(_gl.GetUniformLocation(_progPrim, "uDest"), 1);
        _gl.Uniform1(_gl.GetUniformLocation(_progPrim, "uExtTex"), 2);
        _gl.Uniform1(_gl.GetUniformLocation(_progPrim, "uRepTex"), 3);
        _gl.Uniform1(_gl.GetUniformLocation(_progPrim, "uRepClut"), 4);
        SetScaleUniform(_progPrim);
        if (_uVramSize >= 0) _gl.Uniform2(_uVramSize, (float)GlVram.Width, GlVram.Height);

        if (!_legacy)
        {
            _progPresentDepth = GlShaders.BuildFullscreen(_gl, fullVs, GlShaders.PresentDepthFs, "present-depth");
            _progPresentLayer = GlShaders.BuildFullscreen(_gl, fullVs, GlShaders.PresentLayerFs, "present-layer");
            _uLayerOrigin = _gl.GetUniformLocation(_progPresentLayer, "uOrigin");
            _uLayerSize = _gl.GetUniformLocation(_progPresentLayer, "uSize");
            _uLayerTexSize = _gl.GetUniformLocation(_progPresentLayer, "uTexSize");
            _uDepthOrigin = _gl.GetUniformLocation(_progPresentDepth, "uOrigin");
            _uDepthSize = _gl.GetUniformLocation(_progPresentDepth, "uSize");
            _uDepthTexSize = _gl.GetUniformLocation(_progPresentDepth, "uTexSize");

            var version = _gl.GetInteger(GetPName.MajorVersion) * 10 + _gl.GetInteger(GetPName.MinorVersion);
            _stencilTexturing = version >= 43;
            if (_stencilTexturing)
            {
                _progPresentMaterial = GlShaders.BuildFullscreen(_gl, fullVs, GlShaders.PresentMaterialFs, "present-material");
                _uMaterialOrigin = _gl.GetUniformLocation(_progPresentMaterial, "uOrigin");
                _uMaterialSize = _gl.GetUniformLocation(_progPresentMaterial, "uSize");
                _uMaterialTexSize = _gl.GetUniformLocation(_progPresentMaterial, "uTexSize");
            }
        }

        _uPresentOrigin = _gl.GetUniformLocation(_progPresent, "uOrigin");
        _uPresentSize = _gl.GetUniformLocation(_progPresent, "uSize");
        _uPresentTexSize = _gl.GetUniformLocation(_progPresent, "uTexSize");
        _gl.UseProgram(_progPresent);
        _gl.Uniform1(_gl.GetUniformLocation(_progPresent, "uVram"), 0);

        _uPresent24Origin = _gl.GetUniformLocation(_progPresent24, "uOrigin");
        _uPresent24Size = _gl.GetUniformLocation(_progPresent24, "uSize");
        _gl.UseProgram(_progPresent24);
        _gl.Uniform1(_gl.GetUniformLocation(_progPresent24, "uVram"), 0);
        SetScaleUniform(_progPresent24);
        var uVramSize24 = _gl.GetUniformLocation(_progPresent24, "uVramSize");
        if (uVramSize24 >= 0) _gl.Uniform2(uVramSize24, (float)GlVram.Width, GlVram.Height);

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(MaxVerts * sizeof(GlVertex)), null,
            BufferUsageARB.DynamicDraw);
        var stride = (uint)sizeof(GlVertex);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)8);
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, stride, (void*)20);
        _gl.EnableVertexAttribArray(3);
        _gl.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, stride, (void*)24);
        _gl.EnableVertexAttribArray(4);
        _gl.VertexAttribPointer(4, 2, VertexAttribPointerType.Float, false, stride, (void*)28);
        _gl.EnableVertexAttribArray(5);
        _gl.VertexAttribPointer(5, 1, VertexAttribPointerType.Float, false, stride, (void*)36);

        // fullscreen quad for present, real vbo since gl_VertexID without arrays does not draw on mesa for some reason?? or i did it wrong?
        _presentVao = _gl.GenVertexArray();
        _presentVbo = _gl.GenBuffer();
        _gl.BindVertexArray(_presentVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _presentVbo);
        float[] quad = { -1f, -1f, 1f, -1f, -1f, 1f, 1f, 1f };
        fixed (float* qp = quad)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(quad.Length * sizeof(float)), qp,
                BufferUsageARB.StaticDraw);
        }

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), (void*)0);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

        _presentTex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _presentTex);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _presentFbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _presentFbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _presentTex, 0);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        _kClipX1 = 1023;
        _kClipY1 = 511;
        Ready = true;
    }

    public void SetDrawEnv(in HleDrawEnv env)
    {
        _env = env;
    }

    private const int FbSlackW = 64;
    private const int FbSlackH = 32;

    private int _clsClipX0 = int.MinValue, _clsClipY0, _clsClipX1, _clsClipY1;
    private long _clsVersion = -1;
    private GlDisplayRt? _clsResult;

    private void InvalidateClassify()
    {
        _clsClipX0 = int.MinValue;
    }

    private GlDisplayRt? Classify()
    {
        if (_clsClipX0 == _env.ClipX0 && _clsClipY0 == _env.ClipY0 && _clsClipX1 == _env.ClipX1 &&
            _clsClipY1 == _env.ClipY1 && _clsVersion == GpuHle.ViewVersion)
        {
            if (_clsResult != null) _clsResult.Stamp = ++_rtStamp;
            return _clsResult;
        }

        _clsClipX0 = _env.ClipX0;
        _clsClipY0 = _env.ClipY0;
        _clsClipX1 = _env.ClipX1;
        _clsClipY1 = _env.ClipY1;
        _clsVersion = GpuHle.ViewVersion;
        _clsResult = ClassifySlow();
        return _clsResult;
    }

    private GlDisplayRt? ClassifySlow()
    {
        int clipX = _env.ClipX0, clipY = _env.ClipY0;
        int clipW = _env.ClipX1 - _env.ClipX0 + 1, clipH = _env.ClipY1 - _env.ClipY0 + 1;
        if (clipW <= 0 || clipH <= 0) return null;

        long bestStamp = -1;
        int fbX = 0, fbY = 0, fbW = 0, fbH = 0;
        for (var i = 0; i < GpuHle.RectCount; i++)
        {
            var r = GpuHle.GetRect(i);
            if (!r.Valid || r.W <= 0 || r.H <= 0 || r.Stamp <= bestStamp) continue;

            var clipInside = clipX >= r.X && clipX + clipW <= r.X + r.W && clipY >= r.Y && clipY + clipH <= r.Y + r.H;
            var clipIsFb = clipX <= r.X && clipX + clipW >= r.X + r.W && clipY <= r.Y && clipY + clipH >= r.Y + r.H &&
                           clipW - r.W <= FbSlackW && clipH - r.H <= FbSlackH;
            if (clipInside)
            {
                bestStamp = r.Stamp;
                fbX = r.X;
                fbY = r.Y;
                fbW = r.W;
                fbH = r.H;
            }
            else if (clipIsFb)
            {
                bestStamp = r.Stamp;
                fbX = clipX;
                fbY = clipY;
                fbW = clipW;
                fbH = clipH;
            }
        }

        return bestStamp < 0 ? null : GetOrCreateRt(fbX, fbY, fbW, fbH);
    }

    private GlDisplayRt GetOrCreateRt(int fbX, int fbY, int fbW, int fbH)
    {
        var slot = -1;
        for (var i = 0; i < _rts.Length; i++)
            if (_rts[i] is { } rt && rt.X == fbX && rt.Y == fbY)
            {
                var sameW = rt.W == fbW;
                var fitsH = rt.H >= fbH && rt.H - fbH <= FbSlackH;
                if (sameW && fitsH && rt.Margin == GpuHle.WideMargin(rt.W))
                {
                    rt.Stamp = ++_rtStamp;
                    return rt;
                }

                slot = i;
                break;
            }

        if (slot < 0)
        {
            slot = 0;
            for (var i = 1; i < _rts.Length; i++)
            {
                if (_rts[i] == null)
                {
                    slot = i;
                    break;
                }

                if (_rts[slot] != null && _rts[i]!.Stamp < _rts[slot]!.Stamp) slot = i;
            }
        }

        if (_rts[slot] is { } old)
        {
            if (old.Dirty) Writeback(old);
            old.Destroy(_gl);
            InvalidateClassify();
        }

        var fresh = new GlDisplayRt
        {
            X = fbX, Y = fbY, W = fbW, H = fbH, Margin = GpuHle.WideMargin(fbW), Stamp = ++_rtStamp,
            LastDrawFrame = _frame
        };
        fresh.Create(_gl);
        _rts[slot] = fresh;
        SyncRtFromVram(fresh, fbX, fbY, fbW, fbH);
        return fresh;
    }

    private void Writeback(GlDisplayRt rt)
    {
        var s = GlVram.Scale;
        _gl.Disable(EnableCap.ScissorTest);
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, rt.Fbo);
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _vram.Fbo);
        _gl.BlitFramebuffer(rt.Margin * s, 0, (rt.Margin + rt.W) * s, rt.H * s,
            rt.X * s, rt.Y * s, (rt.X + rt.W) * s, (rt.Y + rt.H) * s,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        rt.Dirty = false;
        Assets.Textures.VramTracker.MarkGpuWrite(rt.X, rt.Y, rt.W, rt.H);
    }

    private void SyncRtFromVram(GlDisplayRt rt, int rx, int ry, int rw, int rh)
    {
        int x0 = Math.Max(rx, rt.X), y0 = Math.Max(ry, rt.Y);
        int x1 = Math.Min(rx + rw, rt.X + rt.W), y1 = Math.Min(ry + rh, rt.Y + rt.H);
        if (x0 >= x1 || y0 >= y1) return;
        var s = GlVram.Scale;
        _gl.Disable(EnableCap.ScissorTest);
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _vram.Fbo);
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, rt.Fbo);
        _gl.BlitFramebuffer(x0 * s, y0 * s, x1 * s, y1 * s,
            (x0 - rt.X + rt.Margin) * s, (y0 - rt.Y) * s, (x1 - rt.X + rt.Margin) * s, (y1 - rt.Y) * s,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private void WritebackDirtyIntersecting(int x, int y, int w, int h)
    {
        foreach (var rt in _rts)
            if (rt is { Dirty: true } && rt.Intersects(x, y, w, h))
                Writeback(rt);
    }

    private void SyncRtsFromVram(int x, int y, int w, int h)
    {
        foreach (var rt in _rts)
            if (rt != null && rt.Intersects(x, y, w, h))
                SyncRtFromVram(rt, x, y, w, h);
    }

    private void CheckTextureFeedback(in PrimFlags f)
    {
        if (!f.Textured || f.UseImage) return;
        var px = (f.TPage & 0xF) * 64;
        var py = ((f.TPage >> 4) & 1) * 256;
        var depth = (f.TPage >> 7) & 3;
        var pw = depth == 0 ? 64 : depth == 1 ? 128 : 256;
        foreach (var rt in _rts)
            if (rt is { Dirty: true } && rt.Intersects(px, py, pw, 256))
            {
                Flush();
                Writeback(rt);
            }
    }

    private bool DesiredMatches(bool transparent, int blend, int image)
    {
        int twAndX = ~(_env.TwMaskX * 8) & 0xFF, twAndY = ~(_env.TwMaskY * 8) & 0xFF;
        int twOrX = (_env.TwOffX & _env.TwMaskX) * 8, twOrY = (_env.TwOffY & _env.TwMaskY) * 8;
        return _kRepTex == _pendingRepTex && _kRepClut == _pendingRepClut
                                          && (_pendingRepTex == 0 || (_kRepX == _pendingRepX && _kRepY == _pendingRepY
                                              && _kRepW == _pendingRepW && _kRepH == _pendingRepH))
                                          && _kTransparent == transparent && _kBlend == blend && _kImage == image
                                          && _kSetMask == (_env.SetMask ? 1 : 0) &&
                                          _kCheckMask == (_env.CheckMask ? 1 : 0)
                                          && _kTwAndX == twAndX && _kTwAndY == twAndY && _kTwOrX == twOrX &&
                                          _kTwOrY == twOrY
                                          && _kClipX0 == _env.ClipX0 && _kClipY0 == _env.ClipY0 &&
                                          _kClipX1 == _env.ClipX1 && _kClipY1 == _env.ClipY1;
    }

    private void Begin(in PrimFlags f, int vertsNeeded, bool useDepth = false, byte material = 0, bool world = false)
    {
        var transparent = f.SemiTrans;
        var blend = f.BlendMode;
        var image = f.UseImage ? f.Image : -1;
        var target = Classify();
        useDepth &= target != null;
        if (useDepth) target!.GeometryScene = _depthScene;
        var overScene = !useDepth && target != null && NativeGeometry.Enabled && NativeGeometry.DepthBuffer &&
                        target.GeometryScene == _depthScene;
        var layer = overScene && !world;
        var stamp = layer && !transparent;
        if (!useDepth) material = 0;
        if (_count > 0 && (useDepth != _kDepth || stamp != _kStamp || layer != _kLayer || material != _kMaterial || target != _kTarget || _kQuantizedAlpha || f.QuantizedAlpha || !DesiredMatches(transparent, blend, image))) Flush();
        if (_count + vertsNeeded > MaxVerts) Flush();
        CheckTextureFeedback(f);

        if (target != null) ClearMargin(target);

        _kDepth = useDepth;
        _kStamp = stamp;
        _kLayer = layer;
        _kMaterial = material;
        _kTarget = target;
        _kImage = image;
        _kQuantizedAlpha = f.QuantizedAlpha;
        _kTransparent = transparent;
        _kBlend = blend;
        _kSetMask = _env.SetMask ? 1 : 0;
        _kCheckMask = _env.CheckMask ? 1 : 0;
        _kTwAndX = ~(_env.TwMaskX * 8) & 0xFF;
        _kTwAndY = ~(_env.TwMaskY * 8) & 0xFF;
        _kTwOrX = (_env.TwOffX & _env.TwMaskX) * 8;
        _kTwOrY = (_env.TwOffY & _env.TwMaskY) * 8;
        _kClipX0 = _env.ClipX0;
        _kClipY0 = _env.ClipY0;
        _kClipX1 = _env.ClipX1;
        _kClipY1 = _env.ClipY1;
        _kRepTex = _pendingRepTex;
        _kRepClut = _pendingRepClut;
        _kRepClutCount = _pendingRepClutCount;
        _kRepX = _pendingRepX;
        _kRepY = _pendingRepY;
        _kRepW = _pendingRepW;
        _kRepH = _pendingRepH;

        if (f.Textured && !f.UseImage && _pendingRepTex == 0)
        {
            var depth = (f.TPage >> 7) & 3;
            AddTexRect((f.TPage & 0xF) * 64, ((f.TPage >> 4) & 1) * 256,
                depth == 0 ? 64 : depth == 1 ? 128 : 256, 256);
            if (depth < 2)
                AddTexRect((f.Clut & 0x3F) * 16, (f.Clut >> 6) & 0x1FF, depth == 0 ? 16 : 256, 1);
        }
    }

    private void AddTexRect(int x, int y, int w, int h)
    {
        if (!_kSamplesVram)
        {
            _kSamplesVram = true;
            _kTexX0 = x;
            _kTexY0 = y;
            _kTexX1 = x + w;
            _kTexY1 = y + h;
            return;
        }

        if (x < _kTexX0) _kTexX0 = x;
        if (y < _kTexY0) _kTexY0 = y;
        if (x + w > _kTexX1) _kTexX1 = x + w;
        if (y + h > _kTexY1) _kTexY1 = y + h;
    }

    private bool SampledRegionIsDirty()
    {
        return _vramDirty && _kTexX0 < _vdX1 && _vdX0 < _kTexX1 && _kTexY0 < _vdY1 && _vdY0 < _kTexY1;
    }

    private uint _pendingRepTex, _pendingRepClut;
    private int _pendingRepClutCount = 16;
    private float _pendingRepX, _pendingRepY, _pendingRepW = 1, _pendingRepH = 1;

    private readonly Dictionary<Assets.ReplacementTexture, uint> _repTextures = [];
    private readonly Dictionary<Assets.ReplacementClut, uint> _repCluts = [];
    private int _repGeneration = -1;

    private void ReleaseStaleReplacements()
    {
        var generation = Assets.AssetReplacerManager.Instance.Generation;
        if (generation == _repGeneration) return;
        _repGeneration = generation;
        foreach (var handle in _repTextures.Values) _gl.DeleteTexture(handle);
        foreach (var handle in _repCluts.Values) _gl.DeleteTexture(handle);
        _repTextures.Clear();
        _repCluts.Clear();
    }

    private void ResolveReplacement(in PrimFlags f, int uMin, int vMin, int uMax, int vMax, int edgeU = 0,
        int edgeV = 0)
    {
        _pendingRepTex = 0;
        _pendingRepClut = 0;

        if (!f.Textured || f.UseImage) return;
        ReleaseStaleReplacements();

        int twAndX = ~(_env.TwMaskX * 8) & 0xFF, twAndY = ~(_env.TwMaskY * 8) & 0xFF;
        int twOrX = (_env.TwOffX & _env.TwMaskX) * 8, twOrY = (_env.TwOffY & _env.TwMaskY) * 8;

        if (!Assets.Textures.TextureResolver.Resolve(f.TPage, f.Clut, uMin, vMin, uMax, vMax,
                twAndX, twAndY, twOrX, twOrY, out var res, edgeU, edgeV))
            return;

        if (res.Texture is { Mode: Assets.TextureMode.Rgba } tex)
        {
            _pendingRepTex = EnsureRepTexture(tex);
            _pendingRepX = res.Rect.U0;
            _pendingRepY = res.Rect.V0;
            _pendingRepW = res.Rect.W;
            _pendingRepH = res.Rect.H;
        }

        if (_pendingRepTex == 0 && res.Clut is { } clut && res.Rect.ClutCount > 0 && clut.Count == res.Rect.ClutCount)
        {
            _pendingRepClut = EnsureRepClut(clut);
            _pendingRepClutCount = clut.Count;
        }
    }

    private unsafe uint EnsureRepTexture(Assets.ReplacementTexture tex)
    {
        if (_repTextures.TryGetValue(tex, out var handle)) return handle;

        _gl.ActiveTexture(TextureUnit.Texture7);
        handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, handle);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
            (int)(_legacy ? GLEnum.Linear : GLEnum.LinearMipmapLinear));
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.TexImage2D<byte>(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)tex.Width, (uint)tex.Height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, tex.Rgba);
        if (!_legacy) _gl.GenerateMipmap(TextureTarget.Texture2D);
        _gl.ActiveTexture(TextureUnit.Texture0);

        _repTextures[tex] = handle;
        return handle;
    }

    private unsafe uint EnsureRepClut(Assets.ReplacementClut clut)
    {
        if (_repCluts.TryGetValue(clut, out var handle)) return handle;

        _gl.ActiveTexture(TextureUnit.Texture7);
        handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, handle);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.TexImage2D<byte>(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)clut.Count, 1, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, clut.Rgba);
        _gl.ActiveTexture(TextureUnit.Texture0);

        _repCluts[clut] = handle;
        return handle;
    }

    private bool DitherOf(in PrimFlags f)
    {
        return _env.Dither && Config.DisplayOptions.Dithering && (f.Gouraud || (f.Textured && !f.RawTexture));
    }

    private GlVertex V(in HleVertex v, in PrimFlags f, bool dither)
    {
        var raw = f.Textured && f.RawTexture;
        float cr = raw ? 128f : v.R, cg = raw ? 128f : v.G, cb = raw ? 128f : v.B;
        var tpage = f.UseImage ? (f.QuantizedAlpha ? 0x4800 : 0x4000) : f.Textured ? f.TPage & 0x1FF : 0x8000;
        if (dither && _pendingRepTex == 0) tpage |= 0x400;
        if (_pendingRepTex != 0) tpage |= 0x2000;
        else if (_pendingRepClut != 0) tpage |= 0x1000;
        if (_count == 0)
        {
            _drawMinX = _drawMaxX = v.X;
            _drawMinY = _drawMaxY = v.Y;
        }
        else
        {
            if (v.X < _drawMinX) _drawMinX = v.X;
            if (v.X > _drawMaxX) _drawMaxX = v.X;
            if (v.Y < _drawMinY) _drawMinY = v.Y;
            if (v.Y > _drawMaxY) _drawMaxY = v.Y;
        }

        return new GlVertex
        {
            X = v.X, Y = v.Y,
            R = cr, G = cg, B = cb,
            Clut = f.Clut & 0x7FFF,
            Texpage = tpage,
            U = v.U, V = v.V,
            W = v.HasGteZ && v.Z > 0f ? v.Z : 1f
        };
    }

    private static bool DepthVertex(in HleVertex v) =>
        v.Native && !v.IgnoreDepth && v.HasGteZ && float.IsFinite(v.Z) && v.Z >= 1f;

    public void DrawTri(in HleVertex a, in HleVertex b, in HleVertex c, in PrimFlags f)
    {
        ResolveReplacement(f,
            (int)Math.Min(a.U, Math.Min(b.U, c.U)), (int)Math.Min(a.V, Math.Min(b.V, c.V)),
            (int)Math.Max(a.U, Math.Max(b.U, c.U)), (int)Math.Max(a.V, Math.Max(b.V, c.V)),
            EdgeOf(a.X, a.U, b.X, b.U, c.X, c.U), EdgeOf(a.Y, a.V, b.Y, b.V, c.Y, c.V));
        var depth = NativeGeometry.Enabled && NativeGeometry.DepthBuffer &&
            DepthVertex(a) && DepthVertex(b) && DepthVertex(c);
        var material = depth && f.Textured && ShaderMaterials.Any
            ? ShaderMaterials.Lookup(f.TPage, f.Clut, (a.U + b.U + c.U) / 3f, (a.V + b.V + c.V) / 3f)
            : ShaderMaterials.None;
        Begin(f, 3, depth, material, a.HasGteZ || b.HasGteZ || c.HasGteZ);
        _drewSincePresent = true;
        if (ShaderMaterials.IsSurface(material)) TrackSurface(a, b, c);
        var dith = DitherOf(f);
        _verts[_count++] = V(a, f, dith);
        _verts[_count++] = V(b, f, dith);
        _verts[_count++] = V(c, f, dith);
    }

    private static int EdgeOf(float p0, float t0, float p1, float t1, float p2, float t2)
    {
        float dp = p1 - p0, dt = t1 - t0;
        if (MathF.Abs(p2 - p0) > MathF.Abs(dp)) (dp, dt) = (p2 - p0, t2 - t0);
        if (MathF.Abs(p2 - p1) > MathF.Abs(dp)) (dp, dt) = (p2 - p1, t2 - t1);
        if (dp == 0f || dt == 0f) return 0;
        return MathF.Sign(dp) == MathF.Sign(dt) ? 1 : -1;
    }

    private void TrackSurface(in HleVertex a, in HleVertex b, in HleVertex c)
    {
        var area = MathF.Abs((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y));
        var normal = Vector3.Cross(b.Camera - a.Camera, c.Camera - a.Camera);
        var length = normal.Length();
        if (area <= 0f || length < 1e-3f) return;
        normal /= length;
        if (_surfaceArea > 0f && Vector3.Dot(normal, new Vector3(_surfaceSum.X, _surfaceSum.Y, _surfaceSum.Z)) < 0f)
            normal = -normal;
        var center = (a.Camera + b.Camera + c.Camera) / 3f;
        _surfaceSum += new Vector4(normal, Vector3.Dot(normal, center)) * area;
        _surfaceArea += area;
    }

    private void PublishSurface()
    {
        if (!_drewSincePresent) return;
        var normal = new Vector3(_surfaceSum.X, _surfaceSum.Y, _surfaceSum.Z);
        var length = normal.Length();
        if (_surfaceArea > 0f && length > 1e-6f)
            ShaderData.Set("SceneWaterPlane", new Vector4(normal / length, _surfaceSum.W / length));
        else ShaderData.Remove("SceneWaterPlane");
        _drewSincePresent = false;
        _surfaceArea = 0f;
        _surfaceSum = Vector4.Zero;
    }

    public void DrawRect(in HleRect r, in PrimFlags f)
    {
        ResolveReplacement(f, r.U, r.V, r.U + Math.Max(0, r.W - 1), r.V + Math.Max(0, r.H - 1));
        Begin(f, 6);
        var a = new HleVertex { X = r.X, Y = r.Y, R = r.R, G = r.G, B = r.B, U = r.U, V = r.V };
        var b = new HleVertex { X = r.X + r.W, Y = r.Y, R = r.R, G = r.G, B = r.B, U = (short)(r.U + r.W), V = r.V };
        var c = new HleVertex { X = r.X, Y = r.Y + r.H, R = r.R, G = r.G, B = r.B, U = r.U, V = (short)(r.V + r.H) };
        var d = new HleVertex
            { X = r.X + r.W, Y = r.Y + r.H, R = r.R, G = r.G, B = r.B, U = (short)(r.U + r.W), V = (short)(r.V + r.H) };
        _verts[_count++] = V(a, f, false);
        _verts[_count++] = V(b, f, false);
        _verts[_count++] = V(c, f, false);
        _verts[_count++] = V(b, f, false);
        _verts[_count++] = V(d, f, false);
        _verts[_count++] = V(c, f, false);
    }

    public void DrawLine(in HleVertex a, in HleVertex b, in PrimFlags f)
    {
        _pendingRepTex = 0;
        _pendingRepClut = 0;
        Begin(f, 6, world: a.HasGteZ || b.HasGteZ);
        var dith = _env.Dither && Config.DisplayOptions.Dithering;
        float x1 = a.X, y1 = a.Y;
        float x2 = b.X, y2 = b.Y;
        float dx = x2 - x1, dy = y2 - y1;

        if (dx == 0 && dy == 0)
        {
            LineVert(x1, y1, a, f, dith);
            LineVert(x1 + 1, y1, a, f, dith);
            LineVert(x1 + 1, y1 + 1, a, f, dith);
            LineVert(x1 + 1, y1 + 1, a, f, dith);
            LineVert(x1, y1 + 1, a, f, dith);
            LineVert(x1, y1, a, f, dith);
            return;
        }

        float xo, yo;
        if (Math.Abs(dx) > Math.Abs(dy))
        {
            xo = 0;
            yo = 1;
            if (dx > 0) x2++;
            else x1++;
        }
        else
        {
            xo = 1;
            yo = 0;
            if (dy > 0) y2++;
            else y1++;
        }

        LineVert(x1, y1, a, f, dith);
        LineVert(x2, y2, b, f, dith);
        LineVert(x2 + xo, y2 + yo, b, f, dith);
        LineVert(x2 + xo, y2 + yo, b, f, dith);
        LineVert(x1 + xo, y1 + yo, a, f, dith);
        LineVert(x1, y1, a, f, dith);
    }

    private void LineVert(float x, float y, in HleVertex src, in PrimFlags f, bool dither)
    {
        var v = src;
        v.X = x;
        v.Y = y;
        _verts[_count++] = V(v, f, dither);
    }

    public void FillRect(int x, int y, int w, int h, ushort color15)
    {
        Flush();
        _vram.Fill(x, y, w, h, color15);
        foreach (var rt in _rts)
        {
            if (rt == null || !rt.Intersects(x, y, w, h)) continue;
            if (rt.Covers(x, y, x + w - 1, y + h - 1))
            {
                FillRtFull(rt, color15);
                rt.Dirty = false;
                rt.LastDrawFrame = _frame;
            }
            else
            {
                SyncRtFromVram(rt, x, y, w, h);
            }
        }
    }
    
    //the display margings dont work well on 16 9 this fix it and the command in the gpu
    public void FillDisplayMargins(int x, int y, int w, int h, ushort color15)
    {
        if (GpuHle.WideAspect <= 0f) return;
        var rt = Classify();
        if (rt == null || rt.Margin <= 0 || x > rt.X || x + w < rt.X + rt.W) return;

        var top = Math.Max(y, rt.Y) - rt.Y;
        var bottom = Math.Min(y + h, rt.Y + rt.H) - rt.Y;
        if (bottom <= top) return;

        Flush();
        ClearMargin(rt);

        var s = GlVram.Scale;
        var left = rt.Margin * s;
        var right = (rt.Margin + rt.W) * s;
        var bandY = top * s;
        var bandH = (uint)((bottom - top) * s);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, rt.Fbo);
        _gl.Enable(EnableCap.ScissorTest);
        _gl.ClearColor((color15 & 0x1F) / 31f, ((color15 >> 5) & 0x1F) / 31f,
            ((color15 >> 10) & 0x1F) / 31f, 1f);
        _gl.Scissor(0, bandY, (uint)left, bandH);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _gl.Scissor(right, bandY, (uint)(rt.TexW - right), bandH);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }
    
    private void ClearMargin(GlDisplayRt rt)
    {
        if (rt.Margin <= 0 || rt.LastMarginFrame == _frame) return;
        rt.LastMarginFrame = _frame;

        var s = GlVram.Scale;
        var left = rt.Margin * s;
        var right = (rt.Margin + rt.W) * s;

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, rt.Fbo);
        _gl.ClearColor(0f, 0f, 0f, 0f);
        _gl.Enable(EnableCap.ScissorTest);
        _gl.Scissor(0, 0, (uint)left, (uint)rt.TexH);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _gl.Scissor(right, 0, (uint)(rt.TexW - right), (uint)rt.TexH);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private void FillRtFull(GlDisplayRt rt, ushort color15)
    {
        rt.InvalidateDepth();
        float r = (color15 & 0x1F) / 31f, g = ((color15 >> 5) & 0x1F) / 31f, b = ((color15 >> 10) & 0x1F) / 31f;
        var a = (color15 & 0x8000) != 0 ? 1f : 0f;
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, rt.Fbo);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.ClearColor(r, g, b, a);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void CopyVram(int sx, int sy, int dx, int dy, int w, int h)
    {
        Flush();
        WritebackDirtyIntersecting(sx, sy, w, h);
        _vram.CopyRect(sx, sy, dx, dy, w, h);
        SyncRtsFromVram(dx, dy, w, h);
    }

    public void WriteVram(int x, int y, int w, int h, ReadOnlySpan<ushort> px)
    {
        Flush();
        _vram.WriteRect(x, y, w, h, px);
        SyncRtsFromVram(x, y, w, h);
    }

    public void ReadVram(int x, int y, int w, int h, Span<ushort> px)
    {
        Flush();
        WritebackDirtyIntersecting(x, y, w, h);
        _vram.ReadRect(x, y, w, h, px);
    }

    public int RegisterImage(ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (width <= 0 || height <= 0 || rgba.Length != checked(width * height * 4))
            throw new ArgumentException("Expected width * height * 4 RGBA bytes.");
        int id = Interlocked.Increment(ref _imageId) - 1;
        _pendingImages.Enqueue((id, rgba.ToArray(), width, height));
        return id;
    }

    public int ReserveImage()
    {
        return Interlocked.Increment(ref _imageId) - 1;
    }

    public void CaptureTarget(int image, IReadOnlyList<CaptureOverlay> overlays)
    {
        if (image < 0) return;
        Flush();
        UploadImages();
        if (_released.Remove(image)) return;
        var rt = Classify();
        if (rt == null) return;

        int width = rt.TexW, height = rt.TexH, size = width * height * 4;
        if (_captureBuffer.Length < size) _captureBuffer = new byte[size];
        var pixels = _captureBuffer.AsSpan(0, size);

        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, rt.Fbo);
        _gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);

        for (var i = 0; i < size; i += 4)
            pixels[i + 3] = (byte)((pixels[i] | pixels[i + 1] | pixels[i + 2]) >= 8 ? 255 : 0);

        foreach (var overlay in overlays)
            Composite(pixels, width, rt, overlay);

        if (!_images.TryGetValue(image, out var texture))
        {
            texture = _gl.GenTexture();
            _images[image] = texture;
        }

        _gl.ActiveTexture(TextureUnit.Texture7);
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, (ReadOnlySpan<byte>)pixels);
        _gl.ActiveTexture(TextureUnit.Texture0);

        _captures[image] = new CapturedImage(rt.Margin, rt.W, rt.H);
    }

    private static void Composite(Span<byte> pixels, int width, GlDisplayRt rt, CaptureOverlay overlay)
    {
        var s = GlVram.Scale;
        for (var y = 0; y < overlay.Height; y++)
        {
            var ty = overlay.Y + y;
            if (ty < 0 || ty >= rt.H) continue;
            for (var x = 0; x < overlay.Width; x++)
            {
                var source = (y * overlay.Width + x) * 4;
                if (overlay.Rgba[source + 3] < 128) continue;
                var tx = overlay.X + x + rt.Margin;
                if (tx < 0 || tx >= rt.Wide1x) continue;

                for (var sy = 0; sy < s; sy++)
                for (var sx = 0; sx < s; sx++)
                {
                    var target = ((ty * s + sy) * width + tx * s + sx) * 4;
                    if (overlay.Behind && pixels[target + 3] != 0) continue;
                    pixels[target] = overlay.Rgba[source];
                    pixels[target + 1] = overlay.Rgba[source + 1];
                    pixels[target + 2] = overlay.Rgba[source + 2];
                    pixels[target + 3] = 255;
                }
            }
        }
    }

    public bool TryGetCapture(int image, out CapturedImage capture)
    {
        return _captures.TryGetValue(image, out capture);
    }

    public void ReleaseImage(int image)
    {
        _captures.TryRemove(image, out _);
        _pendingReleases.Enqueue(image);
    }

    private void UploadImages()
    {
        while (_pendingReleases.TryDequeue(out var released))
        {
            if (_images.Remove(released, out var texture)) _gl.DeleteTexture(texture);
            else _released.Add(released);
        }

        while (_pendingImages.TryDequeue(out var image))
        {
            if (_released.Remove(image.Id)) continue;
            _gl.ActiveTexture(TextureUnit.Texture7);
            var t = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, t);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)image.Width, (uint)image.Height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, image.Pixels);
            _gl.ActiveTexture(TextureUnit.Texture0);

            _images.Add(image.Id, t);
        }
    }

    public void Flush()
    {
        if (_count == 0) return;
        UploadImages();

        var rt = _kTarget;
        uint destTex;
        if (rt == null)
        {
            _vram.BindDraw();
            destTex = _vram.Texture;
        }
        else
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, rt.Fbo);
            _gl.Viewport(0, 0, (uint)rt.TexW, (uint)rt.TexH);
            destTex = rt.Tex;
        }

        var destW = rt == null ? GlVram.Width : rt.TexW;
        var destH = rt == null ? GlVram.Height : rt.TexH;

        GpuGlAccess.Gl = _gl;
        GpuGlAccess.TargetFbo = rt == null ? _vram.Fbo : rt.Fbo;
        GpuGlAccess.TargetWidth = destW;
        GpuGlAccess.TargetHeight = destH;
        GpuGlAccess.TargetOriginX = rt == null ? 0 : rt.X;
        GpuGlAccess.TargetOriginY = rt == null ? 0 : rt.Y;
        GpuGlAccess.TargetMargin = rt == null ? 0 : rt.Margin;

        if (_kDepth && rt != null)
        {
            rt.PrepareDepth(_gl, _depthScene);
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(DepthFunction.Lequal);
            _gl.DepthMask(!ShaderMaterials.IsSurface(_kMaterial) && (!_kTransparent || _kMaterial != ShaderMaterials.None));
            Stencil(_kTransparent && _kMaterial == ShaderMaterials.None ? ShaderMaterials.Translucent : _kMaterial, true);
        }
        else if (_kStamp && rt != null)
        {
            rt.PrepareDepth(_gl, _depthScene);
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(DepthFunction.Always);
            _gl.DepthMask(true);
            Stencil(ShaderMaterials.None, true);
        }

        else
        {
            _gl.Disable(EnableCap.DepthTest);
            _gl.DepthMask(false);
            _gl.Disable(EnableCap.StencilTest);
        }
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.ScissorTest);
        var s = GlVram.Scale;

        int clipX0, clipY0, clipX1, clipY1;
        if (rt == null)
        {
            clipX0 = _kClipX0;
            clipY0 = _kClipY0;
            clipX1 = _kClipX1;
            clipY1 = _kClipY1;
        }
        else
        {
            clipX0 = _kClipX0 - rt.X + rt.Margin;
            clipY0 = _kClipY0 - rt.Y;
            clipX1 = _kClipX1 - rt.X + rt.Margin;
            clipY1 = _kClipY1 - rt.Y;
            if (rt.Margin > 0 && _kClipX0 <= rt.X && _kClipX1 >= rt.X + rt.W - 1)
            {
                clipX0 = 0;
                clipX1 = rt.Wide1x - 1;
            }
        }

        var bx0 = (int)Math.Floor(_drawMinX) + (rt == null ? 0 : rt.Margin - rt.X);
        var by0 = (int)Math.Floor(_drawMinY) - (rt == null ? 0 : rt.Y);
        var bx1 = (int)Math.Ceiling(_drawMaxX) + (rt == null ? 0 : rt.Margin - rt.X);
        var by1 = (int)Math.Ceiling(_drawMaxY) - (rt == null ? 0 : rt.Y);

        int rx0 = Math.Max(clipX0, bx0), ry0 = Math.Max(clipY0, by0);
        int rx1 = Math.Min(clipX1, bx1), ry1 = Math.Min(clipY1, by1);

        _gl.Scissor(clipX0 * s, clipY0 * s,
            (uint)Math.Max(0, (clipX1 - clipX0 + 1) * s), (uint)Math.Max(0, (clipY1 - clipY0 + 1) * s));

        var readX = Math.Max(0, rx0 * s);
        var readY = Math.Max(0, ry0 * s);
        var readW = Math.Max(0, (rx1 - rx0 + 1) * s);
        var readH = Math.Max(0, (ry1 - ry0 + 1) * s);
        var needDest = _legacy || _kCheckMask != 0 || _kQuantizedAlpha;
        if (needDest)
        {
            destTex = _vram.BeginDestRead(destTex, destW, destH, readX, readY, readW, readH);
            RebindTarget(rt);
            _vramDirty = false;
        }
        else if (rt == null && _kSamplesVram && SampledRegionIsDirty())
        {
            _vram.SampleBarrier();
            _vramDirty = false;
        }

        _gl.UseProgram(_progPrim);
        _gl.Uniform1(_uDepthEnabled, _kDepth ? 1 : _kStamp ? 2 : 0);
        var surface = !_legacy && _kDepth && ShaderMaterials.IsSurface(_kMaterial);
        if (_uSurface >= 0) _gl.Uniform1(_uSurface, surface ? 1 : 0);
        _gl.BindVertexArray(_vao);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _vram.Texture);
        _gl.ActiveTexture(TextureUnit.Texture1);
        _gl.BindTexture(TextureTarget.Texture2D, destTex);
        if (_images.TryGetValue(_kImage, out uint imageTexture))
        {
            _gl.ActiveTexture(TextureUnit.Texture2);
            _gl.BindTexture(TextureTarget.Texture2D, imageTexture);
        }

        if (_kRepTex != 0)
        {
            _gl.ActiveTexture(TextureUnit.Texture3);
            _gl.BindTexture(TextureTarget.Texture2D, _kRepTex);
            _gl.Uniform4(_uRepRect, _kRepX, _kRepY, _kRepW, _kRepH);
        }

        if (_kRepClut != 0)
        {
            _gl.ActiveTexture(TextureUnit.Texture4);
            _gl.BindTexture(TextureTarget.Texture2D, _kRepClut);
            _gl.Uniform1(_uRepClutCount, (float)_kRepClutCount);
        }

        _gl.ActiveTexture(TextureUnit.Texture0);
        if (rt != null)
        {
            _gl.Uniform2(_uPosBias, (float)(rt.Margin - rt.X), (float)-rt.Y);
            _gl.Uniform2(_uFbInv, 2f / rt.Wide1x, 2f / rt.H);
        }
        else
        {
            _gl.Uniform2(_uPosBias, 0f, 0f);
            _gl.Uniform2(_uFbInv, 2f / VramShadow.Width, 2f / VramShadow.Height);
        }

        if (_legacy)
        {
            _gl.Uniform4(_uTexWindow, (float)_kTwAndX, _kTwAndY, _kTwOrX, _kTwOrY);
            _gl.Uniform1(_uSetMask, _kSetMask == 1 ? 1f : 0f);
            _gl.Uniform1(_uCheckMask, _kCheckMask == 1 ? 1f : 0f);
            _gl.Uniform1(_uTrueColor, GlVram.TrueColor ? 1f : 0f);
            if (_uDestSize >= 0) _gl.Uniform2(_uDestSize, (float)destW, destH);
            if (_uSemiTrans >= 0) _gl.Uniform1(_uSemiTrans, _kTransparent ? 1f : 0f);
            if (_uBlendMode >= 0) _gl.Uniform1(_uBlendMode, (float)_kBlend);
        }
        else
        {
            _gl.Uniform4(_uTexWindow, _kTwAndX, _kTwAndY, _kTwOrX, _kTwOrY);
            _gl.Uniform1(_uSetMask, _kSetMask == 1 ? 1f : 0f);
            _gl.Uniform1(_uCheckMask, _kCheckMask);
            _gl.Uniform1(_uTrueColor, GlVram.TrueColor ? 1 : 0);
            _gl.Uniform4(_uBlendOpaque, 1f, 1f, 1f, 0f);
        }

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BufferSubData<GlVertex>(BufferTargetARB.ArrayBuffer, 0, _verts.AsSpan(0, _count));

        if (_legacy)
        {
            _gl.Disable(EnableCap.Blend);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_count);
        }
        else if (!_kTransparent && surface)
        {
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFuncSeparate(BlendingFactor.Src1Color, BlendingFactor.Src1Alpha, BlendingFactor.One,
                BlendingFactor.Zero);
            _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
            SetBlend(1f, 0f);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_count);
        }
        else if (!_kTransparent)
        {
            _gl.Disable(EnableCap.Blend);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_count);
        }
        else
        {
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFuncSeparate(BlendingFactor.Src1Color, BlendingFactor.Src1Alpha,
                surface ? BlendingFactor.Zero : BlendingFactor.One, surface ? BlendingFactor.One : BlendingFactor.Zero);
            if (_kBlend == 2)
            {
                _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
                SetBlend(0f, 1f);
                _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_count);

                if (needDest)
                {
                    _vram.BeginDestRead(destTex, destW, destH, readX, readY, readW, readH);
                    RebindTarget(rt);
                }

                _gl.BlendEquationSeparate(BlendEquationModeEXT.FuncReverseSubtract, BlendEquationModeEXT.FuncAdd);
                SetBlend(1f, 1f);
                _gl.Uniform4(_uBlendOpaque, 0f, 0f, 0f, 1f);
                _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_count);
            }
            else
            {
                _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
                SetBlend(_kBlend switch { 0 => 0.5f, 3 => 0.25f, _ => 1f }, _kBlend == 0 ? 0.5f : 1f);
                _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_count);
            }
        }

        if (_kLayer && rt != null && !_legacy) DrawLayer(rt);
        else if (_kDepth && rt != null && !_legacy && rt.HasUi(_depthScene)) ResetLayer(rt);

        _gl.Disable(EnableCap.ScissorTest);
        if (rt != null)
        {
            rt.Dirty = true;
            rt.LastDrawFrame = _frame;
            rt.LastScene = _depthScene;
        }
        else
        {
            var x0 = Math.Max(_kClipX0, (int)Math.Floor(_drawMinX));
            var y0 = Math.Max(_kClipY0, (int)Math.Floor(_drawMinY));
            var x1 = Math.Min(_kClipX1, (int)Math.Ceiling(_drawMaxX));
            var y1 = Math.Min(_kClipY1, (int)Math.Ceiling(_drawMaxY));
            if (x1 >= x0 && y1 >= y0)
            {
                Assets.Textures.VramTracker.MarkGpuWrite(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
                if (!_vramDirty)
                {
                    _vramDirty = true;
                    _vdX0 = x0;
                    _vdY0 = y0;
                    _vdX1 = x1 + 1;
                    _vdY1 = y1 + 1;
                }
                else
                {
                    if (x0 < _vdX0) _vdX0 = x0;
                    if (y0 < _vdY0) _vdY0 = y0;
                    if (x1 + 1 > _vdX1) _vdX1 = x1 + 1;
                    if (y1 + 1 > _vdY1) _vdY1 = y1 + 1;
                }
            }
        }

        _count = 0;
        _kSamplesVram = false;
    }

    private void SetBlend(float src, float dst)
    {
        _gl.Uniform4(_uBlend, src, src, src, dst);
    }

    private void SetScaleUniform(uint prog)
    {
        var loc = _gl.GetUniformLocation(prog, "uScale");
        if (loc < 0) return;
        if (_legacy) _gl.Uniform1(loc, (float)GlVram.Scale);
        else _gl.Uniform1(loc, GlVram.Scale);
    }

    private void RebindTarget(GlDisplayRt? rt)
    {
        if (rt == null)
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _vram.Fbo);
            _gl.Viewport(0, 0, (uint)GlVram.Width, (uint)GlVram.Height);
        }
        else
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, rt.Fbo);
            _gl.Viewport(0, 0, (uint)rt.TexW, (uint)rt.TexH);
        }
    }

    public void Present(in HleDispEnv disp)
    {
        PresentDisplay(disp.X, disp.Y, disp.W, disp.H, disp.Rgb24);
    }

    public unsafe (uint tex, int w, int h, float aspect) PresentDisplay(int dispX, int dispY, int w, int h,
        bool rgb24 = false, int outW = 0, int outH = 0)
    {
        if (!Ready || w <= 0 || h <= 0) return (0, 0, 0, GpuHle.OutputAspect);

        Flush();

        for (var i = 0; i < _rts.Length; i++)
        {
            if (_rts[i] is not { } rt) continue;
            if (rt.Dirty) Writeback(rt);
            if (_frame - rt.LastDrawFrame > 300)
            {
                rt.Destroy(_gl);
                _rts[i] = null;
            }
        }

        GlDisplayRt? src = null;
        if (!rgb24)
            foreach (var rt in _rts)
            {
                if (rt == null || _frame - rt.LastDrawFrame > 4) continue;
                if (dispX < rt.X || dispY < rt.Y || dispX + w > rt.X + rt.W || dispY + h > rt.Y + rt.H) continue;
                if (src == null || rt.LastDrawFrame > src.LastDrawFrame) src = rt;
            }

        var w1x = src != null ? w + src.Margin * 2 : w;
        var h1x = h;
        var aspect = src is { Margin: > 0 } ? GpuHle.WideAspect :
            src != null ? GpuHle.SourceAspect : GpuHle.OutputAspect;


        GpuHle.LastDisplayW = w;
        GpuHle.LastDisplayH = h;

        var presentScale = GlVram.Scale;
        var fbW = w1x * presentScale;
        var fbH = h1x * presentScale;
        EnsurePresentSize(fbW, fbH, GlVram.Scale == 1);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _presentFbo);
        _gl.Viewport(0, 0, (uint)fbW, (uint)fbH);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.StencilTest);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Disable(EnableCap.CullFace);

        _gl.UseProgram(rgb24 ? _progPresent24 : _progPresent);
        _gl.BindVertexArray(_presentVao);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, src?.Tex ?? _vram.Texture);
        if (rgb24)
        {
            _gl.Uniform2(_uPresent24Origin, (float)dispX, dispY);
            _gl.Uniform2(_uPresent24Size, (float)w, h);
        }
        else if (src != null)
        {
            _gl.Uniform2(_uPresentOrigin, (float)(dispX - src.X), dispY - src.Y);
            _gl.Uniform2(_uPresentSize, (float)w1x, h1x);
            _gl.Uniform2(_uPresentTexSize, (float)src.Wide1x, src.H);
        }
        else
        {
            _gl.Uniform2(_uPresentOrigin, (float)dispX, dispY);
            _gl.Uniform2(_uPresentSize, (float)w, h);
            _gl.Uniform2(_uPresentTexSize, (float)VramShadow.Width, VramShadow.Height);
        }

        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);

        var outTex = _presentTex;
        int outTexW = fbW, outTexH = fbH;
        if (PostFx.Active)
        {
            _postChain ??= new SlangChain(_gl);
            PublishSurface();
            PresentDepth(src, dispX, dispY, w1x, h1x, fbW, fbH);
            outTex = _postChain.Apply(_presentTex, fbW, fbH, w1x, h1x, outW, outH, aspect, out outTexW, out outTexH);
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        return (outTex, outTexW, outTexH, aspect);
    }

    private unsafe void PresentDepth(GlDisplayRt? src, int dispX, int dispY, int w1x, int h1x, int fbW, int fbH)
    {
        var chain = _postChain!;
        if (_progPresentDepth == 0 || src is not { DepthCurrent: true })
        {
            chain.SetDepth(0, 0, 0);
            chain.SetMaterial(0, 0, 0);
            chain.SetUiLayer(0, 0, 0);
            return;
        }

        if (_depthPresentTex == 0)
        {
            _depthPresentTex = _gl.GenTexture();
            _depthPresentFbo = _gl.GenFramebuffer();
        }

        if (_depthPresentW != fbW || _depthPresentH != fbH)
        {
            _gl.BindTexture(TextureTarget.Texture2D, _depthPresentTex);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R32f, (uint)fbW, (uint)fbH, 0, PixelFormat.Red,
                PixelType.Float, null);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _depthPresentFbo);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, _depthPresentTex, 0);
            _depthPresentW = fbW;
            _depthPresentH = fbH;
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _depthPresentFbo);
        _gl.Viewport(0, 0, (uint)fbW, (uint)fbH);
        _gl.UseProgram(_progPresentDepth);
        _gl.BindVertexArray(_presentVao);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, src.DepthTex);
        _gl.Uniform2(_uDepthOrigin, (float)(dispX - src.X), dispY - src.Y);
        _gl.Uniform2(_uDepthSize, (float)w1x, h1x);
        _gl.Uniform2(_uDepthTexSize, (float)src.Wide1x, src.H);
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        chain.SetDepth(_depthPresentTex, fbW, fbH);
        PresentMaterial(src, dispX, dispY, w1x, h1x, fbW, fbH);
        PresentLayer(src, dispX, dispY, w1x, h1x, fbW, fbH);
    }

    private unsafe void PresentMaterial(GlDisplayRt src, int dispX, int dispY, int w1x, int h1x, int fbW, int fbH)
    {
        var chain = _postChain!;
        if (_progPresentMaterial == 0 || !ShaderMaterials.Any)
        {
            chain.SetMaterial(0, 0, 0);
            return;
        }

        if (_materialPresentTex == 0)
        {
            _materialPresentTex = _gl.GenTexture();
            _materialPresentFbo = _gl.GenFramebuffer();
        }

        if (_materialPresentW != fbW || _materialPresentH != fbH)
        {
            _gl.BindTexture(TextureTarget.Texture2D, _materialPresentTex);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R8, (uint)fbW, (uint)fbH, 0, PixelFormat.Red,
                PixelType.UnsignedByte, null);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _materialPresentFbo);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, _materialPresentTex, 0);
            _materialPresentW = fbW;
            _materialPresentH = fbH;
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _materialPresentFbo);
        _gl.Viewport(0, 0, (uint)fbW, (uint)fbH);
        _gl.UseProgram(_progPresentMaterial);
        _gl.BindVertexArray(_presentVao);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, src.DepthTex);
        _gl.TexParameter(TextureTarget.Texture2D, (TextureParameterName)DepthStencilTextureMode, (int)GLEnum.StencilIndex);
        _gl.Uniform2(_uMaterialOrigin, (float)(dispX - src.X), dispY - src.Y);
        _gl.Uniform2(_uMaterialSize, (float)w1x, h1x);
        _gl.Uniform2(_uMaterialTexSize, (float)src.Wide1x, src.H);
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        _gl.TexParameter(TextureTarget.Texture2D, (TextureParameterName)DepthStencilTextureMode, (int)GLEnum.DepthComponent);
        chain.SetMaterial(_materialPresentTex, fbW, fbH);
    }

    private void DrawLayer(GlDisplayRt rt)
    {
        rt.PrepareUi(_gl, _depthScene);
        _gl.Viewport(0, 0, (uint)rt.TexW, (uint)rt.TexH);
        _gl.Enable(EnableCap.ScissorTest);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.StencilTest);
        _gl.DepthMask(false);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFuncSeparate(BlendingFactor.Src1Color, BlendingFactor.Src1Alpha, BlendingFactor.Zero,
            BlendingFactor.Src1Alpha);
        _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        _gl.Uniform4(_uBlendOpaque, 1f, 1f, 1f, 0f);

        if (!_kTransparent)
        {
            SetBlend(1f, 0f);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_count);
        }
        else if (_kBlend == 2)
        {
            SetBlend(0f, 1f);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_count);
            _gl.BlendEquationSeparate(BlendEquationModeEXT.FuncReverseSubtract, BlendEquationModeEXT.FuncAdd);
            SetBlend(1f, 1f);
            _gl.Uniform4(_uBlendOpaque, 0f, 0f, 0f, 1f);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_count);
        }
        else
        {
            SetBlend(_kBlend switch { 0 => 0.5f, 3 => 0.25f, _ => 1f }, _kBlend == 0 ? 0.5f : 1f);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_count);
        }

        _gl.Uniform4(_uBlendOpaque, 1f, 1f, 1f, 0f);
        _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        RebindTarget(rt);
    }

    private void ResetLayer(GlDisplayRt rt)
    {
        rt.PrepareUi(_gl, _depthScene);
        _gl.Viewport(0, 0, (uint)rt.TexW, (uint)rt.TexH);
        _gl.Enable(EnableCap.ScissorTest);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(false);
        _gl.Disable(EnableCap.StencilTest);
        _gl.Disable(EnableCap.Blend);
        _gl.Uniform1(_uLayerReset, 1);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_count);
        _gl.Uniform1(_uLayerReset, 0);
        RebindTarget(rt);
    }

    private unsafe void PresentLayer(GlDisplayRt src, int dispX, int dispY, int w1x, int h1x, int fbW, int fbH)
    {
        var chain = _postChain!;
        if (_progPresentLayer == 0 || !src.UiCurrent)
        {
            chain.SetUiLayer(0, 0, 0);
            return;
        }

        if (_layerPresentTex == 0)
        {
            _layerPresentTex = _gl.GenTexture();
            _layerPresentFbo = _gl.GenFramebuffer();
        }

        if (_layerPresentW != fbW || _layerPresentH != fbH)
        {
            _gl.BindTexture(TextureTarget.Texture2D, _layerPresentTex);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f, (uint)fbW, (uint)fbH, 0, PixelFormat.Rgba,
                PixelType.HalfFloat, null);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _layerPresentFbo);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, _layerPresentTex, 0);
            _layerPresentW = fbW;
            _layerPresentH = fbH;
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _layerPresentFbo);
        _gl.Viewport(0, 0, (uint)fbW, (uint)fbH);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.UseProgram(_progPresentLayer);
        _gl.BindVertexArray(_presentVao);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, src.UiTex);
        _gl.Uniform2(_uLayerOrigin, (float)(dispX - src.X), dispY - src.Y);
        _gl.Uniform2(_uLayerSize, (float)w1x, h1x);
        _gl.Uniform2(_uLayerTexSize, (float)src.Wide1x, src.H);
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        chain.SetUiLayer(_layerPresentTex, fbW, fbH);
    }

    private void Stencil(byte material, bool write)
    {
        _gl.Enable(EnableCap.StencilTest);
        _gl.StencilFunc(StencilFunction.Always, material, 0xFF);
        _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace);
        _gl.StencilMask(write ? 0xFFu : 0u);
    }

    private unsafe void EnsurePresentSize(int w, int h, bool nearest)
    {
        if (w == _presentW && h == _presentH && nearest == _presentNearest) return;
        _gl.BindTexture(TextureTarget.Texture2D, _presentTex);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)w, (uint)h, 0, PixelFormat.Rgba,
            PixelType.UnsignedByte, null);
        var filter = nearest ? GLEnum.Nearest : GLEnum.Linear;
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)filter);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)filter);
        _presentW = w;
        _presentH = h;
        _presentNearest = nearest;
    }

    public void Dispose()
    {
        foreach (var rt in _rts) rt?.Destroy(_gl);
        _vram.Dispose();
        if (_vbo != 0) _gl.DeleteBuffer(_vbo);
        if (_presentVbo != 0) _gl.DeleteBuffer(_presentVbo);
        if (_vao != 0) _gl.DeleteVertexArray(_vao);
        if (_presentVao != 0) _gl.DeleteVertexArray(_presentVao);
        if (_progPrim != 0) _gl.DeleteProgram(_progPrim);
        if (_progPresent != 0) _gl.DeleteProgram(_progPresent);
        if (_progPresent24 != 0) _gl.DeleteProgram(_progPresent24);
        if (_presentTex != 0) _gl.DeleteTexture(_presentTex);
        if (_presentFbo != 0) _gl.DeleteFramebuffer(_presentFbo);
        if (_depthPresentTex != 0) _gl.DeleteTexture(_depthPresentTex);
        if (_depthPresentFbo != 0) _gl.DeleteFramebuffer(_depthPresentFbo);
        if (_progPresentDepth != 0) _gl.DeleteProgram(_progPresentDepth);
        if (_progPresentLayer != 0) _gl.DeleteProgram(_progPresentLayer);
        if (_layerPresentTex != 0) _gl.DeleteTexture(_layerPresentTex);
        if (_layerPresentFbo != 0) _gl.DeleteFramebuffer(_layerPresentFbo);
        if (_materialPresentTex != 0) _gl.DeleteTexture(_materialPresentTex);
        if (_materialPresentFbo != 0) _gl.DeleteFramebuffer(_materialPresentFbo);
        if (_progPresentMaterial != 0) _gl.DeleteProgram(_progPresentMaterial);
        _postChain?.Dispose();
    }
}
