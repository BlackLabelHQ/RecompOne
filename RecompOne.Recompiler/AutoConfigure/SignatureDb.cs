using System.Text.Json;
using System.Text.Json.Serialization;

namespace RecompOne.Recompiler.AutoConfigure;

public sealed class SignatureFile
{
    [JsonPropertyName("functions")] public SignatureFunction[] Functions { get; set; } = [];
}

public sealed class SignatureFunction
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("library")] public string Library { get; set; } = "";
    [JsonPropertyName("hle")] public bool Hle { get; set; }
    [JsonPropertyName("variants")] public SignatureVariant[] Variants { get; set; } = [];
}

public sealed class SignatureVariant
{
    [JsonPropertyName("size")] public int Size { get; set; }
    [JsonPropertyName("words")] public string[] Words { get; set; } = [];
}

public sealed record Signature(string Name, string Library, bool Hle, uint[] Value, uint[] Mask, int Fixed)
{
    public int Words => Value.Length;

    public bool Matches(ReadOnlySpan<uint> body)
    {
        if (body.Length != Value.Length) return false;
        for (var i = 0; i < Value.Length; i++)
            if ((body[i] & Mask[i]) != Value[i])
                return false;
        return true;
    }
}

public sealed class SignatureDb
{
    private readonly Dictionary<int, List<Signature>> _bySize = new();

    public int Count { get; private set; }
    public int Names => _bySize.Values.SelectMany(v => v).Select(s => s.Name).Distinct().Count();

    public static SignatureDb Load(string path)
    {
        var db = new SignatureDb();
        db.Add(path);

        var local = Path.Combine(Path.GetDirectoryName(path)!,
            Path.GetFileNameWithoutExtension(path) + ".local.json");
        if (File.Exists(local)) db.Add(local);

        return db;
    }

    private void Add(string path)
    {
        var options = new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true
        };

        using var stream = File.OpenRead(path);
        var file = JsonSerializer.Deserialize<SignatureFile>(stream, options)
                   ?? throw new InvalidDataException($"failed to parse signatures {path}");

        foreach (var fn in file.Functions)
        foreach (var variant in fn.Variants)
        {
            if (variant.Words.Length == 0) continue;
            var signature = Compile(fn, variant);
            if (!_bySize.TryGetValue(signature.Words, out var list))
                _bySize[signature.Words] = list = [];
            list.Add(signature);
            Count++;
        }
    }

    private static Signature Compile(SignatureFunction fn, SignatureVariant variant)
    {
        var value = new uint[variant.Words.Length];
        var mask = new uint[variant.Words.Length];
        var known = 0;

        for (var i = 0; i < variant.Words.Length; i++)
        {
            var text = variant.Words[i];
            uint v = 0, m = 0;
            for (var n = 0; n < 8; n++)
            {
                var c = n < text.Length ? text[n] : '?';
                v <<= 4;
                m <<= 4;
                if (c == '?' || c == '*') continue;
                v |= (uint)Convert.ToInt32(c.ToString(), 16);
                m |= 0xF;
                known++;
            }

            value[i] = v & m;
            mask[i] = m;
        }

        return new Signature(fn.Name, fn.Library, fn.Hle, value, mask, known);
    }

    public Signature? Lookup(ReadOnlySpan<uint> body, out bool ambiguous)
    {
        ambiguous = false;
        if (!_bySize.TryGetValue(body.Length, out var candidates)) return null;

        Signature? best = null;
        foreach (var candidate in candidates)
        {
            if (!candidate.Matches(body)) continue;
            if (best == null)
            {
                best = candidate;
                continue;
            }

            if (best.Name == candidate.Name)
            {
                if (candidate.Fixed > best.Fixed) best = candidate;
                continue;
            }

            ambiguous = true;
            return null;
        }

        return best;
    }
}