using System.Numerics;
using Silk.NET.Input;

namespace RecompOne.UI;

public sealed class BindingCapturePopup : Popup
{
    private const double Timeout = 5.0;
    
    private static readonly Key[] _keys = Enum.GetValues<Key>().Where(static k => k is not (Key.Unknown or Key.Menu)).Distinct().ToArray();
    
    private int _port;
    private PadInput _input;
    private bool _add;
    private bool _armed;
    private bool _done;
    private double _deadline;
    
    protected override string TitleKey => "settings.input.button";
    protected override Vector2 Size => new(400f, 0f);
    protected override bool Closable => false;
    
    public static void Begin(int port, PadInput input, bool add)
    {
        var popup = PopupManager.Get<BindingCapturePopup>();
        if (popup == null || popup.IsOpen) return;
        popup._port = port;
        popup._input = input;
        popup._add = add;
        popup.Open();
    }
    
    protected override void OnOpened()
    {
        _armed = false;
        _done = false;
    }
    
    protected override void DrawContent()
    {
        var remaining = _armed ? Math.Max(0.0, _deadline - ImGui.GetTime()) : Timeout;
        
        UiText.CenteredWrapped(PadInputs.Label(_input));
        ImGui.Spacing();
        UiText.CenteredWrapped(Localization.T(_add ? "settings.input.capture_add" : "settings.input.capture"));
        ImGui.Spacing();
        using (ImGuiUtils.Color(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
            UiText.CenteredWrapped(Localization.T("settings.input.capture_cancel", (int)Math.Ceiling(remaining)));
        
        Step();
    }
    
    private void Step()
    {
        var key = PressedKey();
        var button = InputManager.GetFirstPressedPadButton(_port);
        var idle = key == null && button == null && !ImGui.IsAnyMouseDown() && MenuReleased();
        
        if (_done)
        {
            if (idle) Close();
            return;
        }
        
        if (!_armed)
        {
            if (!idle) return;
            _armed = true;
            _deadline = ImGui.GetTime() + Timeout;
            return;
        }
        
        if (key == Key.Escape || ImGui.IsAnyMouseDown() || ImGui.GetTime() >= _deadline)
        {
            _done = true;
            return;
        }
        
        var port = Settings.System.Input[_port];
        if (key is { } pressed)
        {
            if (!_add) PadInputs.SetKey(port.Keys, _input, pressed.ToString());
            else return;
        }
        else if (button is { } code && code != PadSourceLabels.Guide)
        {
            var current = PadInputs.GetPad(port.Gamepad, _input);
            if (!_add) PadInputs.SetPad(port.Gamepad, _input, [code]);
            else if (!current.Contains(code)) PadInputs.SetPad(port.Gamepad, _input, [.. current, code]);
        }
        else
        {
            return;
        }
        
        Settings.MarkDirty();
        _done = true;
    }
    
    private static bool MenuReleased()
    {
        var pad = InputManager.MenuPad;
        return pad with { LeftX = 0f, LeftY = 0f } == default;
    }
    
    private static Key? PressedKey()
    {
        foreach (var key in _keys)
            if (InputManager.IsKeyDown(key))
                return key;
        return null;
    }
}
