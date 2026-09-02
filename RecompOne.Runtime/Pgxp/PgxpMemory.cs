namespace RecompOne.Runtime.Pgxp;

public static class PgxpMemory
{
    private static PgxpValue[] _shadow = [];
    private static uint _mask;
    
    public static void Init(uint ramSize)
    {
        _shadow = new PgxpValue[ramSize >> 2];
        _mask = ramSize - 4u;
    }
    
    public static void Free()
    {
        _shadow = [];
    }
    
    public static void Store(uint address, in PgxpValue value, uint written)
    {
        if (!Pgxp.MemoryTracking || _shadow.Length == 0) return;

        ref var slot = ref _shadow[Index(address)];
        slot = value;
        slot.Value = written;
    }
    
    public static void StoreHalf(uint address, in PgxpValue src, ushort written)
    {
        if (!Pgxp.MemoryTracking || _shadow.Length == 0) return;
        
        ref var slot = ref _shadow[Index(address)];
        var srcValid = (src.Flags & PgxpFlags.Valid0) != 0;
        
        if ((address & 2u) != 0)
        {
            slot.Y = src.X;
            slot.Flags = (slot.Flags & ~PgxpFlags.Valid1) | (srcValid ? PgxpFlags.Valid1 : PgxpFlags.None);
            slot.Value = (slot.Value & 0x0000FFFFu) | ((uint)written << 16);
        }
        else
        {
            slot.X = src.X;
            slot.Flags = (slot.Flags & ~PgxpFlags.Valid0) | (srcValid ? PgxpFlags.Valid0 : PgxpFlags.None);
            slot.Value = (slot.Value & 0xFFFF0000u) | written;
        }
        
        if ((src.Flags & PgxpFlags.Valid2) == 0) return;
        
        slot.Z = src.Z;
        slot.Flags |= PgxpFlags.Valid2;
    }
    
    
    public static void LoadHalf(uint address, uint value, ref PgxpValue dest)
    {
        if (_shadow.Length == 0)
        {
            dest.Flags = PgxpFlags.None;
            dest.Value = value;
            return;
        }
        
        ref var slot = ref _shadow[Index(address)];
        var high = (address & 2u) != 0;
        var valid = high ? (slot.Flags & PgxpFlags.Valid1) != 0 : (slot.Flags & PgxpFlags.Valid0) != 0;
        var stored = high ? slot.Value >> 16 : slot.Value & 0xFFFFu;
        
        dest.X = high ? slot.Y : slot.X;
        dest.Y = dest.X < 0f ? -1f : 0f;
        dest.Z = slot.Z;
        dest.Value = value;
        dest.Flags = valid && stored == (value & 0xFFFFu) ? PgxpFlags.Valid0 | PgxpFlags.Valid1 | (slot.Flags & PgxpFlags.Valid2) : PgxpFlags.None;
    }
    
    public static void LoadInto(uint address, uint value, ref PgxpValue dest)
    {
        if (_shadow.Length == 0)
        {
            dest.Flags = PgxpFlags.None;
            dest.Value = value;
            return;
        }
        
        dest = _shadow[Index(address)];
        if (dest.Value != value) dest.Flags = PgxpFlags.None;
        dest.Value = value;
    }
    public static void Invalidate(uint address, uint written)
    {
        if (!Pgxp.MemoryTracking || _shadow.Length == 0) return;

        
        ref var slot = ref _shadow[Index(address)];
        slot.Flags = PgxpFlags.None;
        slot.Value = written;
    }
    public static bool TryLoad(uint address, uint packed, out float x, out float y, out float w, out bool validW,
        out uint seq)
    {
        x = 0f;
        y = 0f;
        w = 1f;
        validW = false;
        seq = 0u;
        if (!Pgxp.MemoryTracking || _shadow.Length == 0) return false;

        ref var slot = ref _shadow[Index(address)];
        if (!PgxpFlags.Matches(in slot, packed)) return false;
        
        validW = (slot.Flags & PgxpFlags.Valid2) != 0;
        x = slot.X;
        y = slot.Y;
        w = slot.Z;
        seq = slot.Count;
        return true;
    }
    
    private static uint Index(uint address)
    {
        return (address & _mask) >> 2;
    }
}
