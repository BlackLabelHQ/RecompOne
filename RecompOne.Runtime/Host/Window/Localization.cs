using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace RecompOne.Runtime.Host.Window;

//push strings, fallback to english
public static class Localization
{
    public readonly record struct Language(string Code, string Name);

    private const string Resource = "RecompOne.Runtime.Host.Window.Assets.languages.json";
    private const string FallbackCode = "en";

    private static readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> _codes = [];

    private static readonly Dictionary<string, Dictionary<string, string>> _tables =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> _warned = [];

    private static bool _baseLoaded;

    private static Dictionary<string, string> _active = [];
    private static Dictionary<string, string> _fallback = [];

    public static string CurrentCode { get; private set; } = FallbackCode;

    public static IReadOnlyList<Language> Languages
    {
        get
        {
            var list = new List<Language>(_codes.Count);
            foreach (var code in _codes)
                list.Add(new Language(code, _names.TryGetValue(code, out var name) ? name : code));
            return list;
        }
    }

    public static event Action? Changed;

    public static void Load()
    {
        EnsureBase();
        SetLanguage(Config.ConfigManager.View.Language);
    }

    private static void EnsureBase()
    {
        if (_baseLoaded) return;
        _baseLoaded = true;
        MergeEmbedded(Assembly.GetExecutingAssembly(), Resource);
    }

    public static bool MergeEmbedded(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            Console.Error.WriteLine($"[Localization] {resourceName} not found in {assembly.GetName().Name}");
            return false;
        }

        using var reader = new StreamReader(stream);
        Merge(reader.ReadToEnd());
        return true;
    }

    public static void Merge(string json)
    {
        EnsureBase();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("languages", out var languages))
                foreach (var language in languages.EnumerateObject())
                    Declare(language.Name, language.Value.GetString() ?? language.Name);

            if (!root.TryGetProperty("strings", out var strings)) return;

            foreach (var entry in strings.EnumerateObject())
            foreach (var translation in entry.Value.EnumerateObject())
            {
                if (translation.Value.GetString() is not { } text) continue;
                Table(translation.Name)[entry.Name] = text;
            }

            RefreshTables();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[Localization] failed to parse language data: {e.Message}");
        }
    }

    public static void SetLanguage(string? code)
    {
        var resolved = Resolve(code) ?? Resolve(CultureInfo.CurrentUICulture.Name) ?? FallbackCode;
        if (resolved == CurrentCode && _active.Count > 0) return;

        CurrentCode = resolved;
        RefreshTables();
        Changed?.Invoke();
    }

    public static string T(string key)
    {
        if (_active.TryGetValue(key, out var text)) return text;

        if (_fallback.TryGetValue(key, out text))
        {
            Warn($"\"{key}\" has no {CurrentCode} translation, falling back to {FallbackCode}");
            return text;
        }

        Warn($"\"{key}\" is missing from every language table");
        return key;
    }

    public static string T(string key, params object?[] args)
    {
        var format = T(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }

    private static void Warn(string message)
    {
        if (!_warned.Add(message)) return;
        Console.Error.WriteLine($"[Localization] {message}");
    }

    private static Dictionary<string, string> Table(string code)
    {
        if (_tables.TryGetValue(code, out var table)) return table;
        Declare(code, code);
        return _tables[code];
    }

    private static void Declare(string code, string name)
    {
        if (!_tables.ContainsKey(code))
        {
            _tables[code] = new Dictionary<string, string>(StringComparer.Ordinal);
            _codes.Add(code);
        }

        _names[code] = name;
    }

    private static void RefreshTables()
    {
        _active = _tables.TryGetValue(CurrentCode, out var active) ? active : [];
        _fallback = _tables.TryGetValue(FallbackCode, out var fallback) ? fallback : [];
        _warned.Clear();
    }

    private static string? Resolve(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        foreach (var known in _codes)
            if (string.Equals(known, code, StringComparison.OrdinalIgnoreCase))
                return known;

        var prefix = code.Split('-')[0];
        foreach (var known in _codes)
            if (string.Equals(known.Split('-')[0], prefix, StringComparison.OrdinalIgnoreCase))
                return known;

        return null;
    }
}