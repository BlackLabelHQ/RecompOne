using System.Numerics;
using Silk.NET.Input;
using Silk.NET.SDL;
using RecompOne.Runtime.Config;
using RecompOne.Runtime.Hardware;
using EventBus = RecompOne.Runtime.Events.Event;
using KeyboardEvent = RecompOne.Runtime.Events.KeyboardEvent;
using MouseEvent = RecompOne.Runtime.Events.MouseEvent;
using ControllerEvent = RecompOne.Runtime.Events.ControllerEvent;
using MouseAction = RecompOne.Runtime.Events.MouseAction;
using EvMouseButton = RecompOne.Runtime.Events.MouseButton;

namespace RecompOne.Runtime.Host;

internal static unsafe class InputManager
{
    static IKeyboard?_keyboard;
    static IMouse?_mouse;
    static Sdl?_sdl;
    static GameController* _pad0;
    static GameController* _pad1;

    const int AxisThreshold = 8000;
    const int StickThreshold = 16000;
    const int LeftTrigger = 100;
    const int RightTrigger = 101;
    const int LeftStickLeft = 102;
    const int LeftStickRight = 103;
    const int LeftStickUp = 104;
    const int LeftStickDown = 105;
    const int RightStickLeft = 106;
    const int RightStickRight = 107;
    const int RightStickUp = 108;
    const int RightStickDown = 109;
    static bool _topBarToggle;
    static bool _fullscreenToggle;

    
    public static bool ConsumeTopBarToggle() { var v = _topBarToggle; _topBarToggle = false; return v; }
    public static bool ConsumeFullscreenToggle(){ var v = _fullscreenToggle; _fullscreenToggle = false; return v; }

    public static void Initialize(IInputContext input)
    {
        if (input.Keyboards.Count > 0)
        {
            _keyboard = input.Keyboards[0];
            _keyboard.KeyDown += OnKeyDown;
            _keyboard.KeyUp += OnKeyUp;
        }

        if (input.Mice.Count > 0)
        {
            _mouse = input.Mice[0];
            _mouse.MouseMove += OnMouseMove;
            _mouse.MouseDown += OnMouseDown;
            _mouse.MouseUp += OnMouseUp;
            _mouse.Scroll += OnScroll;
        }


        try
        {
            _sdl = Sdl.GetApi();
            _sdl.SetHint("SDL_JOYSTICK_RAWINPUT", "0");
            _sdl.InitSubSystem(Sdl.InitGamecontroller | Sdl.InitAudio);
            Rescan();
        }
        catch { _sdl = null; }
    }

    public static bool IsConnected => _pad0 != null;

    public static bool IsPadConnected(int pad) => pad == 0 ? _pad0 != null : _pad1 != null;

    public static bool IsKeyDown(Key k) => _keyboard?.IsKeyPressed(k) ?? false;

    public static void Poll()
    {
        PollGamepadEvents();
        PollKeyboard();
        PollGamepads();
        Controller.Connected2 = _pad1 != null || HasAnyKey(ConfigManager.Game.Keys2);
    }

    public static int? GetFirstPressedPadButton(int pad = 0)
    {
        var ctrl = pad == 0 ? _pad0 : _pad1;
        if (_sdl == null || ctrl == null) return null;
        for (int b = 0; b < (int)GameControllerButton.Max; b++)
            if (_sdl.GameControllerGetButton(ctrl, (GameControllerButton)b) != 0)
                return b;
        if (Pressed(ctrl, LeftTrigger)) return LeftTrigger;
        if (Pressed(ctrl, RightTrigger)) return RightTrigger;
        for (int b = LeftStickLeft; b <= RightStickDown; b++)
            if (Pressed(ctrl, b)) return b;
        return null;
    }

    static bool IsStickBinding(int b) => b is >= LeftStickLeft and <= RightStickDown;

    static (GameControllerAxis Axis, bool Positive) AxisBinding(int b) => b switch
    {
        LeftStickLeft   => (GameControllerAxis.Leftx,  false),
        LeftStickRight  => (GameControllerAxis.Leftx,  true),
        LeftStickUp     => (GameControllerAxis.Lefty,  false),
        LeftStickDown   => (GameControllerAxis.Lefty,  true),
        RightStickLeft  => (GameControllerAxis.Rightx, false),
        RightStickRight => (GameControllerAxis.Rightx, true),
        RightStickUp    => (GameControllerAxis.Righty, false),
        _               => (GameControllerAxis.Righty, true),
    };

    public static void Shutdown()
    {
        CloseControllers();
        _sdl?.QuitSubSystem(Sdl.InitGamecontroller | Sdl.InitAudio);
        _sdl?.Dispose();
        _sdl = null;
    }

    static void PollGamepadEvents()
    {
        if (_sdl == null) return;
        Silk.NET.SDL.Event ev;
        bool changed = false;
        bool anyCtrl = EventBus.HasAnyListeners<ControllerEvent>();
        while (_sdl.PollEvent(&ev) != 0)
        {
            if (ev.Type == (uint)EventType.Controllerdeviceadded) changed = true;
            if (ev.Type == (uint)EventType.Controllerdeviceremoved) changed = true;
            if (ev.Type == (uint)EventType.Audiodeviceadded || ev.Type == (uint)EventType.Audiodeviceremoved)
                Audio.NotifyAudioDeviceChanged();
            if (!anyCtrl) continue;
            if (ev.Type == (uint)EventType.Controllerbuttondown || ev.Type == (uint)EventType.Controllerbuttonup)
                EventBus.Dispatch(new ControllerEvent
                {
                    Device = ev.Cbutton.Which,
                    Button = ev.Cbutton.Button,
                    Pressed = ev.Type == (uint)EventType.Controllerbuttondown,
                });
            else if (ev.Type == (uint)EventType.Controlleraxismotion)
                EventBus.Dispatch(new ControllerEvent
                {
                    Device = ev.Caxis.Which,
                    Axis = ev.Caxis.Axis,
                    Value = ev.Caxis.Value / 32768f,
                });
        }
        if (changed) Rescan();
    }

    static void CloseControllers()
    {
        if (_pad0 != null) { _sdl?.GameControllerClose(_pad0); _pad0 = null; }
        if (_pad1 != null) { _sdl?.GameControllerClose(_pad1); _pad1 = null; }
    }

    static void Rescan()
    {
        if (_sdl == null) return;
        CloseControllers();
        int n = _sdl.NumJoysticks();
        for (int i = 0; i < n; i++)
        {
            if (_sdl.IsGameController(i) != SdlBool.True) continue;
            var ctrl = _sdl.GameControllerOpen(i);
            if (ctrl == null) continue;
            if (_pad0 == null) _pad0 = ctrl;
            else { _pad1 = ctrl; break; }
        }
    }

    static void PollKeyboard()
    {
        var kb = _keyboard;
        if (kb == null)
        {
            Controller.State = 0xFFFF;
            Controller.State2 = 0xFFFF;
            return;
        }
        Controller.State = KeyState(kb, ConfigManager.Game.Keys);
        Controller.State2 = KeyState(kb, ConfigManager.Game.Keys2);
    }

    static ushort KeyState(IKeyboard kb, KeyBindings cfg)
    {
        ushort s = 0xFFFF;
        void B(string keyName, ushort bit)
        {
            if (Enum.TryParse<Key>(keyName, out var k) && kb.IsKeyPressed(k))
                s &= (ushort)~bit;
        }

        B(cfg.Cross,    Controller.Cross);
        B(cfg.Circle,   Controller.Circle);
        B(cfg.Square,   Controller.Square);
        B(cfg.Triangle, Controller.Triangle);
        B(cfg.L1,       Controller.L1);
        B(cfg.R1,       Controller.R1);
        B(cfg.L2,       Controller.L2);
        B(cfg.R2,       Controller.R2);
        B(cfg.L3,       Controller.L3);
        B(cfg.R3,       Controller.R3);
        B(cfg.Start,    Controller.Start);
        B(cfg.Select,   Controller.Select);
        B(cfg.Up,       Controller.Up);
        B(cfg.Down,     Controller.Down);
        B(cfg.Left,     Controller.Left);
        B(cfg.Right,    Controller.Right);

        return s;
    }

    static bool HasAnyKey(KeyBindings cfg) =>
        cfg.Cross.Length > 0 || cfg.Circle.Length > 0 || cfg.Square.Length > 0 || cfg.Triangle.Length > 0 ||
        cfg.L1.Length > 0 || cfg.R1.Length > 0 || cfg.L2.Length > 0 || cfg.R2.Length > 0 ||
        cfg.L3.Length > 0 || cfg.R3.Length > 0 || cfg.Start.Length > 0 || cfg.Select.Length > 0 ||
        cfg.Up.Length > 0 || cfg.Down.Length > 0 || cfg.Left.Length > 0 || cfg.Right.Length > 0;

    static void PollGamepads()
    {
        if (_sdl == null) return;

        if (_pad0 != null)
        {
            Controller.State = PadState(_pad0, ConfigManager.Game.Pad, Controller.State);
            Controller.LeftX = AxisToByte(_sdl.GameControllerGetAxis(_pad0, GameControllerAxis.Leftx));
            Controller.LeftY = AxisToByte(_sdl.GameControllerGetAxis(_pad0, GameControllerAxis.Lefty));
            Controller.RightX = AxisToByte(_sdl.GameControllerGetAxis(_pad0, GameControllerAxis.Rightx));
            Controller.RightY = AxisToByte(_sdl.GameControllerGetAxis(_pad0, GameControllerAxis.Righty));
        }

        if (_pad1 != null)
        {
            Controller.State2 = PadState(_pad1, ConfigManager.Game.Pad2, Controller.State2);
            Controller.LeftX2 = AxisToByte(_sdl.GameControllerGetAxis(_pad1, GameControllerAxis.Leftx));
            Controller.LeftY2 = AxisToByte(_sdl.GameControllerGetAxis(_pad1, GameControllerAxis.Lefty));
            Controller.RightX2 = AxisToByte(_sdl.GameControllerGetAxis(_pad1, GameControllerAxis.Rightx));
            Controller.RightY2 = AxisToByte(_sdl.GameControllerGetAxis(_pad1, GameControllerAxis.Righty));
        }
        else
        {
            Controller.LeftX2 = Controller.LeftY2 = Controller.RightX2 = Controller.RightY2 = 0x80;
        }
    }

    static ushort PadState(GameController* ctrl, GamepadBindings pad, ushort s)
    {
        s = Apply(ctrl, pad.Cross,    Controller.Cross,    s);
        s = Apply(ctrl, pad.Circle,   Controller.Circle,   s);
        s = Apply(ctrl, pad.Square,   Controller.Square,   s);
        s = Apply(ctrl, pad.Triangle, Controller.Triangle, s);
        s = Apply(ctrl, pad.L1,       Controller.L1,       s);
        s = Apply(ctrl, pad.R1,       Controller.R1,       s);
        s = Apply(ctrl, pad.L2,       Controller.L2,       s);
        s = Apply(ctrl, pad.R2,       Controller.R2,       s);
        s = Apply(ctrl, pad.L3,       Controller.L3,       s);
        s = Apply(ctrl, pad.R3,       Controller.R3,       s);
        s = Apply(ctrl, pad.Start,    Controller.Start,    s);
        s = Apply(ctrl, pad.Select,   Controller.Select,   s);
        s = Apply(ctrl, pad.Up,       Controller.Up,       s);
        s = Apply(ctrl, pad.Down,     Controller.Down,     s);
        s = Apply(ctrl, pad.Left,     Controller.Left,     s);
        s = Apply(ctrl, pad.Right,    Controller.Right,    s);
        return s;
    }

    static ushort Apply(GameController* ctrl, int[] bindings, ushort bit, ushort s)
    {
        foreach (var binding in bindings)
            if (Pressed(ctrl, binding))
                return (ushort)(s & ~bit);
        return s;
    }

    static bool Pressed(GameController* ctrl, int binding)
    {
        if (_sdl == null) return false;
        if (binding == LeftTrigger)
            return _sdl.GameControllerGetAxis(ctrl, GameControllerAxis.Triggerleft) > AxisThreshold;
        if (binding == RightTrigger)
            return _sdl.GameControllerGetAxis(ctrl, GameControllerAxis.Triggerright) > AxisThreshold;
        if (IsStickBinding(binding))
        {
            var (axis, positive) = AxisBinding(binding);
            short v = _sdl.GameControllerGetAxis(ctrl, axis);
            return positive ? v > StickThreshold : v < -StickThreshold;
        }
        return _sdl.GameControllerGetButton(ctrl, (GameControllerButton)binding) != 0;
    }

    static byte AxisToByte(short axis)
    {
        float f = Math.Clamp(axis * 1.3f / 32768.0f, -1.0f, 1.0f);
        return (byte)Math.Clamp((int)MathF.Round((f + 1.0f) * 127.5f), 0, 255);
    }

    public static void SetRumble(byte large, byte small)
    {
        if (_sdl == null || _pad0 == null) return;
        ushort lo = (ushort)(large * 257);
        ushort hi = small != 0 ? (ushort)65535 : (ushort)0;
        uint duration = large == 0 && small == 0 ? 0u : 500u;
        _sdl.GameControllerRumble(_pad0, lo, hi, duration);
    }

    static void OnKeyDown(IKeyboard kb, Key key, int _)
    {
        if (key == Key.F1)  _topBarToggle = true;
        if (key == Key.F11) _fullscreenToggle = true;

        if (EventBus.HasAnyListeners<KeyboardEvent>())
        {
            EventBus.Dispatch(new KeyboardEvent{
                Key = (int)key,
                Pressed = true
            });
        }
    }

    static void OnKeyUp(IKeyboard kb, Key key, int _)
    {
        if (EventBus.HasAnyListeners<KeyboardEvent>())
        {
            EventBus.Dispatch(new KeyboardEvent{
                Key = (int)key,
                Pressed = false
            });
        }
    }

    static void OnMouseMove(IMouse mouse, Vector2 position)
    {
        if (EventBus.HasAnyListeners<MouseEvent>())
        {
            EventBus.Dispatch(new MouseEvent
            {
                Action = MouseAction.Move,
                X = (int)position.X,
                Y = (int)position.Y
            });
        }
    }

    static void OnMouseDown(IMouse mouse, MouseButton mouseButton)
    {
        if (EventBus.HasAnyListeners<MouseEvent>())
        {
            EventBus.Dispatch(new MouseEvent
            {
                Action = MouseAction.Button,
                Button = MapMouseButton(mouseButton),
                Pressed = true,
                X = (int)mouse.Position.X,
                Y = (int)mouse.Position.Y
            });
        }
    }

    static void OnMouseUp(IMouse mouse, MouseButton mouseButton)
    {
        if (EventBus.HasAnyListeners<MouseEvent>())
        {
            EventBus.Dispatch(new MouseEvent
            {
                Action = MouseAction.Button,
                Button = MapMouseButton(mouseButton),
                Pressed = false,
                X = (int)mouse.Position.X,
                Y = (int)mouse.Position.Y
            });
        }
    }
    
    static void OnScroll(IMouse mouse, ScrollWheel wheel)
    {
        if (EventBus.HasAnyListeners<MouseEvent>())
        {
            EventBus.Dispatch(new MouseEvent
            {
                Action = MouseAction.Wheel,
                Wheel = (int)wheel.Y,
                X = (int)mouse.Position.X,
                Y = (int)mouse.Position.Y
            });
        }
    }
    static EvMouseButton MapMouseButton(MouseButton button) => button switch
    {
        MouseButton.Left => EvMouseButton.Left,
        MouseButton.Right => EvMouseButton.Right,
        MouseButton.Middle => EvMouseButton.Middle,
        _ => EvMouseButton.None
    };

}
