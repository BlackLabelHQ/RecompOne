namespace RecompOne.UI.BigPicture.Input;

public static class PadFeed
{
    public const float StickThreshold = 0.5f;
    public const float StickDeadzone = 0.2f;
    
    public static void Feed(in MenuPadState pad, bool active) //kek
    {
        var io = ImGui.GetIO();
        io.AddKeyEvent(ImGuiKey.GamepadDpadUp, active && pad.Up);
        io.AddKeyEvent(ImGuiKey.GamepadDpadDown, active && pad.Down);
        io.AddKeyEvent(ImGuiKey.GamepadDpadLeft, active && pad.Left);
        io.AddKeyEvent(ImGuiKey.GamepadDpadRight, active && pad.Right);
        io.AddKeyEvent(ImGuiKey.GamepadFaceDown, active && pad.Accept);
        io.AddKeyEvent(ImGuiKey.GamepadFaceRight, active && pad.Back);
        io.AddKeyEvent(ImGuiKey.GamepadFaceLeft, active && pad.Alternate);
        io.AddKeyEvent(ImGuiKey.GamepadFaceUp, active && pad.Extra);
        io.AddKeyEvent(ImGuiKey.GamepadL1, active && pad.LeftShoulder);
        io.AddKeyEvent(ImGuiKey.GamepadR1, active && pad.RightShoulder);
        
        Stick(io, ImGuiKey.GamepadLStickLeft, active ? -pad.LeftX : 0f);
        Stick(io, ImGuiKey.GamepadLStickRight, active ? pad.LeftX : 0f);
        Stick(io, ImGuiKey.GamepadLStickUp, active ? -pad.LeftY : 0f);
        Stick(io, ImGuiKey.GamepadLStickDown, active ? pad.LeftY : 0f);
    }
    
    public static void Release()
    {
        Feed(default, false);
    }
    
    private static void Stick(ImGuiIOPtr io, ImGuiKey key, float value)
    {
        var amount = value > StickDeadzone ? value : 0f;
        io.AddKeyAnalogEvent(key, amount > StickThreshold, amount);
    }
}
