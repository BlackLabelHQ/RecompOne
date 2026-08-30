using System.Text.RegularExpressions;
using RecompOne.Recompiler.Symbols;

namespace RecompOne.Recompiler.Map;

public static partial class MapReader
{
    public static FunctionInfo Read(string path)
    {
        var info = new FunctionInfo();
        var pending = new List<(uint Addr, string Name)>();
        var chunkActive = false;
        uint chunkBase = 0, chunkSize = 0;

        void FinalizeChunk()
        {
            if (!chunkActive || pending.Count == 0)
            {
                pending.Clear();
                return;
            }

            var groups = pending.GroupBy(p => p.Addr).OrderBy(g => g.Key).ToList();
            for (var i = 0; i < groups.Count; i++)
            {
                var addr = groups[i].Key;
                var end = i + 1 < groups.Count ? groups[i + 1].Key : chunkBase + chunkSize;
                var size = end > addr ? end - addr : 0;
                if (size == 0) continue;

                foreach (var sym in groups[i])
                    info.Functions.Add(new FunctionEntry { Name = sym.Name, Address = addr, Size = size });
            }

            pending.Clear();
        }

        foreach (var line in File.ReadLines(path))
        {
            var symMatch = SymbolLine().Match(line);
            if (chunkActive && symMatch.Success)
            {
                pending.Add((Convert.ToUInt32(symMatch.Groups[1].Value, 16), symMatch.Groups[2].Value));
                continue;
            }

            FinalizeChunk();
            chunkActive = false;

            var textMatch = TextSectionLine().Match(line);
            if (textMatch.Success)
            {
                chunkBase = Convert.ToUInt32(textMatch.Groups[1].Value, 16);
                chunkSize = Convert.ToUInt32(textMatch.Groups[2].Value, 16);
                chunkActive = chunkSize > 0;
                continue;
            }

            if (info.LoadAddress == 0)
            {
                var outMatch = OutputSectionLine().Match(line);
                if (outMatch.Success)
                {
                    var addr = Convert.ToUInt32(outMatch.Groups[1].Value, 16);
                    if (addr >= 0x80000000) info.LoadAddress = addr;
                }
            }
        }

        FinalizeChunk();

        info.TextBase = info.LoadAddress;
        return info;
    }

    [GeneratedRegex(@"^\s+0x([0-9a-fA-F]+)\s+(\S+)\s*$")]
    private static partial Regex SymbolLine();

    [GeneratedRegex(@"^ \.text\s+0x([0-9a-fA-F]+)\s+0x([0-9a-fA-F]+)\s+\S")]
    private static partial Regex TextSectionLine();

    [GeneratedRegex(@"^\.\S+\s+0x([0-9a-fA-F]+)\s+0x[0-9a-fA-F]+")]
    private static partial Regex OutputSectionLine();
}