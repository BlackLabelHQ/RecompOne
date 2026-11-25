namespace RecompOne.UI;

public static class PadSourceLabels
{
    public const int South = 0;
    public const int East = 1;
    public const int West = 2;
    public const int North = 3;
    public const int Guide = 5;
    
    private static readonly Dictionary<int, string> _xbox = new()
    {
        [0] = "A", [1] = "B", [2] = "X", [3] = "Y", [4] = "Back", [5] = "Guide", [6] = "Start",
        [7] = "LS", [8] = "RS", [9] = "LB", [10] = "RB", [100] = "LT", [101] = "RT"
    };
    
    private static readonly Dictionary<int, string> _playStation = new()
    {
        [0] = "Cross", [1] = "Circle", [2] = "Square", [3] = "Triangle", [4] = "Select", [5] = "PS", [6] = "Start",
        [7] = "L3", [8] = "R3", [9] = "L1", [10] = "R1", [100] = "L2", [101] = "R2"
    };
    
    private static readonly Dictionary<int, string> _nintendo = new()
    {
        [0] = "B", [1] = "A", [2] = "Y", [3] = "X", [4] = "-", [5] = "Home", [6] = "+",
        [7] = "LS", [8] = "RS", [9] = "L", [10] = "R", [100] = "ZL", [101] = "ZR"
    };
    
    private static readonly Dictionary<int, string> _shared = new()
    {
        [11] = "D-Up", [12] = "D-Down", [13] = "D-Left", [14] = "D-Right",
        [102] = "LS Left", [103] = "LS Right", [104] = "LS Up", [105] = "LS Down",
        [106] = "RS Left", [107] = "RS Right", [108] = "RS Up", [109] = "RS Down"
    };
    
    public static string Name(int code)
    {
        var table = InputManager.MenuPadStyle switch
        {
            PadStyle.PlayStation or PadStyle.PlayStation5 => _playStation,
            PadStyle.Nintendo => _nintendo,
            _ => _xbox
        };
        
        if (table.TryGetValue(code, out var name)) return name;
        return _shared.TryGetValue(code, out name) ? name : $"Btn {code}";
    }
}
