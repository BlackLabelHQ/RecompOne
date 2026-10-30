using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ImGuiNET;
using ImPlotNET;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace RecompOne.Runtime.Host.Window;

internal sealed class ImGuiController : IDisposable
{
    private const string VertexSource = """
        #version 330 core
        layout (location = 0) in vec2 Position;
        layout (location = 1) in vec2 UV;
        layout (location = 2) in vec4 Color;
        uniform mat4 ProjMtx;
        out vec2 Frag_UV;
        out vec4 Frag_Color;
        void main()
        {
            Frag_UV = UV;
            Frag_Color = Color;
            gl_Position = ProjMtx * vec4(Position.xy, 0, 1);
        }
        """;

    private const string FragmentSource = """
        #version 330 core
        in vec2 Frag_UV;
        in vec4 Frag_Color;
        uniform sampler2D Texture;
        layout (location = 0) out vec4 Out_Color;
        void main()
        {
            Out_Color = Frag_Color * texture(Texture, Frag_UV.st);
        }
        """;

    private readonly GL _gl;
    private readonly IView _view;
    private readonly IInputContext _input;
    private readonly IKeyboard? _keyboard;
    private readonly List<char> _typed = [];
    private readonly IntPtr _context;
    private readonly IntPtr _plotContext;

    private uint _program;
    private int _projection;
    private int _texture;
    private uint _vao;
    private uint _vbo;
    private uint _ebo;
    private uint _fontTexture;
    private float _wheel;
    private float _wheelH;
    private bool _frameBegun;
    private bool _disposed;
    private ImGuiMouseCursor _cursor = ImGuiMouseCursor.COUNT;

    private static IKeyboard? _clipboard;
    private static IntPtr _clipboardText;

    public ImGuiController(GL gl, IView view, IInputContext input, Action? configure)
    {
        _gl = gl;
        _view = view;
        _input = input;

        _context = ImGui.CreateContext();
        ImGui.SetCurrentContext(_context);
        _plotContext = ImPlot.CreateContext();
        ImPlot.SetImGuiContext(_context);
        ImPlot.SetCurrentContext(_plotContext);

        var io = ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;

        configure?.Invoke();

        CreateDeviceObjects();
        CreateFontTexture();

        _keyboard = input.Keyboards.Count > 0 ? input.Keyboards[0] : null;
        if (_keyboard != null)
        {
            _keyboard.KeyDown += OnKeyDown;
            _keyboard.KeyUp += OnKeyUp;
            _keyboard.KeyChar += OnKeyChar;
        }

        foreach (var mouse in input.Mice)
        {
            mouse.Scroll += OnScroll;
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.MouseMove += OnMouseMove;
        }

        view.FocusChanged += OnFocusChanged;
        InstallClipboard();

        SetFrameData(1f / 60f);
        ImGui.NewFrame();
        _frameBegun = true;
    }

    public void Update(float delta)
    {
        if (_frameBegun) ImGui.Render();

        SetFrameData(delta);
        UpdateInput();
        _frameBegun = true;
        ImGui.NewFrame();
    }

    public void Render()
    {
        if (!_frameBegun) return;

        _frameBegun = false;
        ImGui.Render();
        RenderDrawData(ImGui.GetDrawData());
    }

    private void SetFrameData(float delta)
    {
        var io = ImGui.GetIO();
        var size = _view.Size;
        var framebuffer = _view.FramebufferSize;
        io.DisplaySize = new Vector2(size.X, size.Y);
        if (size.X > 0 && size.Y > 0)
            io.DisplayFramebufferScale = new Vector2((float)framebuffer.X / size.X, (float)framebuffer.Y / size.Y);
        io.DeltaTime = delta > 0f ? delta : 1f / 60f;
    }

    private void UpdateInput()
    {
        var io = ImGui.GetIO();

        if (_wheel != 0f || _wheelH != 0f)
        {
            io.AddMouseWheelEvent(_wheelH, _wheel);
            _wheel = 0f;
            _wheelH = 0f;
        }

        if (_keyboard != null)
        {
            io.AddKeyEvent(ImGuiKey.ModCtrl, _keyboard.IsKeyPressed(Key.ControlLeft) || _keyboard.IsKeyPressed(Key.ControlRight));
            io.AddKeyEvent(ImGuiKey.ModShift, _keyboard.IsKeyPressed(Key.ShiftLeft) || _keyboard.IsKeyPressed(Key.ShiftRight));
            io.AddKeyEvent(ImGuiKey.ModAlt, _keyboard.IsKeyPressed(Key.AltLeft) || _keyboard.IsKeyPressed(Key.AltRight));
            io.AddKeyEvent(ImGuiKey.ModSuper, _keyboard.IsKeyPressed(Key.SuperLeft) || _keyboard.IsKeyPressed(Key.SuperRight));
        }

        foreach (var c in _typed) io.AddInputCharacter(c);
        _typed.Clear();

        UpdateCursor(io);
    }

    private void UpdateCursor(ImGuiIOPtr io)
    {
        if ((io.ConfigFlags & ImGuiConfigFlags.NoMouseCursorChange) != 0 || _input.Mice.Count == 0) return;

        var wanted = io.MouseDrawCursor ? ImGuiMouseCursor.None : ImGui.GetMouseCursor();
        if (wanted == _cursor) return;
        _cursor = wanted;

        var cursor = _input.Mice[0].Cursor;
        if (wanted == ImGuiMouseCursor.None)
        {
            cursor.CursorMode = CursorMode.Hidden;
            return;
        }

        cursor.CursorMode = CursorMode.Normal;
        cursor.StandardCursor = wanted switch
        {
            ImGuiMouseCursor.TextInput => StandardCursor.IBeam,
            ImGuiMouseCursor.ResizeAll => StandardCursor.ResizeAll,
            ImGuiMouseCursor.ResizeNS => StandardCursor.VResize,
            ImGuiMouseCursor.ResizeEW => StandardCursor.HResize,
            ImGuiMouseCursor.ResizeNESW => StandardCursor.NeswResize,
            ImGuiMouseCursor.ResizeNWSE => StandardCursor.NwseResize,
            ImGuiMouseCursor.Hand => StandardCursor.Hand,
            ImGuiMouseCursor.NotAllowed => StandardCursor.NotAllowed,
            _ => StandardCursor.Default
        };
    }

    private static void OnMouseDown(IMouse mouse, MouseButton button) => SendButton(button, true);

    private static void OnMouseUp(IMouse mouse, MouseButton button) => SendButton(button, false);

    private static void OnMouseMove(IMouse mouse, Vector2 position) => ImGui.GetIO().AddMousePosEvent(position.X, position.Y);

    private static void OnFocusChanged(bool focused) => ImGui.GetIO().AddFocusEvent(focused);

    private static void SendButton(MouseButton button, bool down)
    {
        var index = button switch
        {
            MouseButton.Left => 0,
            MouseButton.Right => 1,
            MouseButton.Middle => 2,
            MouseButton.Button4 => 3,
            MouseButton.Button5 => 4,
            _ => -1
        };
        if (index >= 0) ImGui.GetIO().AddMouseButtonEvent(index, down);
    }

    private unsafe void InstallClipboard()
    {
        _clipboard = _keyboard;
        if (_clipboard == null) return;

        var platform = ImGui.GetPlatformIO();
        platform.Platform_GetClipboardTextFn = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr>)&GetClipboard;
        platform.Platform_SetClipboardTextFn = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&SetClipboard;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static IntPtr GetClipboard(IntPtr context)
    {
        if (_clipboardText != IntPtr.Zero) Marshal.FreeCoTaskMem(_clipboardText);
        _clipboardText = Marshal.StringToCoTaskMemUTF8(_clipboard?.ClipboardText ?? "");
        return _clipboardText;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void SetClipboard(IntPtr context, IntPtr text)
    {
        if (_clipboard != null) _clipboard.ClipboardText = Marshal.PtrToStringUTF8(text) ?? "";
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int scancode) => SendKey(key, true);

    private void OnKeyUp(IKeyboard keyboard, Key key, int scancode) => SendKey(key, false);

    private void OnKeyChar(IKeyboard keyboard, char c) => _typed.Add(c);

    private void OnScroll(IMouse mouse, ScrollWheel wheel)
    {
        _wheel += wheel.Y;
        _wheelH += wheel.X;
    }

    private static void SendKey(Key key, bool down)
    {
        var mapped = Map(key);
        if (mapped != ImGuiKey.None) ImGui.GetIO().AddKeyEvent(mapped, down);
    }

    private static ImGuiKey Map(Key key)
    {
        if (key is >= Key.A and <= Key.Z) return ImGuiKey.A + (key - Key.A);
        if (key is >= Key.Number0 and <= Key.Number9) return ImGuiKey._0 + (key - Key.Number0);
        if (key is >= Key.Keypad0 and <= Key.Keypad9) return ImGuiKey.Keypad0 + (key - Key.Keypad0);
        if (key is >= Key.F1 and <= Key.F12) return ImGuiKey.F1 + (key - Key.F1);

        return key switch
        {
            Key.Tab => ImGuiKey.Tab,
            Key.Left => ImGuiKey.LeftArrow,
            Key.Right => ImGuiKey.RightArrow,
            Key.Up => ImGuiKey.UpArrow,
            Key.Down => ImGuiKey.DownArrow,
            Key.PageUp => ImGuiKey.PageUp,
            Key.PageDown => ImGuiKey.PageDown,
            Key.Home => ImGuiKey.Home,
            Key.End => ImGuiKey.End,
            Key.Insert => ImGuiKey.Insert,
            Key.Delete => ImGuiKey.Delete,
            Key.Backspace => ImGuiKey.Backspace,
            Key.Space => ImGuiKey.Space,
            Key.Enter => ImGuiKey.Enter,
            Key.Escape => ImGuiKey.Escape,
            Key.Apostrophe => ImGuiKey.Apostrophe,
            Key.Comma => ImGuiKey.Comma,
            Key.Minus => ImGuiKey.Minus,
            Key.Period => ImGuiKey.Period,
            Key.Slash => ImGuiKey.Slash,
            Key.Semicolon => ImGuiKey.Semicolon,
            Key.Equal => ImGuiKey.Equal,
            Key.LeftBracket => ImGuiKey.LeftBracket,
            Key.BackSlash => ImGuiKey.Backslash,
            Key.RightBracket => ImGuiKey.RightBracket,
            Key.GraveAccent => ImGuiKey.GraveAccent,
            Key.CapsLock => ImGuiKey.CapsLock,
            Key.ScrollLock => ImGuiKey.ScrollLock,
            Key.NumLock => ImGuiKey.NumLock,
            Key.PrintScreen => ImGuiKey.PrintScreen,
            Key.Pause => ImGuiKey.Pause,
            Key.KeypadDecimal => ImGuiKey.KeypadDecimal,
            Key.KeypadDivide => ImGuiKey.KeypadDivide,
            Key.KeypadMultiply => ImGuiKey.KeypadMultiply,
            Key.KeypadSubtract => ImGuiKey.KeypadSubtract,
            Key.KeypadAdd => ImGuiKey.KeypadAdd,
            Key.KeypadEnter => ImGuiKey.KeypadEnter,
            Key.KeypadEqual => ImGuiKey.KeypadEqual,
            Key.ShiftLeft => ImGuiKey.LeftShift,
            Key.ControlLeft => ImGuiKey.LeftCtrl,
            Key.AltLeft => ImGuiKey.LeftAlt,
            Key.SuperLeft => ImGuiKey.LeftSuper,
            Key.ShiftRight => ImGuiKey.RightShift,
            Key.ControlRight => ImGuiKey.RightCtrl,
            Key.AltRight => ImGuiKey.RightAlt,
            Key.SuperRight => ImGuiKey.RightSuper,
            Key.Menu => ImGuiKey.Menu,
            _ => ImGuiKey.None
        };
    }

    private void CreateDeviceObjects()
    {
        var vertex = Compile(ShaderType.VertexShader, VertexSource);
        var fragment = Compile(ShaderType.FragmentShader, FragmentSource);
        _program = _gl.CreateProgram();
        _gl.AttachShader(_program, vertex);
        _gl.AttachShader(_program, fragment);
        _gl.LinkProgram(_program);
        _gl.GetProgram(_program, ProgramPropertyARB.LinkStatus, out var linked);
        if (linked == 0) throw new InvalidOperationException($"ImGui shader link failed: {_gl.GetProgramInfoLog(_program)}");
        _gl.DetachShader(_program, vertex);
        _gl.DetachShader(_program, fragment);
        _gl.DeleteShader(vertex);
        _gl.DeleteShader(fragment);

        _projection = _gl.GetUniformLocation(_program, "ProjMtx");
        _texture = _gl.GetUniformLocation(_program, "Texture");

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _ebo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        var stride = (uint)System.Runtime.CompilerServices.Unsafe.SizeOf<ImDrawVert>();
        _gl.EnableVertexAttribArray(0);
        _gl.EnableVertexAttribArray(1);
        _gl.EnableVertexAttribArray(2);
        unsafe
        {
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);
            _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)8);
            _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, stride, (void*)16);
        }
        _gl.BindVertexArray(0);
    }

    private uint Compile(ShaderType type, string source)
    {
        var shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out var compiled);
        if (compiled == 0) throw new InvalidOperationException($"ImGui shader compile failed: {_gl.GetShaderInfoLog(shader)}");
        return shader;
    }

    private unsafe void CreateFontTexture()
    {
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out var width, out var height, out _);

        _gl.GetInteger(GetPName.TextureBinding2D, out var previous);
        _fontTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _fontTexture);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)width, (uint)height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, (void*)pixels);
        _gl.BindTexture(TextureTarget.Texture2D, (uint)previous);

        io.Fonts.SetTexID((IntPtr)_fontTexture);
        io.Fonts.ClearTexData();
    }

    private unsafe void RenderDrawData(ImDrawDataPtr data)
    {
        var width = (int)(data.DisplaySize.X * data.FramebufferScale.X);
        var height = (int)(data.DisplaySize.Y * data.FramebufferScale.Y);
        if (width <= 0 || height <= 0 || data.CmdListsCount == 0) return;

        var state = GlState.Capture(_gl);

        _gl.Enable(EnableCap.Blend);
        _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        _gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.StencilTest);
        _gl.Enable(EnableCap.ScissorTest);
        _gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
        _gl.Viewport(0, 0, (uint)width, (uint)height);

        var left = data.DisplayPos.X;
        var right = data.DisplayPos.X + data.DisplaySize.X;
        var top = data.DisplayPos.Y;
        var bottom = data.DisplayPos.Y + data.DisplaySize.Y;
        Span<float> projection =
        [
            2f / (right - left), 0f, 0f, 0f,
            0f, 2f / (top - bottom), 0f, 0f,
            0f, 0f, -1f, 0f,
            (right + left) / (left - right), (top + bottom) / (bottom - top), 0f, 1f
        ];

        _gl.UseProgram(_program);
        _gl.Uniform1(_texture, 0);
        _gl.UniformMatrix4(_projection, 1, false, projection);
        _gl.BindSampler(0, 0);
        _gl.BindVertexArray(_vao);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);

        var clipOffset = data.DisplayPos;
        var clipScale = data.FramebufferScale;
        var vertexSize = System.Runtime.CompilerServices.Unsafe.SizeOf<ImDrawVert>();

        for (var n = 0; n < data.CmdListsCount; n++)
        {
            var list = data.CmdLists[n];
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(list.VtxBuffer.Size * vertexSize), (void*)list.VtxBuffer.Data, BufferUsageARB.StreamDraw);
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(list.IdxBuffer.Size * sizeof(ushort)), (void*)list.IdxBuffer.Data, BufferUsageARB.StreamDraw);

            for (var i = 0; i < list.CmdBuffer.Size; i++)
            {
                var cmd = list.CmdBuffer[i];
                if (cmd.UserCallback != IntPtr.Zero) continue;

                var x0 = (cmd.ClipRect.X - clipOffset.X) * clipScale.X;
                var y0 = (cmd.ClipRect.Y - clipOffset.Y) * clipScale.Y;
                var x1 = (cmd.ClipRect.Z - clipOffset.X) * clipScale.X;
                var y1 = (cmd.ClipRect.W - clipOffset.Y) * clipScale.Y;
                if (x1 <= x0 || y1 <= y0) continue;

                _gl.Scissor((int)x0, (int)(height - y1), (uint)(x1 - x0), (uint)(y1 - y0));
                _gl.BindTexture(TextureTarget.Texture2D, (uint)cmd.TextureId);
                _gl.DrawElementsBaseVertex(PrimitiveType.Triangles, cmd.ElemCount, DrawElementsType.UnsignedShort,
                    (void*)(cmd.IdxOffset * sizeof(ushort)), (int)cmd.VtxOffset);
            }
        }

        state.Restore(_gl);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_keyboard != null)
        {
            _keyboard.KeyDown -= OnKeyDown;
            _keyboard.KeyUp -= OnKeyUp;
            _keyboard.KeyChar -= OnKeyChar;
        }

        foreach (var mouse in _input.Mice)
        {
            mouse.Scroll -= OnScroll;
            mouse.MouseDown -= OnMouseDown;
            mouse.MouseUp -= OnMouseUp;
            mouse.MouseMove -= OnMouseMove;
        }

        _view.FocusChanged -= OnFocusChanged;
        if (_clipboard == _keyboard) _clipboard = null;
        if (_clipboardText != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(_clipboardText);
            _clipboardText = IntPtr.Zero;
        }

        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteTexture(_fontTexture);
        _gl.DeleteProgram(_program);

        ImPlot.DestroyContext(_plotContext);
        ImGui.DestroyContext(_context);
    }

    private readonly struct GlState
    {
        private readonly int _program;
        private readonly int _texture;
        private readonly int _active;
        private readonly int _sampler;
        private readonly int _array;
        private readonly int _vertexArray;
        private readonly int _blendSrcRgb;
        private readonly int _blendDstRgb;
        private readonly int _blendSrcAlpha;
        private readonly int _blendDstAlpha;
        private readonly int _blendEquationRgb;
        private readonly int _blendEquationAlpha;
        private readonly int[] _viewport;
        private readonly int[] _scissor;
        private readonly bool _blend;
        private readonly bool _cull;
        private readonly bool _depth;
        private readonly bool _stencil;
        private readonly bool _scissorTest;

        private GlState(GL gl)
        {
            gl.GetInteger(GetPName.CurrentProgram, out _program);
            gl.GetInteger(GetPName.ActiveTexture, out _active);
            gl.ActiveTexture(TextureUnit.Texture0);
            gl.GetInteger(GetPName.TextureBinding2D, out _texture);
            gl.GetInteger(GetPName.SamplerBinding, out _sampler);
            gl.GetInteger(GetPName.ArrayBufferBinding, out _array);
            gl.GetInteger(GetPName.VertexArrayBinding, out _vertexArray);
            gl.GetInteger(GetPName.BlendSrcRgb, out _blendSrcRgb);
            gl.GetInteger(GetPName.BlendDstRgb, out _blendDstRgb);
            gl.GetInteger(GetPName.BlendSrcAlpha, out _blendSrcAlpha);
            gl.GetInteger(GetPName.BlendDstAlpha, out _blendDstAlpha);
            gl.GetInteger(GetPName.BlendEquationRgb, out _blendEquationRgb);
            gl.GetInteger(GetPName.BlendEquationAlpha, out _blendEquationAlpha);
            _viewport = new int[4];
            _scissor = new int[4];
            gl.GetInteger(GetPName.Viewport, _viewport);
            gl.GetInteger(GetPName.ScissorBox, _scissor);
            _blend = gl.IsEnabled(EnableCap.Blend);
            _cull = gl.IsEnabled(EnableCap.CullFace);
            _depth = gl.IsEnabled(EnableCap.DepthTest);
            _stencil = gl.IsEnabled(EnableCap.StencilTest);
            _scissorTest = gl.IsEnabled(EnableCap.ScissorTest);
        }

        public static GlState Capture(GL gl) => new(gl);

        public void Restore(GL gl)
        {
            gl.UseProgram((uint)_program);
            gl.BindTexture(TextureTarget.Texture2D, (uint)_texture);
            gl.BindSampler(0, (uint)_sampler);
            gl.ActiveTexture((TextureUnit)_active);
            gl.BindVertexArray((uint)_vertexArray);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, (uint)_array);
            gl.BlendEquationSeparate((BlendEquationModeEXT)_blendEquationRgb, (BlendEquationModeEXT)_blendEquationAlpha);
            gl.BlendFuncSeparate((BlendingFactor)_blendSrcRgb, (BlendingFactor)_blendDstRgb,
                (BlendingFactor)_blendSrcAlpha, (BlendingFactor)_blendDstAlpha);
            Toggle(gl, EnableCap.Blend, _blend);
            Toggle(gl, EnableCap.CullFace, _cull);
            Toggle(gl, EnableCap.DepthTest, _depth);
            Toggle(gl, EnableCap.StencilTest, _stencil);
            Toggle(gl, EnableCap.ScissorTest, _scissorTest);
            gl.Viewport(_viewport[0], _viewport[1], (uint)_viewport[2], (uint)_viewport[3]);
            gl.Scissor(_scissor[0], _scissor[1], (uint)_scissor[2], (uint)_scissor[3]);
        }

        private static void Toggle(GL gl, EnableCap cap, bool on)
        {
            if (on) gl.Enable(cap);
            else gl.Disable(cap);
        }
    }
}
