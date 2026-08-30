namespace RecompOne.Runtime;

public static class Log
{
    public static bool BiosOn = false;
    public static bool SpuOn = false;
    public static bool GpuOn = false;
    public static bool DmaOn = false;
    public static bool CdOn = false;
    public static bool SdkOn = false;
    public static bool MdecOn = false;
    public static bool IrqOn = false;

    public static void Mdec(string m)
    {
        if (MdecOn) Console.WriteLine($"[MDEC] {m}");
    }

    public static void Irq(string m)
    {
        if (IrqOn) Console.WriteLine($"[IRQ] {m}");
    }

    public static void Bios(string m)
    {
        if (BiosOn) Console.WriteLine($"[BIOS] {m}");
    }

    public static void Spu(string m)
    {
        if (SpuOn) Console.WriteLine($"[SPU] {m}");
    }

    public static void Gpu(string m)
    {
        if (GpuOn)
            Console.WriteLine($"[GPU] {m}");
    }

    public static void Dma(string m)
    {
        if (DmaOn) Console.WriteLine($"[DMA] {m}");
    }

    public static void Cd(string m)
    {
        if (CdOn) Console.WriteLine($"[CD] {m}");
    }

    public static void Sdk(string m)
    {
        if (SdkOn) Console.WriteLine($"[SDK] {m}");
    }
}