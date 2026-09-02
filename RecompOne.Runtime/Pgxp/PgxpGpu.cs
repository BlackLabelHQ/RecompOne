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
    
    private const uint SeqWindow = 1024u;
    private const int MaxAlternates = 3;
    
    private struct Cell
    {
        public float X;
        public float Y;
        public float Z;
        public uint Seq;
        public uint Tag;
    }
    
    private struct Alt
    {
        public uint Generation;
        public int Count;
        public float X0, Y0, Z0;
        public float X1, Y1, Z1;
        public float X2, Y2, Z2;
        public uint S0, S1, S2;
        
        public void Get(int index, out float x, out float y, out float z, out uint seq)
        {
            switch (index)
            {
                case 0: x = X0; y = Y0; z = Z0; seq = S0; return;
                case 1: x = X1; y = Y1; z = Z1; seq = S1; return;
                default: x = X2; y = Y2; z = Z2; seq = S2; return;
            }
        }
        
        public void Set(int index, float x, float y, float z, uint seq)
        {
            switch (index)
            {
                case 0: X0 = x; Y0 = y; Z0 = z; S0 = seq; return;
                case 1: X1 = x; Y1 = y; Z1 = z; S1 = seq; return;
                default: X2 = x; Y2 = y; Z2 = z; S2 = seq; return;
            }
        }
    }
    
    private static readonly Dictionary<int, Alt> _alternates = new();
    
    private static Cell[]? _cache;
    private static uint _generation;
    private static uint _mode = ModeInit;
    
    public static void Init()
    {
        _cache = null;
        _alternates.Clear();
        _generation = 0;
        _mode = ModeInit;
    }
    public static void Free()
    {
        _cache = null;
        _alternates.Clear();
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
            if (cell.X == value.X && cell.Y == value.Y && cell.Z == value.Z) return;
            
            if (!_alternates.TryGetValue(index, out var alt) || alt.Generation != _generation)
            {
                alt = new Alt { Generation = _generation, Count = 1 };
                alt.Set(0, value.X, value.Y, value.Z, value.Count);
                _alternates[index] = alt;
                return;
            }
            
            for (var i = 0; i < alt.Count; i++)
            {
                alt.Get(i, out var ax, out var ay, out var az, out _);
                if (ax == value.X && ay == value.Y && az == value.Z) return;
            }
            
            if (alt.Count >= MaxAlternates)
            {
                cell.Tag |= TagAmbiguous;
                
                return;
            }
            
            alt.Set(alt.Count, value.X, value.Y, value.Z, value.Count);
            alt.Count++;
            _alternates[index] = alt;
            return;
        }
        
        cell.X = value.X;
        cell.Y = value.Y;
        cell.Z = value.Z;
        cell.Seq = value.Count; //ad seq
        cell.Tag = _generation << 1;
    }
    
    public static bool TryGetVertex(uint packed, uint hintSeq, bool hasHint, out float x, out float y, out float w,
        out bool validW, out uint seq)
    {
        validW = true;
        if (TryMatch(in PgxpGte.Sxy2, packed, out x, out y, out w, out seq)) return true;
        if (TryMatch(in PgxpGte.Sxy1, packed, out x, out y, out w, out seq)) return true;
        if (TryMatch(in PgxpGte.Sxy0, packed, out x, out y, out w, out seq)) return true;

        if (!Pgxp.VertexCache || !TryCache(packed, hintSeq, hasHint, out x, out y, out w, out seq)) return false;
        
        validW = Pgxp.CacheW && w > 0f;
        return true;
    }
    private static bool TryMatch(in PgxpValue value, uint packed, out float x, out float y, out float w, out uint seq)
    {
        x = 0f;
        y = 0f;
        w = 1f;
        seq = 0u;
        
        if (!PgxpFlags.Matches(in value, packed)) return false;
        
        x = value.X;
        y = value.Y;
        w = value.Z;
        seq = value.Count;
        return true;
    }
    
    private static bool TryCache(uint packed, uint hintSeq, bool hasHint, out float x, out float y, out float w,
        out uint seq)
    {
        x = 0f;
        y = 0f;
        w = 1f;
        seq = 0u;
        
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
        seq = cell.Seq;
        
        if (!_alternates.TryGetValue(index, out var alt) || alt.Generation != _generation) return true;
        
        if (!hasHint) return false;
        
        var best = Distance(seq, hintSeq);
        
        for (var i = 0; i < alt.Count; i++)
        {
            alt.Get(i, out var ax, out var ay, out var az, out var aseq);
            
            var distance = Distance(aseq, hintSeq);
            if (distance >= best) continue;
            
            best = distance;
            x = ax;
            y = ay;
            w = az;
            seq = aseq;
        }
        
        return best <= SeqWindow;
    }
    
    private static bool TrySlot(int sx, int sy, out int index)
    {
        index = 0;
        if (sx < -CacheOrigin || sx >= CacheOrigin || sy < -CacheOrigin || sy >= CacheOrigin) return false;
        
        index = (sy + CacheOrigin) * CacheDim + (sx + CacheOrigin);
        return true;
    }
    
    private static uint Distance(uint a, uint b)
    {
        return a > b ? a - b : b - a;
    }
    
    private static uint Generation(uint tag)
    {
        return tag >> 1;
    }
}
