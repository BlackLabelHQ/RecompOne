using RecompOne.Recompiler.Analysis;

namespace RecompOne.Recompiler.AutoConfigure;

public sealed record MatchReport(int Named, int Ambiguous, int Total, int ByLayout, List<string> Libraries);

public static class SdkMatcher
{
    public static MatchReport Name(List<MipsFunction> functions, byte[] text, uint textBase, SignatureDb db)
    {
        var words = new uint[text.Length / 4];
        for (var i = 0; i < words.Length; i++) words[i] = BitConverter.ToUInt32(text, i * 4);

        var found = new List<Signature>();
        var anchors = new Dictionary<(string Sdk, string Object), long>();
        var pending = new List<(MipsFunction Fn, int Start, int Count)>();

        int named = 0, ambiguous = 0, byLayout = 0;
        var libraries = new HashSet<string>();

        foreach (var fn in functions)
        {
            if (fn.Start < textBase || fn.End <= fn.Start) continue;

            var start = (int)(fn.Start - textBase) / 4;
            var count = (int)(fn.End - fn.Start) / 4;
            if (start < 0 || count <= 0 || start + count > words.Length) continue;

            db.Collect(words.AsSpan(start, count), found);
            if (found.Count == 0) continue;

            var name = found[0].Name;
            var single = true;
            foreach (var hit in found)
                if (hit.Name != name)
                {
                    single = false;
                    break;
                }

            if (!single)
            {
                ambiguous++;
                pending.Add((fn, start, count));
                continue;
            }

            fn.Name = name;
            named++;
            libraries.Add(found[0].Library);

            foreach (var hit in found)
            foreach (var sdk in hit.Sdk)
                anchors[(sdk, hit.Object)] = fn.Start - hit.Offset;
        }

        foreach (var (fn, start, count) in pending)
        {
            db.Collect(words.AsSpan(start, count), found);

            string? resolved = null;
            Signature? source = null;
            var conflict = false;

            foreach (var hit in found)
            {
                var placed = false;
                foreach (var sdk in hit.Sdk)
                    if (anchors.TryGetValue((sdk, hit.Object), out var seat) && seat == fn.Start - hit.Offset)
                    {
                        placed = true;
                        break;
                    }

                if (!placed) continue;
                if (resolved != null && resolved != hit.Name)
                {
                    conflict = true;
                    break;
                }

                resolved = hit.Name;
                source = hit;
            }

            if (conflict || resolved == null) continue;

            fn.Name = resolved;
            named++;
            byLayout++;
            ambiguous--;
            libraries.Add(source!.Library);
        }

        return new MatchReport(named, ambiguous, functions.Count, byLayout, libraries.OrderBy(l => l).ToList());
    }
}
