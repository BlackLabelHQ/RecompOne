using RecompOne.Recompiler.Analysis;

namespace RecompOne.Recompiler.AutoConfigure;

public sealed record MatchReport(int Named, int Ambiguous, int Total, List<string> Libraries);

public static class SdkMatcher
{
    public static MatchReport Name(List<MipsFunction> functions, byte[] text, uint textBase, SignatureDb db)
    {
        var words = new uint[text.Length / 4];
        for (var i = 0; i < words.Length; i++) words[i] = BitConverter.ToUInt32(text, i * 4);

        int named = 0, ambiguous = 0;
        var libraries = new HashSet<string>();

        foreach (var fn in functions)
        {
            if (fn.Start < textBase || fn.End <= fn.Start) continue;

            var start = (int)(fn.Start - textBase) / 4;
            var count = (int)(fn.End - fn.Start) / 4;
            if (start < 0 || count <= 0 || start + count > words.Length) continue;

            var hit = db.Lookup(words.AsSpan(start, count), out var clash);
            if (clash) ambiguous++;
            if (hit == null) continue;

            fn.Name = hit.Name;
            named++;
            libraries.Add(hit.Library);
        }

        return new MatchReport(named, ambiguous, functions.Count, libraries.OrderBy(l => l).ToList());
    }
}