using LibObjectFile.Elf;
using RecompOne.Recompiler.Symbols;

namespace RecompOne.Recompiler.Elf;

public static class ElfReader
{
    public static FunctionInfo Read(string path)
    {
        using var stream = File.OpenRead(path);
        var elf = ElfFile.Read(stream);
        var info = new FunctionInfo();

        foreach (var seg in elf.Segments)
            if (seg.Type == ElfSegmentTypeCore.Load)
            {
                info.LoadAddress = (uint)seg.VirtualAddress;
                break;
            }

        ElfSymbolTable? symTab = null;

        foreach (var sec in elf.Sections)
        {
            if (sec is not ElfSymbolTable st) continue;
            symTab = st;
            break;
        }

        if (symTab != null)
        {
            uint minFuncAddr = uint.MaxValue, maxFuncEnd = 0;
            var notypeCandidates = new List<FunctionEntry>();

            foreach (var sym in symTab.Entries)
            {
                if (sym.Size == 0) continue;
                uint addr = (uint)sym.Value, size = (uint)sym.Size;

                if (sym.Type == ElfSymbolType.Function)
                {
                    info.Functions.Add(new FunctionEntry { Name = sym.Name.ToString(), Address = addr, Size = size });
                    if (addr < minFuncAddr) minFuncAddr = addr;
                    if (addr + size > maxFuncEnd) maxFuncEnd = addr + size;
                }
                else if (sym.Type == ElfSymbolType.NoType)
                {
                    notypeCandidates.Add(new FunctionEntry { Name = sym.Name.ToString(), Address = addr, Size = size });
                }
            }

            info.NoTypeSymbols = notypeCandidates;

            if (minFuncAddr != uint.MaxValue)
                foreach (var c in notypeCandidates)
                    if (c.Address >= minFuncAddr && c.Address < maxFuncEnd)
                        info.Functions.Add(c);
        }

        foreach (var sec in elf.Sections)
        {
            var alloc = (sec.Flags & ElfSectionFlags.Alloc) != 0;
            var exec = (sec.Flags & ElfSectionFlags.Executable) != 0;
            if (!alloc) continue;

            var name = sec.Name.ToString();

            if (exec)
            {
                info.TextBase = (uint)sec.VirtualAddress;
                info.TextData = ReadBytes(sec);
            }
            else if (sec.Type == ElfSectionType.NoBits)
            {
                info.DataSections.Add(new DataSection
                    { Name = name, Va = (uint)sec.VirtualAddress, IsZero = true, ZeroSize = (uint)sec.Size });
            }
            else
            {
                info.DataSections.Add(new DataSection
                    { Name = name, Va = (uint)sec.VirtualAddress, Data = ReadBytes(sec) });
            }
        }

        return info;
    }

    private static byte[] ReadBytes(ElfSection sec)
    {
        if (sec is ElfStreamSection ss)
        {
            var buf = new byte[ss.Stream.Length];
            ss.Stream.Position = 0;
            ss.Stream.ReadExactly(buf);
            return buf;
        }

        return [];
    }
}