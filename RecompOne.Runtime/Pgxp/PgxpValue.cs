using System.Runtime.CompilerServices;

namespace RecompOne.Runtime.Pgxp;

public struct PgxpValue
{
    public float X;
    public float Y;
    public float Z;
    public uint Value;
    public uint Flags;
    public uint Count;
}

public static class PgxpFlags
{
    public const uint None = 0u;
    public const uint Valid0 = 1u << 0;
    public const uint Valid1 = 1u << 8;
    public const uint Valid2 = 1u << 16;
    public const uint Valid3 = 1u << 24;
    public const uint ValidLow = Valid0 | Valid1;
    public const uint ValidAll = Valid0 | Valid1 | Valid2 | Valid3;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Matches(in PgxpValue value, uint architectural)
    {
        return value.Value == architectural && (value.Flags & ValidLow) == ValidLow;
    }
}