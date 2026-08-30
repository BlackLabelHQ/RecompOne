namespace RecompOne.Runtime.Config;

public class KeyBindings
{
    public string Cross { get; set; } = "Z";
    public string Circle { get; set; } = "X";
    public string Square { get; set; } = "A";
    public string Triangle { get; set; } = "S";
    public string L1 { get; set; } = "Q";
    public string R1 { get; set; } = "W";
    public string L2 { get; set; } = "E";
    public string R2 { get; set; } = "R";
    public string L3 { get; set; } = "F";
    public string R3 { get; set; } = "G";
    public string Start { get; set; } = "Enter";
    public string Select { get; set; } = "ShiftRight";
    public string Up { get; set; } = "Up";
    public string Down { get; set; } = "Down";
    public string Left { get; set; } = "Left";
    public string Right { get; set; } = "Right";

    public static KeyBindings Empty()
    {
        return new KeyBindings
        {
            Cross = "", Circle = "", Square = "", Triangle = "",
            L1 = "", R1 = "", L2 = "", R2 = "",
            L3 = "", R3 = "", Start = "", Select = "",
            Up = "", Down = "", Left = "", Right = ""
        };
    }
}

public enum PadKind
{
    Digital,
    Analog
}

public class GamepadBindings
{
    public int[] Cross { get; set; } = [0];
    public int[] Circle { get; set; } = [1];
    public int[] Square { get; set; } = [2];
    public int[] Triangle { get; set; } = [3];
    public int[] L1 { get; set; } = [9];
    public int[] R1 { get; set; } = [10];
    public int[] L2 { get; set; } = [100];
    public int[] R2 { get; set; } = [101];
    public int[] L3 { get; set; } = [7];
    public int[] R3 { get; set; } = [8];
    public int[] Start { get; set; } = [6];
    public int[] Select { get; set; } = [4];
    public int[] Up { get; set; } = [11, 104];
    public int[] Down { get; set; } = [12, 105];
    public int[] Left { get; set; } = [13, 102];
    public int[] Right { get; set; } = [14, 103];

    public int LeftStickX { get; set; } = 0;
    public int LeftStickY { get; set; } = 1;
    public int RightStickX { get; set; } = 2;
    public int RightStickY { get; set; } = 3;

    public static GamepadBindings DefaultAnalog()
    {
        return new GamepadBindings
        {
            Up = [11], Down = [12], Left = [13], Right = [14]
        };
    }

    public static GamepadBindings Empty()
    {
        return new GamepadBindings
        {
            Cross = [], Circle = [], Square = [], Triangle = [],
            L1 = [], R1 = [], L2 = [], R2 = [],
            L3 = [], R3 = [], Start = [], Select = [],
            Up = [], Down = [], Left = [], Right = []
        };
    }
}

public class GameConfig
{
    public string CdPath { get; set; } = "";
    public string CardAPath { get; set; } = "carda.sav";
    public string CardBPath { get; set; } = "cardb.sav";
    public bool CardAEnabled { get; set; } = true;
    public bool CardBEnabled { get; set; } = true;
    public float MasterVolume { get; set; } = 0.5f; // We don't wanna bust your ear drums out on initial run <3
    public float SpuVolume { get; set; } = 1.0f;
    public float XaVolume { get; set; } = 1.0f;
    public bool Muted { get; set; } = false;
    public KeyBindings Keys { get; set; } = new();
    public KeyBindings Keys2 { get; set; } = KeyBindings.Empty();
    public GamepadBindings Pad { get; set; } = new();
    public GamepadBindings Pad2 { get; set; } = GamepadBindings.Empty();
    public string PadDevice { get; set; } = "";
    public string PadDevice2 { get; set; } = "";
    public GamepadBindings PadAnalog { get; set; } = GamepadBindings.DefaultAnalog();
    public GamepadBindings PadAnalog2 { get; set; } = GamepadBindings.Empty();
    public PadKind PadKind { get; set; } = PadKind.Digital;
    public PadKind PadKind2 { get; set; } = PadKind.Digital;

    public PadKind KindFor(int port)
    {
        return port == 0 ? PadKind : PadKind2;
    }

    public GamepadBindings PadFor(int port)
    {
        return port == 0
            ? (PadKind == PadKind.Analog ? PadAnalog : Pad)
            : (PadKind2 == PadKind.Analog ? PadAnalog2 : Pad2);
    }

    public List<string> ActiveMods { get; set; } = [];
}