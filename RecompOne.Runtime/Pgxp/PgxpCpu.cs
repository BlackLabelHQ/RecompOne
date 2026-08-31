namespace RecompOne.Runtime.Pgxp;

public static class PgxpCpu
{
    private static readonly PgxpValue[] _gpr = new PgxpValue[32];
    
    public static void Reset()
    {
        for (var i = 0; i < _gpr.Length; i++) _gpr[i].Flags = PgxpFlags.None;
    }
    
    public static void Mfc2(int rt, int rd, uint value)
    {
        if (!Pgxp.CpuTracking) return;
        
        ref var src = ref PgxpGte.Data(rd);
        if (src.Value != value) src.Flags = PgxpFlags.None;
        
        _gpr[rt] = src;
        _gpr[rt].Value = value;
    }
    
    public static void Mtc2(int rd, int rt, uint value)
    {
        if (!Pgxp.CpuTracking) return;
        
        ref var src = ref _gpr[rt];
        if (src.Value != value) src.Flags = PgxpFlags.None;
        
        ref var dest = ref PgxpGte.Data(rd);
        dest = src;
        dest.Value = value;
    }
    public static void Lw(int rt, uint address, uint value)
    {
        if (!Pgxp.CpuTracking) return;
        
        PgxpMemory.LoadInto(address, value, ref _gpr[rt]);
    }
    public static void Sw(int rt, uint address, uint value)
    {
        if (!Pgxp.CpuTracking) return;
        
        ref var src = ref _gpr[rt];
        if (src.Value != value) src.Flags = PgxpFlags.None;
        
        PgxpMemory.Store(address, in src, value);
    }

    public static void Sh(int rt, uint address, uint value)
    {
        if (!Pgxp.CpuTracking) return;
        
        ref var src = ref _gpr[rt];
        if ((src.Value & 0xFFFFu) != (value & 0xFFFFu)) src.Flags &= ~PgxpFlags.Valid0; 
        
        PgxpMemory.StoreHalf(address, in src, (ushort)value);
    }

    public static void Lh(int rt, uint address, uint value)
    {
        if (!Pgxp.CpuTracking) return;
        
        PgxpMemory.LoadHalf(address, value, ref _gpr[rt]);
    }
    
    public static void Lwc2(int rt, uint address, uint value)
    {
        if (!Pgxp.CpuTracking) return;
        
        PgxpMemory.LoadInto(address, value, ref PgxpGte.Data(rt));
    }
    
    public static void Swc2(int rt, uint address, uint value)
    {
        if (!Pgxp.CpuTracking) return;
        
        ref var src = ref PgxpGte.Data(rt);
        if (src.Value != value) src.Flags = PgxpFlags.None;
        
        PgxpMemory.Store(address, in src, value);
    }
    
    public static void Invalidate(int rt)
    {
        if (!Pgxp.CpuTracking) return;
        
        _gpr[rt].Flags = PgxpFlags.None;
    }

    public static void InvalidateMem(uint address, uint value)
    {
        if (!Pgxp.CpuTracking) return;
        
        PgxpMemory.Invalidate(address, value);
    }
}
