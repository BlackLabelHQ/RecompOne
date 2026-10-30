using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime;

public static class InterpCamera
{
    public static void Set(ReadOnlySpan<short> rotation, ReadOnlySpan<int> translation) => Gte.SetCamera(rotation, translation);

    public static void Set(IMemory m, uint matrix)
    {
        Span<short> rotation = stackalloc short[9];
        Span<int> translation = stackalloc int[3];
        Read(m, matrix, rotation, translation);
        Gte.SetCamera(rotation, translation);
    }

    public static void Clear() => Gte.ClearCamera();

    public static void Push(ReadOnlySpan<short> rotation, ReadOnlySpan<int> translation) => Gte.PushCamera(rotation, translation);

    public static void Push(IMemory m, uint matrix)
    {
        Span<short> rotation = stackalloc short[9];
        Span<int> translation = stackalloc int[3];
        Read(m, matrix, rotation, translation);
        Gte.PushCamera(rotation, translation);
    }

    public static void Pop() => Gte.PopCamera();

    private static void Read(IMemory m, uint matrix, Span<short> rotation, Span<int> translation)
    {
        for (var i = 0; i < 9; i++) rotation[i] = (short)m.ReadU16(matrix + (uint)i * 2u);
        for (var i = 0; i < 3; i++) translation[i] = (int)m.ReadU32(matrix + 0x14u + (uint)i * 4u);
    }
}
