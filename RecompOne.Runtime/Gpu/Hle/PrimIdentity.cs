namespace RecompOne.Runtime.Hle;

public static class PrimIdentity
{
    private const int KeepFrames = 2;

    private struct Pair
    {
        public uint Primary;
        public uint Secondary;
        public uint Length;
        public int Seen;
    }

    private static readonly List<Pair> Pairs = [];
    private static int _frame;

    public static bool Any => Pairs.Count > 0;

    public static void Alias(uint primary, uint secondary, uint length)
    {
        primary &= 0x1FFFFFFFu;
        secondary &= 0x1FFFFFFFu;
        if (length == 0 || primary == secondary) return;

        for (var i = 0; i < Pairs.Count; i++)
        {
            var pair = Pairs[i];
            if (pair.Primary != primary || pair.Secondary != secondary) continue;
            pair.Length = length;
            pair.Seen = _frame;
            Pairs[i] = pair;
            return;
        }

        Pairs.Add(new Pair { Primary = primary, Secondary = secondary, Length = length, Seen = _frame });
    }

    public static bool TryResolve(uint address, out uint id)
    {
        address &= 0x1FFFFFFFu;
        foreach (var pair in Pairs)
        {
            if (address - pair.Primary < pair.Length)
            {
                id = address;
                return true;
            }

            if (address - pair.Secondary < pair.Length)
            {
                id = pair.Primary + (address - pair.Secondary);
                return true;
            }
        }

        id = 0;
        return false;
    }

    public static void EndFrame()
    {
        _frame++;
        Pairs.RemoveAll(pair => _frame - pair.Seen > KeepFrames);
    }

    public static void Clear()
    {
        Pairs.Clear();
    }
}
