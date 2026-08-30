using RecompOne.Runtime.Bios;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Sdk;

public static class LibApi
{
    public static void PatchPad(CpuContext c, IMemory m)
    {
        Log.Bios("_patch_pad");
    }

    public static void PatchCard(CpuContext c, IMemory m)
    {
        Log.Bios("_patch_card ");
    }

    public static void PatchCard2(CpuContext c, IMemory m)
    {
        Log.Bios("_patch_card2");
    }

    public static void PatchBios(CpuContext c, IMemory m)
    {
        Log.Bios("patch bios");
    }

    public static void PatchedBiosCall(CpuContext c, IMemory m)
    {
        c.V0 = 1u;
    }
}