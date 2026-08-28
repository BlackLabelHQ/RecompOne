using System.Text;
using RecompOne.Recompiler.Analysis;
using RecompOne.Recompiler.Disasm;

namespace RecompOne.Recompiler.CodeGen;

//Todo: later cleanup
public static class InstructionEmitter
{
    static string R(int r) => r == 0 ? "0u" : r switch
    {
        1 =>  "c.At",
        2 =>  "c.V0",  3 =>  "c.V1",
        4 =>  "c.A0",  5 =>  "c.A1",  6 =>  "c.A2",  7 =>  "c.A3",
        8 =>  "c.T0",  9 =>  "c.T1",  10 => "c.T2",  11 => "c.T3",
        12 => "c.T4",  13 => "c.T5",  14 => "c.T6",  15 => "c.T7",
        16 => "c.S0",  17 => "c.S1",  18 => "c.S2",  19 => "c.S3",
        20 => "c.S4",  21 => "c.S5",  22 => "c.S6",  23 => "c.S7",
        24 => "c.T8",  25 => "c.T9",
        26 => "c.K0",  27 => "c.K1",
        28 => "c.GP",  29 => "c.SP",  30 => "c.FP",  31 => "c.RA",
        _ => throw new ArgumentOutOfRangeException()
    };

    static string Addr(int rs, short imm)
    {
        if (rs == 0) return $"0x{(uint)(int)imm:X8}u";
        if (imm == 0) return R(rs);
        if (imm > 0) return $"({R(rs)} + 0x{(uint)imm:X}u)";
        return $"({R(rs)} - 0x{unchecked((uint)(-(int)imm)):X}u)";
    }
    static string Cop0Read(int rd) => rd switch
    {
        8 =>  "c.BadVAddr",
        12 => "c.SR",
        13 => "c.Cause",
        14 => "c.EPC",
        15 => "c.PRId",
        _ =>  $"0u /* COP0[{rd}] */"
    };

    
    static string Cop0Write(int rd, string val) => rd switch
    {
        8 =>  $"c.BadVAddr = {val};",
        12 => $"c.SR = {val};",
        13 => $"c.Cause = {val};",
        14 => $"c.EPC = {val};",
        15 => $"c.PRId = {val};",
        _ =>  $"/* MTC0 r{rd} ignored */"
    };

    public static string EmitSingle(MipsInstruction i)
    {
        uint op = i.Word >> 26;
        uint fn = i.Word & 0x3F;
        int rs = i.Rs, rt = i.Rt, rd = i.Rd, sa = i.Sa;
        short imm = i.ImmS;
        ushort immU = i.ImmU;
        string RS = R(rs), RT = R(rt), RD = R(rd);

        if (op == 0)
        {
            return (int)fn switch
            {
                0 =>  rd == 0 ? "" : sa == 0 ? $"{RD} = {RT};" : $"{RD} = {RT} << {sa};",
                2 =>  rd == 0 ? "" : $"{RD} = {RT} >> {sa};",
                3 =>  rd == 0 ? "" : $"{RD} = (uint)((int){RT} >> {sa});",
                4 =>  rd == 0 ? "" : $"{RD} = {RT} << (int)({RS} & 31u);",
                6 =>  rd == 0 ? "" : $"{RD} = {RT} >> (int)({RS} & 31u);",
                7 =>  rd == 0 ? "" : $"{RD} = (uint)((int){RT} >> (int)({RS} & 31u));",
                8 =>  "",
                9 =>  "",
                12 => "Bios.Syscall(c, m);",
                13 => "Bios.Break(c, m);",
                16 => rd == 0 ? "" : $"{RD} = c.HI;",
                17 => $"c.HI = {RS};",
                18 => rd == 0 ? "" : $"{RD} = c.LO;",
                19 => $"c.LO = {RS};",
                24 => $"{{ var _r = (long)(int){RS} * (int){RT}; c.LO = (uint)_r; c.HI = (uint)(_r >> 32); }}",
                25 => $"{{ var _r = (ulong){RS} * {RT}; c.LO = (uint)_r; c.HI = (uint)(_r >> 32); }}",
                26 => rt == 0 ? "c.LO = 0u; c.HI = 0u;" : $"if ({RT} != 0u) {{ if ((int){RS} == int.MinValue && (int){RT} == -1) {{ c.LO = 0x80000000u; c.HI = 0u; }} else {{ c.LO = (uint)((int){RS} / (int){RT}); c.HI = (uint)((int){RS} % (int){RT}); }} }}",
                27 => rt == 0 ? "c.LO = 0u; c.HI = 0u;" : $"if ({RT} != 0u) {{ c.LO = {RS} / {RT}; c.HI = {RS} % {RT}; }}",
                32 or 33 => rd == 0 ? "" : $"{RD} = {RS} + {RT};",
                34 or 35 => rd == 0 ? "" : $"{RD} = {RS} - {RT};",
                36 => rd == 0 ? "" : $"{RD} = {RS} & {RT};",
                37 => rd == 0 ? "" : rs == 0 ? $"{RD} = {RT};" : rt == 0 ? $"{RD} = {RS};" : $"{RD} = {RS} | {RT};",
                38 => rd == 0 ? "" : $"{RD} = {RS} ^ {RT};",
                39 => rd == 0 ? "" : $"{RD} = ~({RS} | {RT});",
                42 => rd == 0 ? "" : $"{RD} = (int){RS} < (int){RT} ? 1u : 0u;",
                43 => rd == 0 ? "" : $"{RD} = {RS} < {RT} ? 1u : 0u;",
                _ =>  UnknownInstr(i, $"SPECIAL fn=0x{fn:X2}")
            };
        }

        if (op == 1) return ""; //handled in emitdelayslot

        if (op == 16)//cop0
        {
            uint cop0rs = (i.Word >> 21) & 0x1F;
            if (cop0rs == 0) return rt == 0 ? "" : $"{RT} = {Cop0Read(rd)};"; 
            if (cop0rs == 4) return Cop0Write(rd, RT);                          
            if (cop0rs == 16 && fn == 16) return "c.SR = (c.SR & ~0xFu) | ((c.SR >> 2) & 0xFu);"; 
            return $"/* COP0 rs={cop0rs} */";
        }

        if (op == 18) //gte
        {
            uint cop2rs = (i.Word >> 21) & 0x1F;
            if (cop2rs == 8) return "";
            if (((i.Word >> 25) & 1) == 1) return $"RecompOne.Runtime.Gte.Execute(0x{i.Word:X8}u);";
            return cop2rs switch
            {
                0 => rt == 0 ? "" : $"{RT} = RecompOne.Runtime.Gte.Read({rd});",
                2 => rt == 0 ? "" : $"{RT} = RecompOne.Runtime.Gte.ReadControl({rd});",
                4 => $"RecompOne.Runtime.Gte.Write({rd}, {RT});",
                6 => $"RecompOne.Runtime.Gte.WriteControl({rd}, {RT});",
                _ => $"/* COP2 rs={cop2rs} */"
            };
        }

        if (op is 2 or 3 or 4 or 5 or 6 or 7) return ""; //thej umps and branches are handled in EmitWithDelaySlot to process with the delayslot

        return (int)op switch
        {
            8  or 9 =>  rt == 0 ? "" : rs == 0 ? $"{RT} = 0x{unchecked((uint)(int)imm):X8}u;" : imm >= 0 ? $"{RT} = {RS} + 0x{(uint)imm:X}u;" : $"{RT} = {RS} - 0x{unchecked((uint)(-(int)imm)):X}u;",
            10 => rt == 0 ? "" : $"{RT} = (int){RS} < {(int)imm} ? 1u : 0u;",
            11 => rt == 0 ? "" : $"{RT} = {RS} < 0x{(uint)(int)imm:X8}u ? 1u : 0u;",
            12 => rt == 0 ? "" : $"{RT} = {RS} & 0x{immU:X4}u;",
            13 => rt == 0 ? "" : immU == 0 ? $"{RT} = {RS};" : $"{RT} = {RS} | 0x{immU:X4}u;",
            14 => rt == 0 ? "" : $"{RT} = {RS} ^ 0x{immU:X4}u;",
            15 => rt == 0 ? "" : $"{RT} = 0x{(uint)immU << 16:X8}u;",
            32 => rt == 0 ? "" : $"{RT} = (uint)(sbyte)m.ReadU8({Addr(rs, imm)});",
            33 => rt == 0 ? "" : $"{RT} = (uint)(short)m.ReadU16({Addr(rs, imm)});",
            34 => rt == 0 ? "" : $"{RT} = m.ReadWordLeft({RT}, {Addr(rs, imm)});",
            35 => rt == 0 ? "" : $"{RT} = m.ReadU32({Addr(rs, imm)});",
            36 => rt == 0 ? "" : $"{RT} = m.ReadU8({Addr(rs, imm)});",
            37 => rt == 0 ? "" : $"{RT} = m.ReadU16({Addr(rs, imm)});",
            38 => rt == 0 ? "" : $"{RT} = m.ReadWordRight({RT}, {Addr(rs, imm)});",
            40 => $"m.WriteU8({Addr(rs, imm)}, (byte){RT});",
            41 => $"m.WriteU16({Addr(rs, imm)}, (ushort){RT});",
            42 => $"m.WriteWordLeft({Addr(rs, imm)}, {RT});",
            43 => $"m.WriteU32({Addr(rs, imm)}, {RT});",
            46 => $"m.WriteWordRight({Addr(rs, imm)}, {RT});",
            50 => $"RecompOne.Runtime.Gte.LoadWord({rt}, m.ReadU32({Addr(rs, imm)}));",
            58 => $"m.WriteU32({Addr(rs, imm)}, RecompOne.Runtime.Gte.StoreWord({rt}));",
            _ =>  UnknownInstr(i, $"op=0x{op:X2}")
        };
    }
    static string UnknownInstr(MipsInstruction i, string desc)
    {
        Console.WriteLine($"[Unknown] {desc} word=0x{i.Word:X8} @ 0x{i.Vram:X8}");
        return $"/* UNKOWN OP {desc} word=0x{i.Word:X8} @ 0x{i.Vram:X8} */";
    }

    //it control instructions that emmit their own delay slot, so the it shouldnt write it a second time again, its just a filter to not produce wrongfully
    public static bool SkipDelaySlot(MipsInstruction ctrl)
    {
        uint op = ctrl.Word >> 26;
        uint fn = ctrl.Word & 0x3F;
        if (op is 2 or 3) return true;
        if (op == 0 && fn is 8 or 9) return true;
        if (op == 4 && ctrl.Rs == ctrl.Rt) return true;
        if (op == 1 && (uint)ctrl.Rt is 0x10 or 0x11) return true;
        return false;
    }

    public static void EmitWithDelaySlot(StringBuilder sb, MipsInstruction ctrl, MipsInstruction? ds, FunctionContext ctx, string indent)
    {
        uint op = ctrl.Word >> 26;
        uint fn = ctrl.Word & 0x3F;
        int rs = ctrl.Rs, rt = ctrl.Rt, rd = ctrl.Rd;
        uint pc = ctrl.Vram;
        string RS = R(rs), RT = R(rt);
        string ind2 = indent + "    ";

        void Ds()
        {
            if (ds == null) return;
            //fixes delay slot as branch target bug
            string line = EmitSingle(ds);
            if (!string.IsNullOrEmpty(line)) sb.AppendLine(ctx.Trail(ds, $"{indent}{line}"));
        }

        void DsInline()
        {
            if (ds == null) return;
            string line = EmitSingle(ds);
            if (!string.IsNullOrEmpty(line)) sb.AppendLine(ctx.Trail(ds, $"{ind2}{line}"));
        }

        void CallOrDispatch(uint addr, string ind)
        {
            if (ctx.KnownFunctions.TryGetValue(addr, out var name))
                sb.AppendLine(ctx.Trail(ctrl, $"{ind}{name}(c, m);"));
            else
                sb.AppendLine(ctx.Trail(ctrl, $"{ind}Dispatcher.Call(c, m, 0x{addr:X8}u);"));
        }

        bool InFunc(uint target) => target >= ctx.FuncStart && target < ctx.FuncEnd;

        void Conditional(string cond, uint target)
        {
            sb.AppendLine(ctx.Trail(ctrl, $"{indent}if ({cond}) {{"));
            DsInline();
            if (InFunc(target))
                sb.AppendLine(ctx.Trail(ctrl, $"{ind2}goto L{target:X8};"));
            else
            {
                CallOrDispatch(target, ind2);
                sb.AppendLine(ctx.Trail(ctrl, $"{ind2}return;"));
            }
            sb.AppendLine(ctx.Trail(ctrl, $"{indent}}}"));
        }

        if (op is 4 or 5 or 6 or 7)
        {
            uint target = ctrl.BranchTarget;
            if (op == 4 && rs == rt)
            {
                Ds();
                if (InFunc(target)) sb.AppendLine(ctx.Trail(ctrl, $"{indent}goto L{target:X8};"));
                else { CallOrDispatch(target, indent); sb.AppendLine(ctx.Trail(ctrl, $"{indent}return;")); }
                return;
            }
            if (op == 5 && rs == rt) return;
            string cond = op switch
            {
                4 => $"{RS} == {RT}",
                5 => $"{RS} != {RT}",
                6 => $"(int){RS} <= 0",
                _ => $"(int){RS} > 0",
            };
            Conditional(cond, target);
            return;
        }

        if (op == 1)
        {
            uint rtField = (uint)rt;
            uint target = ctrl.BranchTarget;
            bool link = rtField is 0x10 or 0x11;
            string cond = rtField switch
            {
                0x00 or 0x10 => $"(int){RS} < 0",
                0x01 or 0x11 => $"(int){RS} >= 0",
                _ => "false"
            };
            if (link)
            {
                Ds();
                sb.AppendLine(ctx.Trail(ctrl, $"{indent}c.RA = 0x{pc + 8:X8}u;"));
                sb.AppendLine(ctx.Trail(ctrl, $"{indent}if ({cond}) {{"));
                if (InFunc(target)) sb.AppendLine(ctx.Trail(ctrl, $"{ind2}goto L{target:X8};"));
                else CallOrDispatch(target, ind2);
                sb.AppendLine(ctx.Trail(ctrl, $"{indent}}}"));
            }
            else Conditional(cond, target);
            return;
        }

        if (op == 3)
        {
            uint target = ctrl.JumpTarget;
            Ds();
            sb.AppendLine(ctx.Trail(ctrl, $"{indent}c.RA = 0x{pc + 8:X8}u;"));
            CallOrDispatch(target, indent);
            return;
        }
        if (op == 2)
        {
            uint target = ctrl.JumpTarget;
            Ds();
            if (InFunc(target)) sb.AppendLine(ctx.Trail(ctrl, $"{indent}goto L{target:X8};"));
            else { CallOrDispatch(target, indent); sb.AppendLine(ctx.Trail(ctrl, $"{indent}return;")); }
            return;
        }
        if (op == 0 && fn == 8)
        {
            Ds();
            if (rs == 31 || ctx.RaReturnJrs.Contains(pc)) sb.AppendLine(ctx.Trail(ctrl, $"{indent}return;"));
            else if (ctx.JumpTablesByJr.TryGetValue(pc, out var jtbl))
            {
                sb.AppendLine(ctx.Trail(ctrl, $"{indent}switch ({RS})"));
                sb.AppendLine(ctx.Trail(ctrl, $"{indent}{{"));
                foreach (uint entry in jtbl.Entries.Distinct())
                    sb.AppendLine(ctx.Trail(ctrl, $"{indent}    case 0x{entry:X8}u: goto L{entry:X8};"));
                sb.AppendLine(ctx.Trail(ctrl, $"{indent}    default: Dispatcher.Call(c, m, {RS}); return;"));
                sb.AppendLine(ctx.Trail(ctrl, $"{indent}}}"));
            }
            else
            {
                sb.AppendLine(ctx.Trail(ctrl, $"{indent}Dispatcher.Call(c, m, {RS});"));
                sb.AppendLine(ctx.Trail(ctrl, $"{indent}return;"));
            }
            return;
        }
        if (op == 0 && fn == 9)
        {
            Ds();
            if (rd != 0) sb.AppendLine(ctx.Trail(ctrl, $"{indent}{R(rd)} = 0x{pc + 8:X8}u;"));
            sb.AppendLine(ctx.Trail(ctrl, $"{indent}Dispatcher.Call(c, m, {RS});"));
            return;
        }
        if (op == 18 && ((ctrl.Word >> 21) & 0x1F) == 8)
        {
            uint target = ctrl.BranchTarget;
            string cond = rt == 1 ? "RecompOne.Runtime.Gte.GetCondition()" : "!RecompOne.Runtime.Gte.GetCondition()";
            Conditional(cond, target);
            return;
        }
    }
}

public sealed class FunctionContext
{
    public uint FuncStart;
    public uint FuncEnd;
    public Dictionary<uint, string> KnownFunctions = [];
    public HashSet<uint> Labels = [];
    public HashSet<uint> BackEdges = []; //fpr irq
    public bool Debug;
    public bool AddressComments;
    public bool DisasmComments;
    public Dictionary<uint, JumpTable> JumpTablesByJr = [];
    public HashSet<uint> RaReturnJrs = [];
    public MipsInstruction[] AllInstructions = [];

    const int CommentColumn = 64;

    public string Trail(MipsInstruction i, string line)
    {
        if (!AddressComments && !DisasmComments) return line;
        string body = DisasmComments ? $"/* 0x{i.Vram:X8}  {i.Disassemble()} */" : $"/* 0x{i.Vram:X8} */";
        return (line.Length < CommentColumn ? line.PadRight(CommentColumn) : line + "  ") + body;
    }
    
    public uint SkipNopPadding(uint addr) //faltru can end up in padding
    {
        if (AllInstructions.Length == 0) return addr;
        uint baseAddr = AllInstructions[0].Vram;
        if (addr < baseAddr) return addr;

        int i = (int)((addr - baseAddr) / 4);
        while (i >= 0 && i < AllInstructions.Length && AllInstructions[i].IsNop)
        {
            addr += 4;
            i++;
        }
        return addr;
    }
}
