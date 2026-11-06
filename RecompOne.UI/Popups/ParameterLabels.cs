using System.Text.RegularExpressions;

namespace RecompOne.UI;

//atempt to sanitize labels didnt work as i expected but meh it is good enough
public static class ParameterLabels
{
    private static readonly char[] Decoration = [' ', '-', '=', '*', '#', '~', '_', ':', '|'];
    private const float SharedPrefixShare = 0.75f;
    private const int SharedPrefixMinimum = 3;

    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    public static Dictionary<string, string> Build(IReadOnlyList<SlangParameter> parameters)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in parameters)
            labels[parameter.Name] = Clean(string.IsNullOrWhiteSpace(parameter.Description) ? parameter.Name : parameter.Description);

        var prefix = SharedPrefix(parameters.Where(p => p.Max > p.Min).Select(p => labels[p.Name]).ToList());
        if (prefix == null) return labels;

        foreach (var parameter in parameters)
        {
            var label = labels[parameter.Name];
            if (!label.StartsWith(prefix + " ", StringComparison.Ordinal)) continue;
            var rest = Clean(label[(prefix.Length + 1)..]);
            if (rest.Length > 0) labels[parameter.Name] = Capitalize(rest);
        }

        return labels;
    }

    private static string Clean(string text)
    {
        return Spaces.Replace(text, " ").Trim(Decoration);
    }

    private static string? SharedPrefix(List<string> labels)
    {
        if (labels.Count < SharedPrefixMinimum) return null;

        var best = labels.Select(l => l.Split(' ', 2))
            .Where(parts => parts.Length == 2)
            .GroupBy(parts => parts[0], StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .FirstOrDefault();

        if (best == null || best.Count() < Math.Max(SharedPrefixMinimum, labels.Count * SharedPrefixShare)) return null;
        return best.Key;
    }

    private static string Capitalize(string text)
    {
        return char.IsLower(text[0]) ? char.ToUpperInvariant(text[0]) + text[1..] : text;
    }
}
