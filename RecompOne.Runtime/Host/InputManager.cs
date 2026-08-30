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
    private static IKeyboard? _keyboard;
    private static IMouse? _mouse;
    private static Sdl? _sdl;
    private static GameController* _pad0;
    private static GameController* _pad1;

    private const int AxisThreshold = 8000;
    private const int StickThreshold = 16000;
    private const int LeftTrigger = 100;
    private const int RightTrigger = 101;
    private const int LeftStickLeft = 102;
    private const int LeftStickRight = 103;
    private const int LeftStickUp = 104;
    private const int LeftStickDown = 105;
    private const int RightStickLeft = 106;
    private const int RightStickRight = 107;
    private const int RightStickUp = 108;
    private const int RightStickDown = 109;
    private static bool _topBarToggle;
    private static bool _fullscreenToggle;


    public static bool ConsumeTopBarToggle()
    {
        var v = _topBarToggle;
        _topBarToggle = false;
        return v;
    }

    public static bool ConsumeFullscreenToggle()
    {
        var v = _fullscreenToggle;
        _fullscreenToggle = false;
        return v;
    }

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
            _sdl.InitSubSystem(Sdl.InitGamecontroller);
            Rescan();
        }
        catch
        {
            _sdl = null;
        }
    }

    public static bool IsConnected => _pad0 != null;

    public static bool IsPadConnected(int pad)
    {
        return pad == 0 ? _pad0 != null : _pad1 != null;
    }

    public static bool IsKeyDown(Key k)
    {
        return _keyboard?.IsKeyPressed(k) ?? false;
    }

    public static void Poll()
    {
        Controller.Analog = ConfigManager.Game.PadKind == PadKind.Analog;
        Controller.Analog2 = ConfigManager.Game.PadKind2 == PadKind.Analog;


        PollGamepadEvents();
        PollKeyboard();
        PollGamepads();
        Controller.Connected2 = _pad1 != null || HasAnyKey(ConfigManager.Game.Keys2);
    }

    public static int? GetFirstPressedPadButton(int pad = 0)
    {
        var ctrl = pad == 0 ? _pad0 : _pad1;
        if (_sdl == null || ctrl == null) return null;
        for (var b = 0; b < (int)GameControllerButton.Max; b++)
            if (_sdl.GameControllerGetButton(ctrl, (GameControllerButton)b) != 0)
                return b;
        if (Pressed(ctrl, LeftTrigger)) return LeftTrigger;
        if (Pressed(ctrl, RightTrigger)) return RightTrigger;
        for (var b = LeftStickLeft; b <= RightStickDown; b++)
            if (Pressed(ctrl, b))
                return b;
        return null;
    }

    private static bool IsStickBinding(int b)
    {
        return b is >= LeftStickLeft and <= RightStickDown;
    }

    private static (GameControllerAxis Axis, bool Positive) AxisBinding(int b)
    {
        return b switch
        {
            LeftStickLeft => (GameControllerAxis.Leftx, false),
            LeftStickRight => (GameControllerAxis.Leftx, true),
            LeftStickUp => (GameControllerAxis.Lefty, false),
            LeftStickDown => (GameControllerAxis.Lefty, true),
            RightStickLeft => (GameControllerAxis.Rightx, false),
            RightStickRight => (GameControllerAxis.Rightx, true),
            RightStickUp => (GameControllerAxis.Righty, false),
            _ => (GameControllerAxis.Righty, true)
        };
    }

    public static void Shutdown()
    {
        CloseControllers();
        _sdl?.QuitSubSystem(Sdl.InitGamecontroller);
        _sdl?.Dispose();
        _sdl = null;
    }

    private static void PollGamepadEvents()
    {
        if (_sdl == null) return;
        Event ev;
        var changed = false;
        var anyCtrl = EventBus.HasAnyListeners<ControllerEvent>();
        while (_sdl.PollEvent(&ev) != 0)
        {
            if (ev.Type == (uint)EventType.Controllerdeviceadded) changed = true;
            if (ev.Type == (uint)EventType.Controllerdeviceremoved) changed = true;
            if (!anyCtrl) continue;
            if (ev.Type == (uint)EventType.Controllerbuttondown || ev.Type == (uint)EventType.Controllerbuttonup)
                EventBus.Dispatch(new ControllerEvent
                {
                    Device = ev.Cbutton.Which,
                    Button = ev.Cbutton.Button,
                    Pressed = ev.Type == (uint)EventType.Controllerbuttondown
                });
            else if (ev.Type == (uint)EventType.Controlleraxismotion)
                EventBus.Dispatch(new ControllerEvent
                {
                    Device = ev.Caxis.Which,
                    Axis = ev.Caxis.Axis,
                    Value = ev.Caxis.Value / 32768f
                });
        }

        if (changed) Rescan();
    }

    private static void CloseControllers()
    {
        if (_pad0 != null)
        {
            _sdl?.GameControllerClose(_pad0);
            _pad0 = null;
        }

        if (_pad1 != null)
        {
            _sdl?.GameControllerClose(_pad1);
            _pad1 = null;
        }
    }

    public readonly record struct PadDevice(string Id, string Name);

    private static readonly List<PadDevice> _devices = [];

    public static IReadOnlyList<PadDevice> Devices
    {
        get
        {
            lock (_devices)
            {
                return _devices.ToArray();
            }
        }
    }

    public static void RefreshDevices()
    {
        Rescan();
    }

    private static string DeviceId(int joystickIndex)
    {
        if (_sdl == null) return "";
        var guid = _sdl.JoystickGetDeviceGUID(joystickIndex);
        var text = new byte[33];
        fixed (byte* p = text)
        {
            _sdl.JoystickGetGUIDString(guid, p, text.Length);
        }

        var len = Array.IndexOf(text, (byte)0);
        return System.Text.Encoding.ASCII.GetString(text, 0, len < 0 ? text.Length : len);
    }

    private static string DeviceName(int joystickIndex)
    {
        if (_sdl == null) return "";
        var name = _sdl.GameControllerNameForIndexS(joystickIndex);
        return string.IsNullOrWhiteSpace(name) ? $"Controller {joystickIndex}" : name;
    }

    private static void Rescan()
    {
        if (_sdl == null) return;
        CloseControllers();

        var found = new List<(int Index, string Id, string Name)>();
        var n = _sdl.NumJoysticks();
        for (var i = 0; i < n; i++)
        {
            if (_sdl.IsGameController(i) != SdlBool.True) continue;
            found.Add((i, DeviceId(i), DeviceName(i)));
        }

        lock (_devices)
        {
            _devices.Clear();
            foreach (var f in found) _devices.Add(new PadDevice(f.Id, f.Name));
        }

        var used = new HashSet<int>();
        _pad0 = OpenFor(found, ConfigManager.Game.PadDevice, used);
        _pad1 = OpenFor(found, ConfigManager.Game.PadDevice2, used);
    }

    private static GameController* OpenFor(List<(int Index, string Id, string Name)> found, string wanted,
        HashSet<int> used)
    {
        if (_sdl == null) return null;

        var pick = -1;
        if (!string.IsNullOrEmpty(wanted))
        {
            foreach (var f in found)
                if (f.Id == wanted && used.Add(f.Index))
                {
                    pick = f.Index;
                    break;
                }

            if (pick < 0) return null;
        }
        else
        {
            foreach (var f in found)
                if (used.Add(f.Index))
                {
                    pick = f.Index;
                    break;
                }

            if (pick < 0) return null;
        }

        var ctrl = _sdl.GameControllerOpen(pick);
        if (ctrl == null) used.Remove(pick);
        return ctrl;
    }

    private static void PollKeyboard()
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

    private static ushort KeyState(IKeyboard kb, KeyBindings cfg)
    {
        ushort s = 0xFFFF;

        void B(string keyName, ushort bit)
        {
            if (Enum.TryParse<Key>(keyName, out var k) && kb.IsKeyPressed(k))
                s &= (ushort)~bit;
        }

        B(cfg.Cross, Controller.Cross);
        B(cfg.Circle, Controller.Circle);
        B(cfg.Square, Controller.Square);
        B(cfg.Triangle, Controller.Triangle);
        B(cfg.L1, Controller.L1);
        B(cfg.R1, Controller.R1);
        B(cfg.L2, Controller.L2);
        B(cfg.R2, Controller.R2);
        B(cfg.L3, Controller.L3);
        B(cfg.R3, Controller.R3);
        B(cfg.Start, Controller.Start);
        B(cfg.Select, Controller.Select);
        B(cfg.Up, Controller.Up);
        B(cfg.Down, Controller.Down);
        B(cfg.Left, Controller.Left);
        B(cfg.Right, Controller.Right);

        return s;
    }

    private static bool HasAnyKey(KeyBindings cfg)
    {
        return cfg.Cross.Length > 0 || cfg.Circle.Length > 0 || cfg.Square.Length > 0 || cfg.Triangle.Length > 0 ||
               cfg.L1.Length > 0 || cfg.R1.Length > 0 || cfg.L2.Length > 0 || cfg.R2.Length > 0 ||
               cfg.L3.Length > 0 || cfg.R3.Length > 0 || cfg.Start.Length > 0 || cfg.Select.Length > 0 ||
               cfg.Up.Length > 0 || cfg.Down.Length > 0 || cfg.Left.Length > 0 || cfg.Right.Length > 0;
    }

    private static void PollGamepads()
    {
        if (_sdl == null) return;

        if (_pad0 != null)
        {
            var bind = ConfigManager.Game.PadFor(0);
            Controller.State = PadState(_pad0, bind, Controller.State);
            Controller.LeftX = Axis(_pad0, bind.LeftStickX);
            Controller.LeftY = Axis(_pad0, bind.LeftStickY);
            Controller.RightX = Axis(_pad0, bind.RightStickX);
            Controller.RightY = Axis(_pad0, bind.RightStickY);
        }

        if (_pad1 != null)
        {
            var bind = ConfigManager.Game.PadFor(1);
            Controller.State2 = PadState(_pad1, bind, Controller.State2);
            Controller.LeftX2 = Axis(_pad1, bind.LeftStickX);
            Controller.LeftY2 = Axis(_pad1, bind.LeftStickY);
            Controller.RightX2 = Axis(_pad1, bind.RightStickX);
            Controller.RightY2 = Axis(_pad1, bind.RightStickY);
        }
        else
        {
            Controller.LeftX2 = Controller.LeftY2 = Controller.RightX2 = Controller.RightY2 = 0x80;
        }
    }

    private static byte Axis(GameController* ctrl, int index)
    {
        if (_sdl == null || index < 0) return 0x80;
        return AxisToByte(_sdl.GameControllerGetAxis(ctrl, (GameControllerAxis)index));
    }

    private static ushort PadState(GameController* ctrl, GamepadBindings pad, ushort s)
    {
        s = Apply(ctrl, pad.Cross, Controller.Cross, s);
        s = Apply(ctrl, pad.Circle, Controller.Circle, s);
        s = Apply(ctrl, pad.Square, Controller.Square, s);
        s = Apply(ctrl, pad.Triangle, Controller.Triangle, s);
        s = Apply(ctrl, pad.L1, Controller.L1, s);
        s = Apply(ctrl, pad.R1, Controller.R1, s);
        s = Apply(ctrl, pad.L2, Controller.L2, s);
        s = Apply(ctrl, pad.R2, Controller.R2, s);
        s = Apply(ctrl, pad.L3, Controller.L3, s);
        s = Apply(ctrl, pad.R3, Controller.R3, s);
        s = Apply(ctrl, pad.Start, Controller.Start, s);
        s = Apply(ctrl, pad.Select, Controller.Select, s);
        s = Apply(ctrl, pad.Up, Controller.Up, s);
        s = Apply(ctrl, pad.Down, Controller.Down, s);
        s = Apply(ctrl, pad.Left, Controller.Left, s);
        s = Apply(ctrl, pad.Right, Controller.Right, s);
        return s;
    }

    private static ushort Apply(GameController* ctrl, int[] bindings, ushort bit, ushort s)
    {
        foreach (var binding in bindings)
            if (Pressed(ctrl, binding))
                return (ushort)(s & ~bit);
        return s;
    }

    private static bool Pressed(GameController* ctrl, int binding)
    {
        if (_sdl == null) return false;
        if (binding == LeftTrigger)
            return _sdl.GameControllerGetAxis(ctrl, GameControllerAxis.Triggerleft) > AxisThreshold;
        if (binding == RightTrigger)
            return _sdl.GameControllerGetAxis(ctrl, GameControllerAxis.Triggerright) > AxisThreshold;
        if (IsStickBinding(binding))
        {
            var (axis, positive) = AxisBinding(binding);
            var v = _sdl.GameControllerGetAxis(ctrl, axis);
            return positive ? v > StickThreshold : v < -StickThreshold;
        }

        return _sdl.GameControllerGetButton(ctrl, (GameControllerButton)binding) != 0;
    }

    private static byte AxisToByte(short axis)
    {
        var f = Math.Clamp(axis * 1.3f / 32768.0f, -1.0f, 1.0f);
        return (byte)Math.Clamp((int)MathF.Round((f + 1.0f) * 127.5f), 0, 255);
    }

    public static void SetRumble(byte large, byte small)
    {
        if (_sdl == null || _pad0 == null) return;
        var lo = (ushort)(large * 257);
        var hi = small != 0 ? (ushort)65535 : (ushort)0;
        var duration = large == 0 && small == 0 ? 0u : 500u;
        _sdl.GameControllerRumble(_pad0, lo, hi, duration);
    }

    private static void OnKeyDown(IKeyboard kb, Key key, int _)
    {
        if (key == Key.F1) _topBarToggle = true;
        if (key == Key.F11) _fullscreenToggle = true;

        if (EventBus.HasAnyListeners<KeyboardEvent>())
            EventBus.Dispatch(new KeyboardEvent
            {
                Key = (int)key,
                Pressed = true
            });
    }

    private static void OnKeyUp(IKeyboard kb, Key key, int _)
    {
        if (EventBus.HasAnyListeners<KeyboardEvent>())
            EventBus.Dispatch(new KeyboardEvent
            {
                Key = (int)key,
                Pressed = false
            });
    }

    private static void OnMouseMove(IMouse mouse, Vector2 position)
    {
        if (EventBus.HasAnyListeners<MouseEvent>())
            EventBus.Dispatch(new MouseEvent
            {
                Action = MouseAction.Move,
                X = (int)position.X,
                Y = (int)position.Y
            });
    }

    private static void OnMouseDown(IMouse mouse, MouseButton mouseButton)
    {
        if (EventBus.HasAnyListeners<MouseEvent>())
            EventBus.Dispatch(new MouseEvent
            {
                Action = MouseAction.Button,
                Button = MapMouseButton(mouseButton),
                Pressed = true,
                X = (int)mouse.Position.X,
                Y = (int)mouse.Position.Y
            });
    }

    private static void OnMouseUp(IMouse mouse, MouseButton mouseButton)
    {
        if (EventBus.HasAnyListeners<MouseEvent>())
            EventBus.Dispatch(new MouseEvent
            {
                Action = MouseAction.Button,
                Button = MapMouseButton(mouseButton),
                Pressed = false,
                X = (int)mouse.Position.X,
                Y = (int)mouse.Position.Y
            });
    }

    private static void OnScroll(IMouse mouse, ScrollWheel wheel)
    {
        if (EventBus.HasAnyListeners<MouseEvent>())
            EventBus.Dispatch(new MouseEvent
            {
                Action = MouseAction.Wheel,
                Wheel = (int)wheel.Y,
                X = (int)mouse.Position.X,
                Y = (int)mouse.Position.Y
            });
    }

    private static EvMouseButton MapMouseButton(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => EvMouseButton.Left,
            MouseButton.Right => EvMouseButton.Right,
            MouseButton.Middle => EvMouseButton.Middle,
            _ => EvMouseButton.None
        };
    }
}