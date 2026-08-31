namespace RecompOne.Runtime.Pgxp;

public static class PgxpGte
{
    private static PgxpValue _sxy0;
    private static PgxpValue _sxy1;
    private static PgxpValue _sxy2;
    private static uint _count;
    
    private static readonly PgxpValue[] _data = new PgxpValue[32];
    public static ref PgxpValue Data(int reg)
    {
        return ref _data[reg & 31];
    }
    public static ref PgxpValue Sxy0 => ref _sxy0;
    public static ref PgxpValue Sxy1 => ref _sxy1;
    public static ref PgxpValue Sxy2 => ref _sxy2;
    
    public static void PushVertex(float x, float y, float w, uint packed)
    {
        _sxy0 = _sxy1;
        _sxy1 = _sxy2;
        
        _sxy2.X = x;
        _sxy2.Y = y;
        _sxy2.Z = Pgxp.TextureCorrection ? w : 1f;
        _sxy2.Value = packed;
        _sxy2.Flags = PgxpFlags.ValidAll;
        _sxy2.Count = _count++;
        
        _data[12] = _sxy0;
        _data[13] = _sxy1;
        _data[14] = _sxy2;
        _data[15] = _sxy2;
        
        if (Pgxp.VertexCache) PgxpGpu.CacheVertex((short)(packed & 0xFFFF), (short)(packed >> 16), in _sxy2);
    }
    
    public static void Invalidate()
    {
        _sxy0.Flags = PgxpFlags.None;
        _sxy1.Flags = PgxpFlags.None;
        _sxy2.Flags = PgxpFlags.None;
        _count = 0;
        
        
        for (var i = 0; i < _data.Length; i++) _data[i].Flags = PgxpFlags.None;
    }
    
    public static bool TryNclip(short x0, short y0, short x1, short y1, short x2, short y2, out double result)
    {
        result = 0.0;

        var p0 = PackXy(x0, y0);
        var p1 = PackXy(x1, y1);
        var p2 = PackXy(x2, y2);

        if (!PgxpFlags.Matches(in _sxy0, p0) || !PgxpFlags.Matches(in _sxy1, p1) || !PgxpFlags.Matches(in _sxy2, p2)) return false;
        var nclip = (double)_sxy0.X * (_sxy1.Y - _sxy2.Y) + (double)_sxy1.X * (_sxy2.Y - _sxy0.Y) + (double)_sxy2.X * (_sxy0.Y - _sxy1.Y);
        
        var magnitude = Math.Abs(nclip);
        if (magnitude > 0.1 && magnitude < 1.0) nclip += nclip < 0.0 ? -1.0 : 1.0;
        
        result = nclip;
        return true;
    }
    
    public static uint PackXy(int x, int y)
    {
        return (uint)(ushort)(short)x | ((uint)(ushort)(short)y << 16);
    }
}