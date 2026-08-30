namespace RecompOne.Recompiler.Disasm;

public static class MipsDisasm
{
    public static MipsInstruction[] Disassemble(byte[] code, uint baseAddr)
    {
        var count = code.Length / 4;
        var instrs = new MipsInstruction[count];
        for (var i = 0; i < count; i++)
        {
            var off = i * 4;
            var word = (uint)(code[off] | (code[off + 1] << 8) | (code[off + 2] << 16) | (code[off + 3] << 24));
            instrs[i] = new MipsInstruction(word, baseAddr + (uint)off);
        }

        return instrs;
    }
}