namespace RecompOne.Runtime.Memory;

public interface IMemory // https://psx-spx.consoledev.net/memorymap/
{
    byte ReadU8(uint address);
    ushort ReadU16(uint address);
    uint ReadU32(uint address);

    void WriteU8(uint address, byte value);
    void WriteU16(uint address, ushort value);
    void WriteU32(uint address, uint value);

    uint ReadWordLeft(uint current, uint address);
    uint ReadWordRight(uint current, uint address);

    void WriteWordLeft(uint address, uint value);
    void WriteWordRight(uint address, uint value);

    void LoadBytes(uint address, byte[] data);
    void ZeroRange(uint address, uint length);

    void SetCd(Cdrom.CdController cd);
}