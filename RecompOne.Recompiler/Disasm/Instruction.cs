using RabbitizerSharp;
using RabbitizerSharp.Native;

namespace RecompOne.Recompiler.Disasm;

public sealed class MipsInstruction : IDisposable
{
    private readonly Instruction _rab;
    public uint Word { get; }
    public uint Vram { get; }

    public int Rs => (int)((Word >> 21) & 0x1F);
    public int Rt => (int)((Word >> 16) & 0x1F);
    public int Rd => (int)((Word >> 11) & 0x1F);
    public int Sa => (int)((Word >> 6) & 0x1F);
    public short ImmS => (short)(Word & 0xFFFF);
    public ushort ImmU => (ushort)(Word & 0xFFFF);
    public uint InstrIndex => Word & 0x3FFFFFFu;

    public uint BranchTarget => (uint)(Vram + 4 + (ImmS << 2));
    public uint JumpTarget => ((Vram + 4) & 0xF0000000u) | (InstrIndex << 2);


    //a bit messy but usefu
    public RabbitizerInstrId UniqueId => _rab.UniqueId;
    public RabbitizerInstrCategory Category => _rab.Category;
    public bool IsNop => _rab.IsNop;
    public bool IsValid => _rab.IsValid;
    public bool IsImplemented => _rab.IsImplemented;
    public bool IsReturn => _rab.IsReturn;
    public bool IsFunctionCall => _rab.IsFunctionCall;
    public bool IsUnconditionalBranch => _rab.IsUnconditionalBranch;
    public bool IsJumptableJump => _rab.IsJumptableJump;
    public bool HasDelaySlot => _rab.HasDelaySlot;

    public bool IsJump => UniqueId is RabbitizerInstrId.cpu_j or RabbitizerInstrId.r3000gte_INVALID;
    public bool IsRegisterJump => UniqueId is RabbitizerInstrId.cpu_jr or RabbitizerInstrId.cpu_jalr;
    public bool IsJrRegister => UniqueId == RabbitizerInstrId.cpu_jr;
    public bool IsBranch => !IsJump && HasDelaySlot && !IsFunctionCall;

    public bool IsLoad => UniqueId is RabbitizerInstrId.cpu_lw or RabbitizerInstrId.cpu_lh or RabbitizerInstrId.cpu_lhu
        or RabbitizerInstrId.cpu_lb or RabbitizerInstrId.cpu_lbu or RabbitizerInstrId.cpu_lwl
        or RabbitizerInstrId.cpu_lwr;

    public MipsInstruction(uint word, uint vram,
        RabbitizerInstrCategory category = RabbitizerInstrCategory.R3000Gte)
    {
        Word = word;
        Vram = vram;
        _rab = new Instruction(word, vram, category);
    }

    public string Disassemble(string? immOverride = null)
    {
        return _rab.Disassemble(immOverride);
    }

    public override string ToString()
    {
        return _rab.ToString();
    }

    public void Dispose()
    {
        _rab.Dispose();
    }
}