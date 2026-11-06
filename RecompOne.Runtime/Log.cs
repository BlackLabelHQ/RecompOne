namespace RecompOne.Runtime;

public static class Log
{
    public static bool BiosOn { get; set; }
    public static bool SpuOn { get; set; }
    public static bool GpuOn { get; set; }
    public static bool DmaOn { get; set; }
    public static bool CdOn { get; set; }
    public static bool SdkOn { get; set; }
    public static bool VSyncOn { get; set; }
    public static bool MdecOn { get; set; }
    public static bool IrqOn { get; set; }

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