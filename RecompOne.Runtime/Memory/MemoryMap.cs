namespace RecompOne.Runtime.Memory;

public static class MemoryMap
{
    public const uint RamBase = 0x00000000;
    public const uint RamWindow = 0x00800000;
    public const uint RetailRamSize = 0x00200000;
    public const uint DevkitRamSize = 0x00800000;

    public const uint ScratchpadBase = 0x1F800000;
    public const uint ScratchpadSize = 0x00000400;

    public const uint HwRegsBase = 0x1F801000;
    public const uint HwRegsSize = 0x00002000;

    public const uint Expansion1Base = 0x1F000000;
    public const uint Expansion2Base = 0x1F802000;
    public const uint Expansion3Base = 0x1FA00000;

    public const uint BiosBase = 0x1FC00000;
    public const uint BiosSize = 0x00080000;

    public const uint Kseg0Base = 0x80000000;
    public const uint Kseg1Base = 0xA0000000;
    public const uint PhysicalMask = 0x1FFFFFFF;

    public static uint ToPhysical(uint vaddr)
    {
        return vaddr & PhysicalMask;
    }
}