namespace RecompOne.UI.BigPicture.Input;

public class MenuInput
{
    private bool _menuHeld;
    
    public InputDevice Device { get; private set; }
    
    public bool UsesMouse => Device == InputDevice.Mouse;
    
    public bool UsesGamepad => Device == InputDevice.Gamepad;
    
    public virtual bool Up => Pressed(ImGuiKey.UpArrow, ImGuiKey.GamepadDpadUp, ImGuiKey.GamepadLStickUp);
    
    public virtual bool Down => Pressed(ImGuiKey.DownArrow, ImGuiKey.GamepadDpadDown, ImGuiKey.GamepadLStickDown);
    
    public virtual bool PreviousTab => ImGui.IsKeyPressed(ImGuiKey.GamepadL1);
    
    public virtual bool NextTab => ImGui.IsKeyPressed(ImGuiKey.GamepadR1);
    
    public virtual bool Accept => ImGui.IsKeyPressed(ImGuiKey.Enter, false) || ImGui.IsKeyPressed(ImGuiKey.Space, false) ||
                                  ImGui.IsKeyPressed(ImGuiKey.GamepadFaceDown, false);
    
    public virtual bool PadBack => ImGui.IsKeyPressed(ImGuiKey.GamepadFaceRight, false);
    
    public virtual bool KeyBack => ImGui.IsKeyPressed(ImGuiKey.Escape, false);
    
    public bool WidgetBusy =>
        ImGui.IsAnyItemActive() || ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel);
    
    public bool ItemFocused => !UsesMouse && ImGui.IsItemFocused() && ImGui.IsAnyItemFocused();
    
    public bool ItemHovered => UsesMouse && ImGui.IsItemHovered();
    
    //needs a started frame
    public void Track(in MenuPadState pad)
    {
        if (PadUsed(pad)) Device = InputDevice.Gamepad;
        else if (KeyboardUsed()) Device = InputDevice.Keyboard;
        else if (MouseUsed()) Device = InputDevice.Mouse;
    }
    
    public void ShowCursor()
    {
        if (UsesGamepad) ImGui.SetNavCursorVisible(true);
    }
    
    public void PrimeSelection()
    {
        if (!UsesGamepad) ImGui.SetNavCursorVisible(false);
        else if (!ImGui.IsAnyItemFocused()) ImGui.GetIO().AddKeyEvent(ImGuiKey.GamepadDpadDown, true);
    }
    
    public virtual bool MenuHeld(in MenuPadState pad) => pad.Guide || (pad.LeftStick && pad.RightStick);
    
    public virtual bool Released(in MenuPadState pad) => !pad.Accept && !pad.Back && !pad.LeftStick && !pad.RightStick && !pad.Guide;
    
    public bool MenuPressed(in MenuPadState pad)
    {
        var held = MenuHeld(pad);
        var pressed = held && !_menuHeld;
        _menuHeld = held;
        return pressed;
    }
    
    private static bool PadUsed(in MenuPadState pad)
    {
        return pad with { LeftX = 0f, LeftY = 0f } != default ||
               MathF.Abs(pad.LeftX) > PadFeed.StickDeadzone || MathF.Abs(pad.LeftY) > PadFeed.StickDeadzone;
    }
    
    private static bool KeyboardUsed()
    {
        for (var key = ImGuiKey.Tab; key < ImGuiKey.GamepadStart; key++)
            if (ImGui.IsKeyPressed(key, false))
                return true;
        return false;
    }
    
    private static bool MouseUsed()
    {
        var io = ImGui.GetIO();
        return io.MouseDelta != Vector2.Zero || io.MouseWheel != 0f || ImGui.IsAnyMouseDown();
    }
    
    private static bool Pressed(ImGuiKey key, ImGuiKey pad, ImGuiKey stick)
    {
        return ImGui.IsKeyPressed(key) || ImGui.IsKeyPressed(pad) || ImGui.IsKeyPressed(stick);
    }
}
