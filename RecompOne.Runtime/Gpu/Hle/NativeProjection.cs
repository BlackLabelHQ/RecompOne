using System.Numerics;

namespace RecompOne.Runtime.Hle;

public static class NativeProjection
{
    private const uint ScratchpadBase = 0x1F800000u;
    private const uint ScratchpadSize = 0x400u;
    private const uint StackWindow = 0x1000u;

    private static readonly NativeVertex[] Fifo = new NativeVertex[3];
    private static readonly short[] Rotation = new short[9];
    private static readonly int[] Translation = new int[3];
    private static readonly int[] View = new int[3];
    private static int _serial = -1;
    private static NativeVertex _first;
    private static uint _firstWord;
    private static bool _firstValid;

    public static bool Enabled { get; set; } = true;

    public static bool Active => Enabled && NativeGeometry.Enabled;

    public static void Reset()
    {
        Array.Clear(Fifo);
        _serial = -1;
        _first = default;
        _firstWord = 0;
        _firstValid = false;
    }

    public static void Rtpt(int shift, bool lm)
    {
        _firstValid = false;
        if (Active && shift == 12)
        {
            Refresh();
            LoadInputs(3);
            Gte.RtptFloat();
            for (var i = 0; i < 3; i++) Fifo[i] = Project(i);
        }
        Gte.Rtpt(shift, lm);
    }

    public static void Rtps(int shift, bool lm)
    {
        if (Active && shift == 12)
        {
            Refresh();
            LoadInputs(1);
            _first = Fifo[0];
            _firstWord = Gte.Read(12);
            _firstValid = true;
            Gte.RtpsFloat();
            Fifo[0] = Fifo[1];
            Fifo[1] = Fifo[2];
            Fifo[2] = Project(2);
        }
        Gte.Rtps(shift, lm);
    }

    public static void Store(int register, uint address, uint word, bool ignoreDepth = false)
    {
        if (!Active || register is < 12 or > 14) return;

        var vertex = ignoreDepth ? Fifo[register - 12] with { IgnoreDepth = true } : Fifo[register - 12];
        NativeGeometry.Store(address, word, in vertex);
        if (IsLocal(address)) NativeGeometry.Anchor(word, in vertex);
    }

    private static bool IsLocal(uint address)
    {
        var physical = address & 0x1FFFFFFFu;
        if (physical - ScratchpadBase < ScratchpadSize) return true;
        return Runtime.Cpu is { } cpu && physical - (cpu.SP & 0x1FFFFFFFu) < StackWindow;
    }

    public static void StoreFirst(uint address, uint word)
    {
        if (Active && _firstValid && word == _firstWord)
            NativeGeometry.Store(address, word, in _first);
    }

    private static void Refresh()
    {
        var serial = Gte.TransformSerial;
        Gte.Snapshot(serial, Rotation, Translation, View);
        Span<float> rotation = stackalloc float[9];
        Span<float> translation = stackalloc float[3];
        for (var i = 0; i < 9; i++) rotation[i] = Rotation[i] / 4096f;
        for (var i = 0; i < 3; i++) translation[i] = Translation[i];
        Gte.SetFloatTransform(rotation, translation, View[0], View[1] / 65536f, View[2] / 65536f);
        _serial = serial;
    }

    private static void LoadInputs(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var xy = Gte.Read(i * 2);
            Gte.LoadVertexFloat(i, (short)xy, (short)(xy >> 16), (short)Gte.Read(i * 2 + 1));
        }
    }

    private static NativeVertex Project(int index)
    {
        var vertex = Gte.ReadFloatVertex(index);
        return new NativeVertex(
            new Vector3(vertex.X, vertex.Y, vertex.Z),
            new Vector2(vertex.ScreenX, vertex.ScreenY),
            _serial);
    }
}
