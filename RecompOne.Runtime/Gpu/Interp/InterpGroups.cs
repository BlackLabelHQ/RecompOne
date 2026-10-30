namespace RecompOne.Runtime;

public enum TransformKind : byte
{
    Auto,
    Explicit,
    Ignored
}

public static class InterpGroups
{
    public static void Push(uint id) => Gte.PushGroup(id);

    public static void Push(uint id, uint part) => Gte.PushGroup(Combine(id, part));

    public static void PushIgnore() => Gte.PushGroup(0u, true);

    public static void Part(uint part) => Gte.SetGroupPart(part);

    public static void Pop() => Gte.PopGroup();

    public static uint Combine(uint id, uint part)
    {
        var hash = id * 0x9E3779B1u;
        hash ^= part + 0x7F4A7C15u + (hash << 6) + (hash >> 2);
        return hash;
    }
}
