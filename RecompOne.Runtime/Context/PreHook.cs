using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Context;

//some shenineguns to properly resolve the prehook without forcing it to be always bool
public static class PreHook
{
    public static bool Run(Func<CpuContext, IMemory, bool> hook, CpuContext c, IMemory m)
    {
        return hook(c, m);
    }

    public static bool Run(Action<CpuContext, IMemory> hook, CpuContext c, IMemory m)
    {
        hook(c, m);
        return true;
    }
}