using System.Text;
using RecompOne.Recompiler.Analysis;
using RecompOne.Recompiler.Disasm;

namespace RecompOne.Recompiler.CodeGen;

//Todo: later cleanup
public static class InstructionEmitter
{
    private static string R(int r)
    {
        return r == 0
            ? "0u"
            : r switch
            {
                1 => "c.At",
                2 => "c.V0", 3 => "c.V1",
                4 => "c.A0", 5 => "c.A1", 6 => "c.A2", 7 => "c.A3",
                8 => "c.T0", 9 => "c.T1", 10 => "c.T2", 11 => "c.T3",
                12 => "c.T4", 13 => "c.T5", 14 => "c.T6", 15 => "c.T7",
                16 => "c.S0", 17 => "c.S1", 18 => "c.S2", 19 => "c.S3",
                20 => "c.S4", 21 => "c.S5", 22 => "c.S6", 23 => "c.S7",
                24 => "c.T8", 25 => "c.T9",
                26 => "c.K0", 27 => "c.K1",
                28 => "c.GP", 29 => "c.SP", 30 => "c.FP", 31 => "c.RA",
                _ => throw new ArgumentOutOfRangeException()
            };
    }

    private static string Addr(int rs, short imm, bool moved = false, uint reloc = 0)
    {
        if (moved) return $"0x{reloc:X8}u";
        if (rs == 0) return $"0x{(uint)(int)imm:X8}u";
        if (imm == 0) return R(rs);
        if (imm > 0) return $"({R(rs)} + 0x{(uint)imm:X}u)";
        return $"({R(rs)} - 0x{unchecked((uint)-(int)imm):X}u)";
    }

    private static string Cop0Read(int rd)
    {
        return rd switch
        {
            8 => "c.BadVAddr",
            12 => "c.SR",
            13 => "c.Cause",
            14 => "c.EPC",
            15 => "c.PRId",
            _ => $"0u /* COP0[{rd}] */"
        };
    }

    private static string Cop0Write(int rd, string val)
    {
        return rd switch
        {
            8 => $"c.BadVAddr = {val};",
            12 => $"c.SR = {val};",
            13 => $"c.Cause = {val};",
            14 => $"c.EPC = {val};",
            15 => $"c.PRId = {val};",
            _ => $"/* MTC0 r{rd} ignored */"
        };
    }

    public static string EmitSingle(MipsInstruction i, Dictionary<uint, uint>? relocations = null, bool precise = false)
    {
        string Operands(string body, int rs, int rt)
        {
            body = body.Replace("_s", R(rs)).Replace("_t", R(rt));
            return body.Contains("var _r") ? $"{{ {body} }}" : body;
        }

        var projection = precise ? "RecompOne.Runtime.Hle.NativeProjection" : "RecompOne.Runtime.Gte";
        uint reloc = 0;
        var moved = relocations != null && relocations.TryGetValue(i.Vram, out reloc);

        var op = i.Word >> 26;
        var fn = i.Word & 0x3F;
        int rs = i.Rs, rt = i.Rt, rd = i.Rd, sa = i.Sa;
        var imm = i.ImmS;
        var immU = i.ImmU;
        string RS = R(rs), RT = R(rt), RD = R(rd);

        if (op == 0)
            return (int)fn switch
            {
                0 => rd == 0 ? "" : sa == 0
                    ? $"{RD} = {RT};"
                    : $"{RD} = {RT} << {sa};",
                2 => rd == 0 ? "" : $"{RD} = {RT} >> {sa};",
                3 => rd == 0 ? "" : $"{RD} = (uint)((int){RT} >> {sa});",
                4 => rd == 0 ? "" : Operands($"{RD} = _t << (int)(_s & 31u);", rs, rt),
                6 => rd == 0 ? "" : Operands($"{RD} = _t >> (int)(_s & 31u);", rs, rt),
                7 => rd == 0 ? "" : Operands($"{RD} = (uint)((int)_t >> (int)(_s & 31u));", rs, rt),
                8 => "",
                9 => "",
                12 => "Bios.Syscall(c, m);",
                13 => "Bios.Break(c, m);",
                16 => rd == 0 ? "" : $"{RD} = c.HI;",
                17 => $"c.HI = {RS};",
                18 => rd == 0 ? "" : $"{RD} = c.LO;",
                19 => $"c.LO = {RS};",
                24 => Operands($"var _r = (long)(int)_s * (int)_t; c.LO = (uint)_r; c.HI = (uint)(_r >> 32);", rs, rt),
                25 => Operands($"var _r = (ulong)_s * _t; c.LO = (uint)_r; c.HI = (uint)(_r >> 32);", rs, rt),
                26 => rt == 0
                    ? "c.LO = 0u; c.HI = 0u;"
                    : Operands($"if (_t != 0u) {{ if ((int)_s == int.MinValue && (int)_t == -1) {{ c.LO = 0x80000000u; c.HI = 0u; }} else {{ c.LO = (uint)((int)_s / (int)_t); c.HI = (uint)((int)_s % (int)_t); }} }}", rs, rt),
                27 => rt == 0
                    ? "c.LO = 0u; c.HI = 0u;"
                    : Operands($"if (_t != 0u) {{ c.LO = _s / _t; c.HI = _s % _t; }}", rs, rt),
                32 or 33 => rd == 0 ? "" : rt == 0
                    ? $"{RD} = {RS};"
                    : rs == 0
                        ? $"{RD} = {RT};"
                        : Operands($"{RD} = _s + _t;", rs, rt),
                34 or 35 => rd == 0 ? "" : Operands($"{RD} = _s - _t;", rs, rt),
                36 => rd == 0 ? "" : Operands($"{RD} = _s & _t;", rs, rt),
                37 => rd == 0 ? "" : rs == 0
                    ? $"{RD} = {RT};"
                    : rt == 0
                        ? $"{RD} = {RS};"
                        : Operands($"{RD} = _s | _t;", rs, rt),
                38 => rd == 0 ? "" : Operands($"{RD} = _s ^ _t;", rs, rt),
                39 => rd == 0 ? "" : Operands($"{RD} = ~(_s | _t);", rs, rt),
                42 => rd == 0 ? "" : Operands($"{RD} = (int)_s < (int)_t ? 1u : 0u;", rs, rt),
                43 => rd == 0 ? "" : Operands($"{RD} = _s < _t ? 1u : 0u;", rs, rt),
                _ => UnknownInstr(i, $"SPECIAL fn=0x{fn:X2}")
            };

        if (op == 1) return ""; //handled in emitdelayslot

        if (op == 16) //cop0
        {
            var cop0rs = (i.Word >> 21) & 0x1F;
            if (cop0rs == 0) return rt == 0 ? "" : $"{RT} = {Cop0Read(rd)};";
            if (cop0rs == 4) return Cop0Write(rd, RT);
            if (cop0rs == 16 && fn == 16) return "c.SR = (c.SR & ~0xFu) | ((c.SR >> 2) & 0xFu);";
            return $"/* COP0 rs={cop0rs} */";
        }

        if (op == 18) //gte
        {
            var cop2rs = (i.Word >> 21) & 0x1F;
            if (cop2rs == 8) return "";
            if (((i.Word >> 25) & 1) == 1)
            {
                var cmd = i.Word;
                var sf = (cmd & (1u << 19)) != 0 ? "12" : "0";
                var lm = (cmd & (1u << 10)) != 0 ? "true" : "false";
                return (cmd & 0x3F) switch
                {
                    0x01 => $"{projection}.Rtps({sf}, {lm});",
                    0x06 => "RecompOne.Runtime.Gte.Nclip();",
                    0x0C => $"RecompOne.Runtime.Gte.Cross({sf}, {lm});",
                    0x10 => $"RecompOne.Runtime.Gte.Dpcs({sf}, {lm});",
                    0x11 => $"RecompOne.Runtime.Gte.Intpl({sf}, {lm});",
                    0x12 =>
                        $"RecompOne.Runtime.Gte.MvmvaOp({sf}, {lm}, {(cmd >> 17) & 3}, {(cmd >> 15) & 3}, {(cmd >> 13) & 3});",
                    0x13 => $"RecompOne.Runtime.Gte.NcdsOp({sf}, {lm});",
                    0x14 => $"RecompOne.Runtime.Gte.Cdp({sf}, {lm});",
                    0x16 => $"RecompOne.Runtime.Gte.NcdtOp({sf}, {lm});",
                    0x1B => $"RecompOne.Runtime.Gte.NccsOp({sf}, {lm});",
                    0x1C => $"RecompOne.Runtime.Gte.Cc({sf}, {lm});",
                    0x1E => $"RecompOne.Runtime.Gte.NcsOp({sf}, {lm});",
                    0x20 => $"RecompOne.Runtime.Gte.NctOp({sf}, {lm});",
                    0x28 => $"RecompOne.Runtime.Gte.Sqr({sf}, {lm});",
                    0x29 => $"RecompOne.Runtime.Gte.Dcpl({sf}, {lm});",
                    0x2A => $"RecompOne.Runtime.Gte.Dpct({sf}, {lm});",
                    0x2D => "RecompOne.Runtime.Gte.Avsz3();",
                    0x2E => "RecompOne.Runtime.Gte.Avsz4();",
                    0x30 => $"{projection}.Rtpt({sf}, {lm});",
                    0x3D => $"RecompOne.Runtime.Gte.Gpf({sf}, {lm});",
                    0x3E => $"RecompOne.Runtime.Gte.Gpl({sf}, {lm});",
                    0x3F => $"RecompOne.Runtime.Gte.NcctOp({sf}, {lm});",
                    _ => $"RecompOne.Runtime.Gte.Execute(0x{cmd:X8}u);"
                };
            }

            return cop2rs switch
            {
                0 => rt == 0 ? "" : $"{RT} = RecompOne.Runtime.Gte.Read({rd});",
                2 => rt == 0 ? "" : $"{RT} = RecompOne.Runtime.Gte.ReadControl({rd});",
                4 => $"RecompOne.Runtime.Gte.Write({rd}, {RT});",
                6 => rd <= 7 ? $"RecompOne.Runtime.Gte.WriteControl({rd}, {RT}, 0x{i.Vram:X8}u);" : $"RecompOne.Runtime.Gte.WriteControl({rd}, {RT});",
                _ => $"/* COP2 rs={cop2rs} */"
            };
        }

        if (op is 2 or 3 or 4 or 5 or 6 or 7)
            return ""; //thej umps and branches are handled in EmitWithDelaySlot to process with the delayslot

        return (int)op switch
        {
            8 or 9 => rt == 0 ? "" :
                moved ? $"{RT} = 0x{reloc:X8}u;" :
                rs == 0 ? $"{RT} = 0x{unchecked((uint)(int)imm):X8}u;" :
                imm >= 0
                    ? $"{RT} = {RS} + 0x{(uint)imm:X}u;"
                    : $"{RT} = {RS} - 0x{unchecked((uint)-(int)imm):X}u;",
            10 => rt == 0 ? "" : $"{RT} = (int){RS} < {(int)imm} ? 1u : 0u;",
            11 => rt == 0 ? "" : $"{RT} = {RS} < 0x{(uint)(int)imm:X8}u ? 1u : 0u;",
            12 => rt == 0 ? "" : $"{RT} = {RS} & 0x{immU:X4}u;",
            13 => rt == 0 ? "" :
                moved ? $"{RT} = 0x{reloc:X8}u;" :
                immU == 0 ? $"{RT} = {RS};" :
                $"{RT} = {RS} | 0x{immU:X4}u;",
            14 => rt == 0 ? "" : $"{RT} = {RS} ^ 0x{immU:X4}u;",
            15 => rt == 0 ? "" : $"{RT} = 0x{(uint)immU << 16:X8}u;",
            32 => rt == 0 ? "" : $"{RT} = (uint)(sbyte)mem.ReadU8({Addr(rs, imm, moved, reloc)});",
            33 => rt == 0 ? "" : $"{RT} = (uint)(short)mem.ReadU16({Addr(rs, imm, moved, reloc)});",
            34 => rt == 0 ? "" : $"{RT} = mem.ReadWordLeft({RT}, {Addr(rs, imm, moved, reloc)});",
            35 => rt == 0 ? "" : $"{RT} = mem.ReadU32({Addr(rs, imm, moved, reloc)});",
            36 => rt == 0 ? "" : $"{RT} = mem.ReadU8({Addr(rs, imm, moved, reloc)});",
            37 => rt == 0 ? "" : $"{RT} = mem.ReadU16({Addr(rs, imm, moved, reloc)});",
            38 => rt == 0 ? "" : $"{RT} = mem.ReadWordRight({RT}, {Addr(rs, imm, moved, reloc)});",
            40 => $"mem.WriteU8({Addr(rs, imm, moved, reloc)}, (byte){RT});",
            41 => $"mem.WriteU16({Addr(rs, imm, moved, reloc)}, (ushort){RT});",
            42 => $"mem.WriteWordLeft({Addr(rs, imm, moved, reloc)}, {RT});",
            43 => $"mem.WriteU32({Addr(rs, imm, moved, reloc)}, {RT});",
            46 => $"mem.WriteWordRight({Addr(rs, imm, moved, reloc)}, {RT});",
            50 =>
                $"{{ var _lw = mem.ReadU32({Addr(rs, imm, moved, reloc)}); RecompOne.Runtime.Gte.Write({rt}, _lw); }}",
            58 when precise && rt is >= 12 and <= 14 =>
                $"{{ var _sw = RecompOne.Runtime.Gte.Read({rt}); var _ad = {Addr(rs, imm, moved, reloc)}; mem.WriteU32(_ad, _sw); RecompOne.Runtime.Hle.NativeProjection.Store({rt}, _ad, _sw); }}",
            58 =>
                $"{{ var _sw = RecompOne.Runtime.Gte.Read({rt}); mem.WriteU32({Addr(rs, imm, moved, reloc)}, _sw); }}",
            _ => UnknownInstr(i, $"op=0x{op:X2}")
        };
    }

    private static string UnknownInstr(MipsInstruction i, string desc)
    {
        Console.WriteLine($"[Unknown] {desc} word=0x{i.Word:X8} @ 0x{i.Vram:X8}");
        return $"/* UNKOWN OP {desc} word=0x{i.Word:X8} @ 0x{i.Vram:X8} */";
    }

    //it control instructions that emmit their own delay slot, so the it shouldnt write it a second time again, its just a filter to not produce wrongfully
    public static bool SkipDelaySlot(MipsInstruction ctrl)
    {
        var op = ctrl.Word >> 26;
        var fn = ctrl.Word & 0x3F;
        if (op is 2 or 3) return true;
        if (op == 0 && fn is 8 or 9) return true;
        if (op == 4 && ctrl.Rs == ctrl.Rt) return true;
        if (op == 1 && (uint)ctrl.Rt is 0x10 or 0x11) return true;
        return false;
    }

    public static void EmitWithDelaySlot(StringBuilder sb, MipsInstruction ctrl, MipsInstruction? ds,
        FunctionContext ctx, string indent)
    {
        var op = ctrl.Word >> 26;
        var fn = ctrl.Word & 0x3F;
        int rs = ctrl.Rs, rt = ctrl.Rt, rd = ctrl.Rd;
        var pc = ctrl.Vram;
        string RS = R(rs), RT = R(rt);
        var ind2 = indent + "    ";

        void Ds()
        {
            if (ds == null) return;
            //fixes delay slot as branch target bug
            var line = EmitSingle(ds, ctx.Relocations, ctx.Precise);
            if (!string.IsNullOrEmpty(line)) sb.AppendLine(ctx.Trail(ds, $"{indent}{line}"));
        }

        void DsInline()
        {
            if (ds == null) return;
            var line = EmitSingle(ds, ctx.Relocations, ctx.Precise);
            if (!string.IsNullOrEmpty(line)) sb.AppendLine(ctx.Trail(ds, $"{ind2}{line}"));
        }

        void CallOrDispatch(uint addr, string ind)
        {
            if (ctx.KnownFunctions.TryGetValue(addr, out var name))
                sb.AppendLine(ctx.Trail(ctrl, $"{ind}{name}(c, m);"));
            else
                sb.AppendLine(ctx.Trail(ctrl, $"{ind}Dispatcher.Call(c, m, 0x{addr:X8}u);"));
        }

        bool InFunc(uint target)
        {
            return target >= ctx.FuncStart && target < ctx.FuncEnd;
        }

        void Conditional(string cond, uint target)
        {
            sb.AppendLine(ctx.Trail(ctrl, $"{indent}if ({cond}) {{"));
            DsInline();
            if (InFunc(target))
            {
                sb.AppendLine(ctx.Trail(ctrl, $"{ind2}goto L{target:X8};"));
            }
            else
            {
                CallOrDispatch(target, ind2);
                sb.AppendLine(ctx.Trail(ctrl, $"{ind2}return;"));
            }

            sb.AppendLine(ctx.Trail(ctrl, $"{indent}}}"));
        }

        if (op is 4 or 5 or 6 or 7)
        {
            var target = ctrl.BranchTarget;
            if (op == 4 && rs == rt)
            {
                Ds();
                if (InFunc(target))
                {
                    sb.AppendLine(ctx.Trail(ctrl, $"{indent}goto L{target:X8};"));
                }
                else
                {
                    CallOrDispatch(target, indent);
                    sb.AppendLine(ctx.Trail(ctrl, $"{indent}return;"));
                }

                return;
            }

            if (op == 5 && rs == rt) return;
            var cond = op switch
            {
                4 => $"{RS} == {RT}",
                5 => $"{RS} != {RT}",
                6 => $"(int){RS} <= 0",
                _ => $"(int){RS} > 0"
            };
            Conditional(cond, target);
            return;
        }

        if (op == 1)
        {
            var rtField = (uint)rt;
            var target = ctrl.BranchTarget;
            var link = rtField is 0x10 or 0x11;
            var cond = rtField switch
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
            else
            {
                Conditional(cond, target);
            }

            return;
        }

        if (op == 3)
        {
            var target = ctrl.JumpTarget;
            Ds();
            sb.AppendLine(ctx.Trail(ctrl, $"{indent}c.RA = 0x{pc + 8:X8}u;"));
            CallOrDispatch(target, indent);
            return;
        }

        if (op == 2)
        {
            var target = ctrl.JumpTarget;
            Ds();
            if (InFunc(target))
            {
                sb.AppendLine(ctx.Trail(ctrl, $"{indent}goto L{target:X8};"));
            }
            else
            {
                CallOrDispatch(target, indent);
                sb.AppendLine(ctx.Trail(ctrl, $"{indent}return;"));
            }

            return;
        }
        
        if (op == 0 && fn == 8)
        {
            Ds();
            if (rs == 31 || ctx.RaReturnJrs.Contains(pc))
            {
                if (ctx.LinkReturns.Count == 0)
                {
                    sb.AppendLine(ctx.Trail(ctrl, $"{indent}return;"));
                }
                else
                {
                    sb.AppendLine(ctx.Trail(ctrl, $"{indent}switch (c.RA)"));
                    sb.AppendLine(ctx.Trail(ctrl, $"{indent}{{"));
                    foreach (var entry in ctx.LinkReturns.OrderBy(a => a)) 
                        sb.AppendLine(ctx.Trail(ctrl, $"{indent}    case 0x{entry:X8}u: goto L{entry:X8};"));
                    sb.AppendLine(ctx.Trail(ctrl, $"{indent}    default: return;"));
                    sb.AppendLine(ctx.Trail(ctrl, $"{indent}}}"));
                }
            }
            else if (ctx.JumpTablesByJr.TryGetValue(pc, out var jtbl))
            {
                sb.AppendLine(ctx.Trail(ctrl, $"{indent}switch ({RS})"));
                sb.AppendLine(ctx.Trail(ctrl, $"{indent}{{"));
                foreach (var entry in jtbl.Entries.Distinct())
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
            var target = ctrl.BranchTarget;
            var cond = rt == 1 ? "RecompOne.Runtime.Gte.GetCondition()" : "!RecompOne.Runtime.Gte.GetCondition()";
            Conditional(cond, target);
            return;
        }
    }
}

public sealed class FunctionContext
{
    public bool Precise;
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
    public HashSet<uint> LinkReturns = [];
    public MipsInstruction[] AllInstructions = [];
    public Dictionary<uint, uint> Relocations = [];

    private const int CommentColumn = 64;

    public string Trail(MipsInstruction i, string line)
    {
        if (!AddressComments && !DisasmComments) return line;
        var body = DisasmComments ? $"/* 0x{i.Vram:X8}  {i.Disassemble()} */" : $"/* 0x{i.Vram:X8} */";
        return (line.Length < CommentColumn ? line.PadRight(CommentColumn) : line + "  ") + body;
    }

    public uint SkipNopPadding(uint addr) //faltru can end up in padding
    {
        if (AllInstructions.Length == 0) return addr;
        var baseAddr = AllInstructions[0].Vram;
        if (addr < baseAddr) return addr;

        var i = (int)((addr - baseAddr) / 4);
        while (i >= 0 && i < AllInstructions.Length && AllInstructions[i].IsNop)
        {
            addr += 4;
            i++;
        }

        return addr;
    }
}
