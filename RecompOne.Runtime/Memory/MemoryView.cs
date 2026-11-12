using RecompOne.Runtime.Cdrom;

namespace RecompOne.Runtime.Memory;

public abstract class MemoryView(IMemory memory) : IMemory
{
    protected IMemory Memory { get; } = memory;

    public virtual byte ReadU8(uint address) => Memory.ReadU8(address);
    public virtual ushort ReadU16(uint address) => Memory.ReadU16(address);
    public virtual uint ReadU32(uint address) => Memory.ReadU32(address);

    public virtual void WriteU8(uint address, byte value) => Memory.WriteU8(address, value);
    public virtual void WriteU16(uint address, ushort value) => Memory.WriteU16(address, value);
    public virtual void WriteU32(uint address, uint value) => Memory.WriteU32(address, value);

    public virtual uint ReadWordLeft(uint current, uint address) => Memory.ReadWordLeft(current, address);
    public virtual uint ReadWordRight(uint current, uint address) => Memory.ReadWordRight(current, address);

    public virtual void WriteWordLeft(uint address, uint value) => Memory.WriteWordLeft(address, value);
    public virtual void WriteWordRight(uint address, uint value) => Memory.WriteWordRight(address, value);

    public virtual void LoadBytes(uint address, byte[] data) => Memory.LoadBytes(address, data);
    public virtual void ZeroRange(uint address, uint length) => Memory.ZeroRange(address, length);

    public virtual void SetCd(CdController cd) => Memory.SetCd(cd);
}
