using RecompOne.Recompiler.Disasm;

namespace RecompOne.Recompiler.Analysis;

public static class LabelManager
{
    public static HashSet<uint> Collect(MipsFunction func)
    {
        var labels = new HashSet<uint>();

        foreach (var instr in func.Instructions)
        {
            if (!instr.HasDelaySlot) continue;

            uint op = instr.Word >> 26;

            if (op is 1 or 4 or 5 or 6 or 7) //these: REGIMM, BEQ, BNE, BLEZ, BGTZ
            {
                uint t = instr.BranchTarget;
                if (t >= func.Start && t < func.End) labels.Add(t);
            }
            else if (op == 2) //internal function goto
            {
                uint t = instr.JumpTarget;
                if (t >= func.Start && t < func.End) labels.Add(t);
            }
        }

        foreach (var jtbl in func.JumpTables)
            foreach (uint entry in jtbl.Entries)
                if (entry >= func.Start && entry < func.End)
                    labels.Add(entry);

        return labels;
    }
    //back edge is branch that goes backward, usually for loop, this will be used to dispatch some of the irqs that cant be properly hled
    public static HashSet<uint> CollectBackEdges(MipsFunction func)
    {
        var heads = new HashSet<uint>();

        foreach (var instr in func.Instructions)
        {
            if (!instr.HasDelaySlot) continue;

            uint op = instr.Word >> 26;
            uint t;

            if (op is 1 or 4 or 5 or 6 or 7) t = instr.BranchTarget;
            else if (op == 2) t = instr.JumpTarget;
            else continue;

            if (t >= func.Start && t < func.End && t <= instr.Vram) heads.Add(t);
        }

        return heads;
    }
}
