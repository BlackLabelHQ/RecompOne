namespace RecompOne.Runtime.Pgxp;

public static class PgxpGpu
{
    private const int CacheDim = 0x400 * 2;
    private const int CacheOrigin = 0x400;
    private const int CacheSize = CacheDim * CacheDim;
    
    private const uint TagAmbiguous = 1u;
    
    private const uint ModeInit = 0;
    private const uint ModeWrite = 1;
    private const uint ModeRead = 2;
    private const uint ModeFail = 3;
    
    private struct Cell
    {
        public float X;
        public float Y;
        public float Z;
        public uint Tag;
    }
    
    private static Cell[]? _cache;
    private static uint _generation;
    private static uint _mode = ModeInit;
    
    public static void Init()
    {
        _cache = null;
        _generation = 0;
        _mode = ModeInit;
    }
    public static void Free()
    {
        _cache = null;
        _mode = ModeInit;
    }
    
    private static bool EnsureAllocated()
    {
        if (_cache != null) return true;
        
        try
        {
            _cache = new Cell[CacheSize];
        }
        catch (OutOfMemoryException)
        {
            return false;
        }
        
        return true;
    }

    public static void CacheVertex(int sx, int sy, in PgxpValue value)
    {
        if (_mode != ModeWrite)
        {
            if (!EnsureAllocated())
            {
                _mode = ModeFail;
                return;
            }
            
            if (_generation < 0x7FFFFFFFu) _generation++;
            _mode = ModeWrite;
        }

        if (!TrySlot(sx, sy, out var index)) return;
        
        ref var cell = ref _cache![index];
        
        if (Generation(cell.Tag) == _generation)
        {
            if (cell.X != value.X || cell.Y != value.Y || cell.Z != value.Z) cell.Tag |= TagAmbiguous;
            return;
        }
        
        cell.X = value.X;
        cell.Y = value.Y;
        cell.Z = value.Z;
        cell.Tag = _generation << 1;
    }
    
    public static bool TryGetVertex(uint packed, out float x, out float y, out float w, out bool validW)
    {
        validW = true;
        if (TryMatch(in PgxpGte.Sxy2, packed, out x, out y, out w)) return true;
        if (TryMatch(in PgxpGte.Sxy1, packed, out x, out y, out w)) return true;
        if (TryMatch(in PgxpGte.Sxy0, packed, out x, out y, out w)) return true;

        validW = false;
        return Pgxp.VertexCache && TryCache(packed, out x, out y, out w);
    }
    private static bool TryMatch(in PgxpValue value, uint packed, out float x, out float y, out float w)
    {
        x = 0f;
        y = 0f;
        w = 1f;
        
        if (!PgxpFlags.Matches(in value, packed)) return false;
        
        x = value.X;
        y = value.Y;
        w = value.Z;
        return true;
    }
    
    private static bool TryCache(uint packed, out float x, out float y, out float w)
    {
        x = 0f;
        y = 0f;
        w = 1f;
        
        if (_mode == ModeFail || _cache == null) return false;
        if (_mode != ModeRead) _mode = ModeRead;
        if (!TrySlot((short)(packed & 0xFFFF), (short)(packed >> 16), out var index)) return false;
        
        ref var cell = ref _cache[index];
        if (cell.Tag == 0) return false;
        if (Generation(cell.Tag) != _generation) return false;
        if ((cell.Tag & TagAmbiguous) != 0) return false;
        
        x = cell.X;
        y = cell.Y;
        w = cell.Z;
        return true;
    }
    
    private static bool TrySlot(int sx, int sy, out int index)
    {
        index = 0;
        if (sx < -CacheOrigin || sx >= CacheOrigin || sy < -CacheOrigin || sy >= CacheOrigin) return false;
        
        index = (sy + CacheOrigin) * CacheDim + (sx + CacheOrigin);
        return true;
    }
    
    private static uint Generation(uint tag)
    {
        return tag >> 1;
    }
}
