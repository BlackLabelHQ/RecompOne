using RecompOne.Runtime.Cdrom;

namespace RecompOne.Runtime.Memory;

public sealed class Dma
{
    const uint Start = 0x01000000u;

    readonly IMemory _mem;
    readonly Gpu _gpu;
    readonly Spu _spu;
    readonly Mdec _mdec;
    readonly Action _raiseIrq;
    CdController? _cd;

    uint _dicr;

    public Dma(IMemory mem, Gpu gpu, Spu spu, Mdec mdec, Action raiseIrq)
    {
        _mem = mem;
        _gpu = gpu;
        _spu = spu;
        _mdec = mdec;
        _raiseIrq = raiseIrq;
    }

    public void SetCd(CdController cd) => _cd = cd;

    public uint ReadDicr() => _dicr;

    public void WriteDicr(uint val)
    {
        uint flags = (_dicr >> 24) & 0x7Fu;
        flags &= ~((val >> 24) & 0x7Fu);
        _dicr = (val & 0x00FFFFFFu) | (flags << 24);
        if ((_dicr & 0x8000u) != 0 || (((_dicr >> 23) & 1u) != 0 && flags != 0))
            _dicr |= 0x80000000u;
    }

    public void Run(int channel, uint madr, uint bcr, uint chcr)
    {
        Log.Dma($"ch{channel} madr=0x{madr:X8} bcr=0x{bcr:X8} chcr=0x{chcr:X8}");
        switch (channel)
        {
            case 0: TransferMdecIn(madr, bcr); break;
            case 1: TransferMdecOut(madr, bcr); break;
            case 2: TransferGpu(madr, bcr, chcr); break;
            case 3: TransferCd(madr, bcr); break;
            case 4: TransferSpu(madr, bcr, chcr); break;
            case 6: ClearOrderingTable(madr, bcr); break;
            default: return;
        }
        Complete(channel);
    }

    void TransferMdecIn(uint madr, uint bcr)
    {
        uint words = WordCount(bcr);
        for (uint i = 0; i < words; i++)
            _mdec.Write0(_mem.ReadU32(madr + i * 4u));
    }

    void TransferMdecOut(uint madr, uint bcr)
    {
        uint words = WordCount(bcr);
        for (uint i = 0; i < words; i++)
            _mem.WriteU32(madr + i * 4u, _mdec.ReadData());
    }

    void TransferGpu(uint madr, uint bcr, uint chcr)
    {
        uint sync = (chcr >> 9) & 3u;
        if (sync == 2)
        {
            uint addr = madr & Runtime.RamWordMask;
            for (int guard = 0; guard < 0x100000; guard++)
            {
                uint header = _mem.ReadU32(addr);
                uint count = header >> 24;
                for (uint i = 0; i < count; i++)
                    _gpu.WriteGp0(_mem.ReadU32(addr + 4u + i * 4u));
                uint next = header & 0xFFFFFFu;
                if (next == 0xFFFFFFu || (next & 0x800000u) != 0) break;
                addr = next & Runtime.RamWordMask;
            }
        }
        else if ((chcr & 1u) != 0)
        {
            uint words = WordCount(bcr);
            for (uint i = 0; i < words; i++)
                _gpu.WriteGp0(_mem.ReadU32(madr + i * 4u));
        }
        else
        {
            uint words = WordCount(bcr);
            for (uint i = 0; i < words; i++)
                _mem.WriteU32(madr + i * 4u, _gpu.ReadData());
        }
    }

    void TransferSpu(uint madr, uint bcr, uint chcr)
    {
        if ((chcr & 1u) == 0) return;
        uint bytes = WordCount(bcr) * 4u;
        var buf = new byte[bytes];
        for (uint i = 0; i < bytes; i++)
            buf[i] = _mem.ReadU8(madr + i);
        _spu.DmaWrite(_spu.TransferAddrBytes(), buf);
    }

    void TransferCd(uint madr, uint bcr)
    {
        if (_cd == null) return;
        _cd.DmaReadData(_mem, madr, WordCount(bcr) * 4u);
    }

    void ClearOrderingTable(uint madr, uint bcr)
    {
        uint count = bcr & 0xFFFFu;
        if (count == 0) return;
        uint addr = madr;
        for (uint i = 0; i < count - 1; i++)
        {
            _mem.WriteU32(addr, (addr - 4u) & 0x00FFFFFFu);
            addr -= 4u;
        }
        _mem.WriteU32(addr, 0x00FFFFFFu);
    }

    void Complete(int channel)
    {
        bool master = (_dicr & (1u << 23)) != 0;
        bool enabled = (_dicr & (1u << (16 + channel))) != 0;
        if (!master || !enabled) return;
        _dicr |= 1u << (24 + channel);
        _raiseIrq();
    }

    static uint WordCount(uint bcr)
    {
        uint size = bcr & 0xFFFFu;
        uint blocks = (bcr >> 16) & 0xFFFFu;
        uint total = blocks == 0 ? size : size * blocks;
        return total == 0 ? 0x10000u : total;
    }
}
