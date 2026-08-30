using RecompOne.Recompiler.Config;
using RecompOne.Recompiler.Disasm;

namespace RecompOne.Recompiler.Analysis;

public static class SymbolRelocator
{
    public static Dictionary<uint, uint> Plan(List<MipsFunction> funcs, RelocationEntry[] entries, string overlayName)
    {
        var active = entries.Where(e => e.MatchesOverlay(overlayName)).ToArray();
        var sites = new Dictionary<uint, uint>();
        if (active.Length == 0) return sites;

        var counts = new int[active.Length];

        foreach (var func in funcs)
        {
            var hi = new uint[32];
            var hiSet = new bool[32];

            foreach (var instr in func.Instructions)
            {
                var op = instr.Word >> 26;
                int rs = instr.Rs, rt = instr.Rt;

                if (op == 0x0F)
                {
                    if (rt != 0)
                    {
                        hi[rt] = (uint)instr.ImmU << 16;
                        hiSet[rt] = true;
                    }

                    continue;
                }

                var consumes = op is 0x09 or 0x0D || (op >= 0x20 && op <= 0x2E) || op is 0x32 or 0x3A;
                if (consumes && rs != 0 && hiSet[rs])
                {
                    var addr = op == 0x0D ? hi[rs] + instr.ImmU : (uint)(hi[rs] + instr.ImmS);
                    var hit = Match(active, addr);
                    if (hit >= 0)
                    {
                        sites[instr.Vram] = active[hit].ToAddress + (addr - active[hit].FromAddress);
                        counts[hit]++;
                    }
                }

                if (Writes(instr, out var dst) && dst != 0) hiSet[dst] = false;
            }
        }

        for (var i = 0; i < active.Length; i++)
            Console.WriteLine($"[Recompiler] relocated {active[i].Label} at {counts[i]} site(s) in {overlayName}");

        return sites;
    }

    private static int Match(RelocationEntry[] entries, uint addr)
    {
        for (var i = 0; i < entries.Length; i++)
            if (addr >= entries[i].FromAddress && addr < entries[i].FromAddress + entries[i].ByteSize)
                return i;
        return -1;
    }

    //an lui value only survives until something else write that register
    private static bool Writes(MipsInstruction i, out int dst)
    {
        dst = 0;
        var op = i.Word >> 26;

        if (op == 0)
        {
            var fn = i.Word & 0x3F;
            if (fn is 8 or 9 or 16 or 18 or 24 or 25 or 26 or 27)
                return false;
            dst = i.Rd;
            return true;
        }

        if (op == 1)
        {
            dst = 31;
            return (uint)i.Rt is 0x10 or 0x11;
        }

        if (op == 3)
        {
            dst = 31;
            return true;
        }

        if (op is 0x08 or 0x09 or 0x0A or 0x0B or 0x0C or 0x0D or 0x0E or 0x0F)
        {
            dst = i.Rt;
            return true;
        }

        if (op >= 0x20 && op <= 0x26)
        {
            dst = i.Rt;
            return true;
        }

        if (op is 0x10 or 0x11 or 0x12 or 0x13)
        {
            dst = i.Rt;
            return i.Rs is 0 or 2;
        }

        return false;
    }
}