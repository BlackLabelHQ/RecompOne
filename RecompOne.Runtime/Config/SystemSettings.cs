using System.Text.Json.Serialization;

namespace RecompOne.Runtime.Config;

public sealed class SystemSettings
{
    public string DiscPath { get; set; } = "";
    public MemoryCardSettings CardA { get; set; } = new() { Path = "carda.sav" };
    public MemoryCardSettings CardB { get; set; } = new() { Path = "cardb.sav" };
    public AudioSettings Audio { get; set; } = new();
    public InputSettings Input { get; set; } = new();

    internal void Normalize()
    {
        DiscPath ??= "";
        CardA ??= new MemoryCardSettings { Path = "carda.sav" };
        CardB ??= new MemoryCardSettings { Path = "cardb.sav" };
        if (string.IsNullOrWhiteSpace(CardA.Path)) CardA.Path = "carda.sav";
        if (string.IsNullOrWhiteSpace(CardB.Path)) CardB.Path = "cardb.sav";
        Audio ??= new AudioSettings();
        Input ??= new InputSettings();
        Input.Normalize();
    }
}

public sealed class MemoryCardSettings
{
    public string Path { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class AudioSettings
{
    public float Master { get; set; } = 0.5f;
    public float Spu { get; set; } = 1.0f;
    public float Xa { get; set; } = 1.0f;
    public bool Muted { get; set; }

    [JsonIgnore] public float EffectiveMaster => Muted ? 0f : Master;
}

public sealed class InputSettings
{
    public const int PortCount = 2;

    public PortSettings[] Ports { get; set; } = [PortSettings.Primary(), PortSettings.Secondary()];

    public PortSettings this[int port] => Ports[port];

    internal void Normalize()
    {
        var ports = Ports ?? [];
        if (ports.Length >= PortCount && ports.All(p => p != null)) return;
        var fixedPorts = new PortSettings[PortCount];
        for (var i = 0; i < PortCount; i++)
            fixedPorts[i] = i < ports.Length && ports[i] != null ? ports[i] : PortSettings.DefaultFor(i);
        Ports = fixedPorts;
    }
}

public enum PadKind
{
    Digital,
    Analog
}

public sealed class PortSettings
{
    public PadKind Kind { get; set; } = PadKind.Digital;
    public string Device { get; set; } = "";
    public KeyBindings Keys { get; set; } = KeyBindings.Empty();
    public GamepadBindings Digital { get; set; } = GamepadBindings.Empty();
    public GamepadBindings Analog { get; set; } = GamepadBindings.Empty();

    [JsonIgnore] public GamepadBindings Gamepad => Kind == PadKind.Analog ? Analog : Digital;

    public static PadKind PrimaryKind { get; set; } = PadKind.Analog;

    public static PortSettings Primary()
    {
        return new PortSettings
        {
            Kind = PrimaryKind,
            Keys = new KeyBindings(),
            Digital = new GamepadBindings(),
            Analog = GamepadBindings.DefaultAnalog()
        };
    }

    public static PortSettings Secondary()
    {
        return new PortSettings();
    }

    public static PortSettings DefaultFor(int port)
    {
        return port == 0 ? Primary() : Secondary();
    }

    public void ResetKeys(int port)
    {
        Keys = DefaultFor(port).Keys;
    }

    public void ResetGamepad(int port)
    {
        var defaults = DefaultFor(port);
        if (Kind == PadKind.Analog) Analog = defaults.Analog;
        else Digital = defaults.Digital;
    }
}

public sealed class KeyBindings
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

    [JsonIgnore]
    public bool Any => Cross.Length > 0 || Circle.Length > 0 || Square.Length > 0 || Triangle.Length > 0 ||
                       L1.Length > 0 || R1.Length > 0 || L2.Length > 0 || R2.Length > 0 ||
                       L3.Length > 0 || R3.Length > 0 || Start.Length > 0 || Select.Length > 0 ||
                       Up.Length > 0 || Down.Length > 0 || Left.Length > 0 || Right.Length > 0;

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

public sealed class GamepadBindings
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
