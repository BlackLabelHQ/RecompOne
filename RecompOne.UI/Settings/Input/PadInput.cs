namespace RecompOne.UI;

public enum PadInput
{
    Cross,
    Circle,
    Square,
    Triangle,
    L1,
    R1,
    L2,
    R2,
    L3,
    R3,
    Start,
    Select,
    Up,
    Down,
    Left,
    Right
}

public static class PadInputs
{
    public static readonly PadInput[] All = Enum.GetValues<PadInput>();
    
    public static string Label(PadInput input) => input.ToString();
    
    public static ushort Mask(PadInput input) => input switch
    {
        PadInput.Cross => Controller.Cross,
        PadInput.Circle => Controller.Circle,
        PadInput.Square => Controller.Square,
        PadInput.Triangle => Controller.Triangle,
        PadInput.L1 => Controller.L1,
        PadInput.R1 => Controller.R1,
        PadInput.L2 => Controller.L2,
        PadInput.R2 => Controller.R2,
        PadInput.L3 => Controller.L3,
        PadInput.R3 => Controller.R3,
        PadInput.Start => Controller.Start,
        PadInput.Select => Controller.Select,
        PadInput.Up => Controller.Up,
        PadInput.Down => Controller.Down,
        PadInput.Left => Controller.Left,
        _ => Controller.Right
    };
    
    public static string GetKey(KeyBindings keys, PadInput input) => input switch
    {
        PadInput.Cross => keys.Cross,
        PadInput.Circle => keys.Circle,
        PadInput.Square => keys.Square,
        PadInput.Triangle => keys.Triangle,
        PadInput.L1 => keys.L1,
        PadInput.R1 => keys.R1,
        PadInput.L2 => keys.L2,
        PadInput.R2 => keys.R2,
        PadInput.L3 => keys.L3,
        PadInput.R3 => keys.R3,
        PadInput.Start => keys.Start,
        PadInput.Select => keys.Select,
        PadInput.Up => keys.Up,
        PadInput.Down => keys.Down,
        PadInput.Left => keys.Left,
        _ => keys.Right
    };
    
    public static void SetKey(KeyBindings keys, PadInput input, string value)
    {
        switch (input)
        {
            case PadInput.Cross: keys.Cross = value; break;
            case PadInput.Circle: keys.Circle = value; break;
            case PadInput.Square: keys.Square = value; break;
            case PadInput.Triangle: keys.Triangle = value; break;
            case PadInput.L1: keys.L1 = value; break;
            case PadInput.R1: keys.R1 = value; break;
            case PadInput.L2: keys.L2 = value; break;
            case PadInput.R2: keys.R2 = value; break;
            case PadInput.L3: keys.L3 = value; break;
            case PadInput.R3: keys.R3 = value; break;
            case PadInput.Start: keys.Start = value; break;
            case PadInput.Select: keys.Select = value; break;
            case PadInput.Up: keys.Up = value; break;
            case PadInput.Down: keys.Down = value; break;
            case PadInput.Left: keys.Left = value; break;
            default: keys.Right = value; break;
        }
    }
    
    public static int[] GetPad(GamepadBindings pad, PadInput input) => input switch
    {
        PadInput.Cross => pad.Cross,
        PadInput.Circle => pad.Circle,
        PadInput.Square => pad.Square,
        PadInput.Triangle => pad.Triangle,
        PadInput.L1 => pad.L1,
        PadInput.R1 => pad.R1,
        PadInput.L2 => pad.L2,
        PadInput.R2 => pad.R2,
        PadInput.L3 => pad.L3,
        PadInput.R3 => pad.R3,
        PadInput.Start => pad.Start,
        PadInput.Select => pad.Select,
        PadInput.Up => pad.Up,
        PadInput.Down => pad.Down,
        PadInput.Left => pad.Left,
        _ => pad.Right
    };
    
    public static void SetPad(GamepadBindings pad, PadInput input, int[] value)
    {
        switch (input)
        {
            case PadInput.Cross: pad.Cross = value; break;
            case PadInput.Circle: pad.Circle = value; break;
            case PadInput.Square: pad.Square = value; break;
            case PadInput.Triangle: pad.Triangle = value; break;
            case PadInput.L1: pad.L1 = value; break;
            case PadInput.R1: pad.R1 = value; break;
            case PadInput.L2: pad.L2 = value; break;
            case PadInput.R2: pad.R2 = value; break;
            case PadInput.L3: pad.L3 = value; break;
            case PadInput.R3: pad.R3 = value; break;
            case PadInput.Start: pad.Start = value; break;
            case PadInput.Select: pad.Select = value; break;
            case PadInput.Up: pad.Up = value; break;
            case PadInput.Down: pad.Down = value; break;
            case PadInput.Left: pad.Left = value; break;
            default: pad.Right = value; break;
        }
    }
}
