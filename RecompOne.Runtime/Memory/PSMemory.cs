using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RecompOne.Runtime.Cdrom;
using RecompOne.Runtime.Dispatch;
using RecompOne.Runtime.Hardware;

namespace RecompOne.Runtime.Memory;

public sealed class PSMemory : IMemory
{
    private readonly byte[] _ram;
    private readonly byte[] _scratchpad = new byte[MemoryMap.ScratchpadSize];
    private readonly byte[] _hwregs = new byte[MemoryMap.HwRegsSize];
    private readonly byte[] _bios = new byte[MemoryMap.BiosSize];
  
    private readonly Gpu _gpu = new();
    private readonly Spu _spu = new();
    private readonly Mdec _mdec = new();
    private readonly Timers _timers = new();
    private readonly Dma _dma;
    private CdController? _cd;

    public ReadOnlySpan<byte> Ram => _ram;

    public bool TryWords(uint address, int count, out ReadOnlySpan<uint> words)
    {
        uint phys = MemoryMap.ToPhysical(address);
        uint off = phys & _ramMask;

        if (phys >= MemoryMap.RamWindow || (off & 3u) != 0 || off + (uint)count * 4u > (uint)_ram.Length)
        {
            words = default;
            return false;
        }

        words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(_ram.AsSpan((int)off, count * 4));
        return true;
    }
    internal byte[] RamBuffer => _ram;
    
    //memory can be frozen for debuging reasons
    private readonly bool[] _frozen;
    private int _frozenCount;

    private readonly uint _ramMask;

    public PSMemory(uint ramSize = 0)
    {
        uint size = ramSize != 0 ? ramSize
            : (Runtime.Mode == RunMode.Devkit ? MemoryMap.DevkitRamSize : MemoryMap.RetailRamSize);
        if (size < MemoryMap.RetailRamSize) size = MemoryMap.RetailRamSize;
        if (size > MemoryMap.RamWindow) size = MemoryMap.RamWindow;
        if ((size & (size - 1)) != 0) throw new ArgumentException("ram size must be a power of two", nameof(ramSize));

        _ram = new byte[size];
        _ramMask = size - 1u;
        _frozen = new bool[size];
        Runtime.RamSize = size;

        _dma = new Dma(this, _gpu, _spu, _mdec, () => Runtime.DispatchIrq(3));
        Runtime.Gpu = _gpu;
        Runtime.Spu = _spu;
        Bios.KromFont.InstallInto(_bios);
    }

    public void SetCd(CdController cd) { _cd = cd; _dma.SetCd(cd); }

    private static bool IsDmaChcr(uint phys) => phys >= 0x1F801080u && phys < 0x1F8010F0u && (phys & 0xFu) == 8u;

    private uint Hw32(uint phys)
    {
        int o = (int)(phys - MemoryMap.HwRegsBase);
        return (uint)(_hwregs[o] | (_hwregs[o + 1] << 8) | (_hwregs[o + 2] << 16) | (_hwregs[o + 3] << 24));
    }

    private void Hw32(uint phys, uint v)
    {
        int o = (int)(phys - MemoryMap.HwRegsBase);
        _hwregs[o] = (byte)v;
        _hwregs[o + 1] = (byte)(v >> 8);
        _hwregs[o + 2] = (byte)(v >> 16);
        _hwregs[o + 3] = (byte)(v >> 24);
    }

    private void TrackWrite(uint phys, int size)
    {
        if (phys < MemoryMap.RamWindow)
        {
            uint off = phys & _ramMask;
            if (RamLogger.TrackWrites) Runtime.RamLog.RecordWrite(off, size);
            Dispatcher.NotifyWrite(off);
        }

    }

    private void TrackRead(uint phys, int size)
    {
        if (RamLogger.TrackReads && phys < MemoryMap.RamWindow)
            Runtime.RamLog.RecordRead(phys & _ramMask, size);
    }

    private Span<byte> Resolve(uint address, int size)
    {
        uint phys = MemoryMap.ToPhysical(address);

        if (phys < MemoryMap.RamWindow)
            return _ram.AsSpan((int)(phys & _ramMask), size);

        if (phys >= MemoryMap.ScratchpadBase && phys < MemoryMap.ScratchpadBase + MemoryMap.ScratchpadSize)
            return _scratchpad.AsSpan((int)(phys - MemoryMap.ScratchpadBase), size);

        if (phys >= MemoryMap.HwRegsBase && phys < MemoryMap.HwRegsBase + MemoryMap.HwRegsSize)
            return _hwregs.AsSpan((int)(phys - MemoryMap.HwRegsBase), size);

        if (phys >= MemoryMap.BiosBase && phys < MemoryMap.BiosBase + MemoryMap.BiosSize)
            return _bios.AsSpan((int)(phys - MemoryMap.BiosBase), size);

        throw new InvalidOperationException($"unmapped address: 0x{address:X8}");
    }

    private readonly Sio0 _sio = new();

    private static bool IsCd(uint phys) => phys >= 0x1F801800u && phys <= 0x1F801803u;
    private static bool IsSpu(uint phys) => phys >= 0x1F801C00u && phys < 0x1F801E80u;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool InRam(uint phys, int size, out uint off)
    {
        off = phys & _ramMask;
        return phys < MemoryMap.RamWindow && off + (uint)size <= (uint)_ram.Length;
    }

    //slow = manages hardware register stuff, faster = just acess the godamm ram, agressive inlining too (improve performance)
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadU8(uint address)
    {
        uint phys = MemoryMap.ToPhysical(address);
        uint off = phys & _ramMask;
        if (phys < MemoryMap.RamWindow && off < (uint)_ram.Length && !RamLogger.TrackReads)
            return Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_ram), (nint)off);

        if (phys - MemoryMap.ScratchpadBase < MemoryMap.ScratchpadSize)
            return Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_scratchpad), (nint)(phys - MemoryMap.ScratchpadBase));

        return ReadU8Slow(address);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort ReadU16(uint address)
    {
        uint phys = MemoryMap.ToPhysical(address);
        uint off = phys & _ramMask;
        if (phys < MemoryMap.RamWindow && off + 2u <= (uint)_ram.Length && !RamLogger.TrackReads)
            return Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_ram), (nint)off));

        if (phys - MemoryMap.ScratchpadBase < MemoryMap.ScratchpadSize - 1u)
            return Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_scratchpad), (nint)(phys - MemoryMap.ScratchpadBase)));

        return ReadU16Slow(address);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadU32(uint address)
    {
        uint phys = MemoryMap.ToPhysical(address);
        uint off = phys & _ramMask;
        if (phys < MemoryMap.RamWindow && off + 4u <= (uint)_ram.Length && !RamLogger.TrackReads)
            return Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_ram), (nint)off));

        if (phys - MemoryMap.ScratchpadBase < MemoryMap.ScratchpadSize - 3u)
            return Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_scratchpad), (nint)(phys - MemoryMap.ScratchpadBase)));

        return ReadU32Slow(address);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteU8(uint address, byte value)
    {
        uint phys = MemoryMap.ToPhysical(address);
        uint off = phys & _ramMask;
        if (_frozenCount == 0 && phys < MemoryMap.RamWindow && off < (uint)_ram.Length && !RamLogger.TrackWrites)
        {
            Dispatcher.NotifyWrite(off);
            Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_ram), (nint)off) = value;
            return;
        }

        if (phys - MemoryMap.ScratchpadBase < MemoryMap.ScratchpadSize)
        {
            Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_scratchpad), (nint)(phys - MemoryMap.ScratchpadBase)) = value;
            return;
        }

        WriteU8Slow(address, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteU16(uint address, ushort value)
    {
        uint phys = MemoryMap.ToPhysical(address);
        uint off = phys & _ramMask;
        if (_frozenCount == 0 && phys < MemoryMap.RamWindow && off + 2u <= (uint)_ram.Length && !RamLogger.TrackWrites)
        {
            Dispatcher.NotifyWrite(off);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_ram), (nint)off), value);
            return;
        }

        if (phys - MemoryMap.ScratchpadBase < MemoryMap.ScratchpadSize - 1u)
        {
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_scratchpad), (nint)(phys - MemoryMap.ScratchpadBase)), value);
            return;
        }

        WriteU16Slow(address, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteU32(uint address, uint value)
    {
        uint phys = MemoryMap.ToPhysical(address);
        uint off = phys & _ramMask;
        if (_frozenCount == 0 && phys < MemoryMap.RamWindow && off + 4u <= (uint)_ram.Length && !RamLogger.TrackWrites)
        {
            Dispatcher.NotifyWrite(off);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_ram), (nint)off), value);
            return;
        }

        if (phys - MemoryMap.ScratchpadBase < MemoryMap.ScratchpadSize - 3u)
        {
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_scratchpad), (nint)(phys - MemoryMap.ScratchpadBase)), value);
            return;
        }
        
        WriteU32Slow(address, value);
    }
    
    private byte ReadU8Slow(uint address)
    {
        uint phys = MemoryMap.ToPhysical(address);
        if (InRam(phys, 1, out uint fast))
        {
            if (RamLogger.TrackReads) Runtime.RamLog.RecordRead(fast, 1);
            return _ram[fast];
        }
        TrackRead(phys, 1);
        if (_cd != null && IsCd(phys)) return _cd.Read(phys);
        if (Sio0.InRange(phys)) return (byte)_sio.Read(phys);
        return Resolve(address, 1)[0];
    }
    
    private ushort ReadU16Slow(uint address)
    {
        uint phys = MemoryMap.ToPhysical(address);
        if (InRam(phys, 2, out uint fast))
        {
            if (RamLogger.TrackReads) Runtime.RamLog.RecordRead(fast, 2);
            return BinaryPrimitives.ReadUInt16LittleEndian(_ram.AsSpan((int)fast));
        }
        TrackRead(phys, 2);
        if (_cd != null && IsCd(phys)) return _cd.Read(phys);
        if (IsSpu(phys)) return _spu.ReadReg16(phys);
        if (Sio0.InRange(phys)) return (ushort)_sio.Read(phys);
        if (phys == 0x1F801070u) return (ushort)Interrupts.ReadStat();
        if (phys == 0x1F801074u) return (ushort)Interrupts.ReadMask();
        if (Timers.InRange(phys) && _timers.TryRead(phys, out uint tv)) return (ushort)tv;
        var s = Resolve(address, 2);
        return (ushort)(s[0] | (s[1] << 8));
    }

    
    private uint ReadU32Slow(uint address)
    {
        uint phys = MemoryMap.ToPhysical(address);
        if (InRam(phys, 4, out uint fast))
        {
            if (RamLogger.TrackReads) Runtime.RamLog.RecordRead(fast, 4);
            return BinaryPrimitives.ReadUInt32LittleEndian(_ram.AsSpan((int)fast));
        }
        TrackRead(phys, 4);
        if (phys == 0x1F801810u) return _gpu.ReadData();
        if (phys == 0x1F801814u) return _gpu.ReadStat();
        if (phys == 0x1F801820u) return _mdec.ReadData();
        if (phys == 0x1F801824u) return _mdec.ReadStatus();
        if (phys == 0x1F8010F4u) return _dma.ReadDicr();
        if (Sio0.InRange(phys)) return _sio.Read(phys);
        if (phys == 0x1F801070u) return Interrupts.ReadStat();
        if (phys == 0x1F801074u) return Interrupts.ReadMask();
        if (_cd != null && IsCd(phys)) return _cd.Read(phys);
        if (IsSpu(phys)) return (uint)(_spu.ReadReg16(phys) | (_spu.ReadReg16(phys + 2) << 16));
        if (Timers.InRange(phys) && _timers.TryRead(phys, out uint tv)) return tv;
        var s = Resolve(address, 4);
        return (uint)(s[0] | (s[1] << 8) | (s[2] << 16) | (s[3] << 24));
    }
    
    private void WriteU8Slow(uint address, byte value)
    {
        uint phys = MemoryMap.ToPhysical(address);
        if (_frozenCount == 0 && InRam(phys, 1, out uint fast))
        {
            if (RamLogger.TrackWrites) Runtime.RamLog.RecordWrite(fast, 1);
            Dispatcher.NotifyWrite(fast);
            _ram[fast] = value;
            return;
        }
        TrackWrite(phys, 1);
        if (_cd != null && IsCd(phys)) { _cd.Write(phys, value); return; }
        if (Hardware.Sio0.InRange(phys)) { _sio.Write(phys, value); return; }

        if (_frozenCount > 0 && phys < MemoryMap.RamWindow && _frozen[phys & _ramMask]) return;
        Resolve(address, 1)[0] = value;
    }
    
    private void WriteU16Slow(uint address, ushort value)
    {
        uint phys = MemoryMap.ToPhysical(address);
        if (_frozenCount == 0 && InRam(phys, 2, out uint fast))
        {
            if (RamLogger.TrackWrites) Runtime.RamLog.RecordWrite(fast, 2);
            Dispatcher.NotifyWrite(fast);
            BinaryPrimitives.WriteUInt16LittleEndian(_ram.AsSpan((int)fast), value);
            return;
        }
        TrackWrite(phys, 2);
        if (_cd != null && IsCd(phys)) { _cd.Write(phys, (byte)value); return; }
        if (IsSpu(phys)) { _spu.WriteReg16(phys, value); return; }
        if (Sio0.InRange(phys)) { _sio.Write(phys, value); return; }
        if (phys == 0x1F801070u) { Interrupts.WriteStat(value); return; }
        if (phys == 0x1F801074u) { Interrupts.WriteMask(value); return; }
        if (_timers.TryWrite(phys, value)) return;
        var s = Resolve(address, 2);

        if (_frozenCount > 0 && phys < MemoryMap.RamWindow)
        {
            uint b = phys & _ramMask;
            if(!_frozen[b])   s[0] = (byte)value;
            if(!_frozen[b+1]) s[1] = (byte)(value >> 8);
            return;
        }
        s[0] = (byte)value;
        s[1] = (byte)(value >> 8);
    }
    
    private void WriteU32Slow(uint address, uint value)
    {
        uint phys = MemoryMap.ToPhysical(address);
        if (_frozenCount == 0 && InRam(phys, 4, out uint fast))
        {
            if (RamLogger.TrackWrites) Runtime.RamLog.RecordWrite(fast, 4);
            Dispatcher.NotifyWrite(fast);
            BinaryPrimitives.WriteUInt32LittleEndian(_ram.AsSpan((int)fast), value);
            return;
        }
        TrackWrite(phys, 4);
        if (phys == 0x1F801810u) { _gpu.WriteGp0(value); return; }
        if (phys == 0x1F801814u) { _gpu.WriteGp1(value); return; }
        if (phys == 0x1F801820u) { _mdec.Write0(value); return; }
        if (phys == 0x1F801824u) { _mdec.WriteControl(value); return; }
        if (phys == 0x1F8010F4u) { _dma.WriteDicr(value); return; }
        if (phys == 0x1F801070u) { Interrupts.WriteStat(value); return; }
        if (phys == 0x1F801074u) { Interrupts.WriteMask(value); return; }
        if (IsDmaChcr(phys) && (value & 0x01000000u) != 0)
        {
            Hw32(phys, value & ~0x01000000u);
            _dma.Run((int)((phys - 0x1F801080u) / 0x10u), Hw32(phys - 8u), Hw32(phys - 4u), value);
            return;
        }
        if (_cd != null && IsCd(phys)) { _cd.Write(phys, (byte)value); return; }
        if (Hardware.Sio0.InRange(phys)) { _sio.Write(phys, value); return; }
        if (IsSpu(phys)) { _spu.WriteReg16(phys, (ushort)value); _spu.WriteReg16(phys + 2, (ushort)(value >> 16)); return; }
        if (_timers.TryWrite(phys, value)) return;
        var s = Resolve(address, 4);
        if (_frozenCount > 0 && phys < MemoryMap.RamWindow)
        {
            uint b = phys & _ramMask;
            if(!_frozen[b])   s[0] = (byte)value;
            if(!_frozen[b+1]) s[1] = (byte)(value >> 8);
            if(!_frozen[b+2]) s[2] = (byte)(value >> 16);
            if(!_frozen[b+3]) s[3] = (byte)(value >> 24);
            return;
        }
        s[0] = (byte)value;
        s[1] = (byte)(value >> 8);
        s[2] = (byte)(value >> 16);
        s[3] = (byte)(value >> 24);
    }

    public uint ReadWordLeft(uint current, uint address)
    {
        int shift = (int)((address & 3) * 8);
        uint word = ReadU32(address & ~3u);
        return (current & (0x00FFFFFFu >> shift)) | (word << (24 - shift));
    }

    public uint ReadWordRight(uint current, uint address)
    {
        int shift = (int)((address & 3) * 8);
        uint word = ReadU32(address & ~3u);
        return (current & (0xFFFFFF00u << (24 - shift))) | (word >> shift);
    }

    public void WriteWordLeft(uint address, uint value)
    {
        uint aligned = address & ~3u;
        int shift = (int)((address & 3) * 8);
        uint mem = ReadU32(aligned);
        WriteU32(aligned, (mem & (0xFFFFFF00u << shift)) | (value >> (24 - shift)));
    }

    public void WriteWordRight(uint address, uint value)
    {
        uint aligned = address & ~3u;
        int shift = (int)((address & 3) * 8);
        uint mem = ReadU32(aligned);
        WriteU32(aligned, (mem & (0x00FFFFFFu >> (24 - shift))) | (value << shift));
    }

    public void LoadBytes(uint address, byte[] data)
    {
        for (int i = 0; i < data.Length; i++)
            WriteU8(address + (uint)i, data[i]);
    }

    public void ZeroRange(uint address, uint length)
    {
        for (uint i = 0; i < length; i++)
            WriteU8(address + i, 0);
    }

    public bool IsFrozen(uint off) => _frozenCount > 0 && _frozen[off % (uint)_frozen.Length];

    public void Freeze(uint off, int len)
    {
        for (int i = 0; i < len; i++)
        {
            uint o = (off + (uint)i) % (uint)_frozen.Length;
            if (!_frozen[o])
            {
                _frozen[o] = true;
                _frozenCount++;
            }
        }
    }
    public void Unfreeze(uint off, int len)
    {
        for (int i = 0; i < len; i++)
        {
            uint o = (off + (uint)i) % (uint)_frozen.Length;
            if (_frozen[o])
            {
                _frozen[o] = false;
                _frozenCount--;
            }
        }
    }

    public void ClearFreezes()
    {
        if (_frozenCount == 0) return;
        System.Array.Clear(_frozen, 0, _frozen.Length);
        _frozenCount = 0;
    }

    public void Poke(uint off, byte val) => _ram[off & _ramMask] = val;
    
}
