using System.Buffers.Binary;
using RecompOne.Recompiler.Symbols;

namespace RecompOne.Recompiler.Analysis;

//still not sure if this works for all cases, seens to do, loosely based on n64recomp, but stupider
public static class JumpTableAnalyzer
{
    private struct RegState
    {
        public uint PrevLui;
        public int PrevAddiuLo;
        public bool ValidLui;
        public bool ValidAddiu;
        public bool ValidAddend;
        public bool ValidLoaded;
        public uint TableVram;

        public void Invalidate()
        {
            this = default;
        }
    }

    public static List<JumpTable> Analyze(MipsFunction func, FunctionInfo elf)
    {
        var regs = new RegState[32];
        var result = new List<JumpTable>();

        foreach (var instr in func.Instructions)
        {
            var op = instr.Word >> 26;
            var fn = instr.Word & 0x3F;
            int rs = instr.Rs, rt = instr.Rt, rd = instr.Rd;

            switch (op)
            {
                case 0:
                    switch (fn)
                    {
                        case 32:
                        case 33:
                            if (rd != 0) Addu(regs, rs, rt, rd);
                            break;
                        case 37:
                            if (rd != 0)
                            {
                                if (rs == 0) regs[rd] = regs[rt];
                                else if (rt == 0) regs[rd] = regs[rs];
                                else regs[rd].Invalidate();
                            }

                            break;
                        case 8:
                            if (rs != 31 && regs[rs].ValidLoaded)
                            {
                                var entries = ReadEntries(elf, regs[rs].TableVram, func);
                                if (entries.Length > 0)
                                    result.Add(new JumpTable { JrVram = instr.Vram, Entries = entries });
                            }

                            break;
                        default:
                            if (rd != 0) regs[rd].Invalidate();
                            break;
                    }

                    break;

                case 8:
                case 9:
                {
                    var temp = regs[rs];
                    if (!temp.ValidAddiu)
                    {
                        temp.PrevAddiuLo = (int)instr.ImmS;
                        temp.ValidAddiu = true;
                    }
                    else
                    {
                        temp.Invalidate();
                    }

                    if (rt != 0) regs[rt] = temp;
                    break;
                }

                case 15:
                    if (rt != 0)
                    {
                        regs[rt].Invalidate();
                        regs[rt].PrevLui = (uint)instr.ImmU << 16;
                        regs[rt].ValidLui = true;
                    }

                    break;

                case 35:
                    if (rt != 0)
                    {
                        var baseReg = regs[rs];
                        regs[rt].Invalidate();
                        if (rs != 29 && baseReg.ValidLui && (baseReg.ValidAddend || baseReg.ValidAddiu))
                        {
                            var imm = instr.ImmS;
                            var nonzero = imm != 0;
                            if (!(nonzero && baseReg.ValidAddiu))
                            {
                                var lo16 = nonzero ? (uint)(int)imm : (uint)baseReg.PrevAddiuLo;
                                regs[rt].TableVram = baseReg.PrevLui + lo16;
                                regs[rt].ValidLoaded = true;
                            }
                        }
                    }

                    break;

                case 2:
                case 4:
                case 5:
                case 6:
                case 7: break;
                case 3:
                    regs[31].Invalidate();
                    break;
                case 16:
                case 18:
                case 40:
                case 41:
                case 42:
                case 43:
                case 46: break;
                default:
                    if (rt != 0) regs[rt].Invalidate();
                    break;
            }
        }

        return result;
    }

    //the add that joins the lui half with the index
    private static void Addu(RegState[] regs, int rs, int rt, int rd)
    {
        var rsLui = regs[rs].ValidLui;
        var rtLui = regs[rt].ValidLui;
        if (rsLui != rtLui)
        {
            var luiSrc = rsLui ? rs : rt;
            regs[rd] = regs[luiSrc];
            regs[rd].ValidAddend = true;
        }
        else if (rs == 0)
        {
            regs[rd] = regs[rt];
        }
        else if (rt == 0)
        {
            regs[rd] = regs[rs];
        }
        else
        {
            regs[rd].Invalidate();
        }
    }

    private static uint[] ReadEntries(FunctionInfo elf, uint tableVram, MipsFunction func)
    {
        var entries = new List<uint>();
        var vram = tableVram;
        while (TryReadWord(elf, vram, out var word))
        {
            if (word < func.Start || word >= func.End) break;
            entries.Add(word);
            vram += 4;
        }

        return entries.ToArray();
    }

    private static bool TryReadWord(FunctionInfo elf, uint vram, out uint value)
    {
        foreach (var sec in elf.DataSections)
        {
            if (sec.IsZero) continue;
            var secEnd = sec.Va + (uint)sec.Data.Length;
            if (vram >= sec.Va && vram + 4 <= secEnd)
            {
                value = BinaryPrimitives.ReadUInt32LittleEndian(sec.Data.AsSpan((int)(vram - sec.Va)));
                return true;
            }
        }

        //shouldnt this be in rodata?
        if (vram >= elf.TextBase && vram + 4 <= elf.TextBase + (uint)elf.TextData.Length)
        {
            value = BinaryPrimitives.ReadUInt32LittleEndian(elf.TextData.AsSpan((int)(vram - elf.TextBase)));
            return true;
        }

        value = 0;
        return false;
    }
}