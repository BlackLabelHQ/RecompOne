using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace RecompOne.Runtime.Config;

public static class Settings
{
    private const string FilePath = "settings.json";
    private const long SaveDelayMs = 500;

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly object _gate = new();
    private static readonly SortedDictionary<string, JsonNode?> _values = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, IOption> _options = new(StringComparer.Ordinal);
    private static SystemSettings _system = new();
    private static bool _loaded;
    private static bool _dirty;
    private static long _dirtyAt;

    public static SystemSettings System
    {
        get
        {
            EnsureLoaded();
            return _system;
        }
    }

    public static event Action<string>? Changed;

    public static bool FirstBoot { get; private set; }

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_gate)
        {
            if (_loaded) return;
            _loaded = true;
            Read();
            AppDomain.CurrentDomain.ProcessExit += static (_, _) => SaveIfDirty();
        }
    }

    public static bool Has(string key)
    {
        EnsureLoaded();
        lock (_gate)
        {
            return _values.ContainsKey(key);
        }
    }

    public static T Get<T>(string key, T fallback)
    {
        EnsureLoaded();
        lock (_gate)
        {
            return _values.TryGetValue(key, out var node) && TryConvert<T>(node, out var value) ? value : fallback;
        }
    }

    public static void Set<T>(string key, T value)
    {
        EnsureLoaded();
        var node = JsonSerializer.SerializeToNode(value, Json);
        IOption? option;
        lock (_gate)
        {
            if (_values.TryGetValue(key, out var current) && JsonNode.DeepEquals(current, node)) return;
            _values[key] = node;
            option = _options.GetValueOrDefault(key);
            MarkDirtyLocked();
        }

        Notify(key, option);
    }

    public static bool Remove(string key)
    {
        EnsureLoaded();
        IOption? option;
        lock (_gate)
        {
            if (!_values.Remove(key)) return false;
            option = _options.GetValueOrDefault(key);
            MarkDirtyLocked();
        }

        Notify(key, option);
        return true;
    }

    public static IReadOnlyList<string> Keys(string prefix = "")
    {
        EnsureLoaded();
        lock (_gate)
        {
            return _values.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        }
    }

    public static void MarkDirty()
    {
        lock (_gate)
        {
            MarkDirtyLocked();
        }
    }

    public static void Save()
    {
        EnsureLoaded();
        lock (_gate)
        {
            var options = new JsonObject();
            foreach (var (key, node) in _values) options[key] = node?.DeepClone();

            var root = new JsonObject
            {
                ["system"] = JsonSerializer.SerializeToNode(_system, Json),
                ["options"] = options
            };

            try
            {
                var temp = FilePath + ".tmp";
                File.WriteAllText(temp, root.ToJsonString(Json));
                File.Move(temp, FilePath, true);
                _dirty = false;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"[Settings] cannot write {FilePath}: {e.Message}");
            }
        }
    }

    public static void SaveIfDirty()
    {
        if (_dirty) Save();
    }

    internal static void SaveIfDue()
    {
        if (_dirty && Environment.TickCount64 - _dirtyAt >= SaveDelayMs) Save();
    }

    internal static void Register(IOption option)
    {
        lock (_gate)
        {
            _options[option.Key] = option;
        }
    }

    internal static IOption? Find(string key)
    {
        lock (_gate)
        {
            return _options.GetValueOrDefault(key);
        }
    }

    private static void MarkDirtyLocked()
    {
        _dirty = true;
        _dirtyAt = Environment.TickCount64;
    }

    private static void Notify(string key, IOption? option)
    {
        option?.Reload();
        Changed?.Invoke(key);
    }

    private static void Read()
    {
        FirstBoot = !File.Exists(FilePath);
        JsonNode? root = null;
        if (File.Exists(FilePath))
            try
            {
                root = JsonNode.Parse(File.ReadAllText(FilePath));
            }
            catch (Exception e) when (e is JsonException or IOException)
            {
                Console.Error.WriteLine($"[Settings] {FilePath} is unreadable, using defaults: {e.Message}");
            }

        if (root is JsonObject obj)
        {
            _system = TryDeserialize<SystemSettings>(obj["system"]) ?? new SystemSettings();
            if (obj["options"] is JsonObject options)
                foreach (var (key, node) in options)
                    _values[key] = node?.DeepClone();
        }

        _system.Normalize();
    }

    private static T? TryDeserialize<T>(JsonNode? node) where T : class
    {
        if (node == null) return null;
        try
        {
            return node.Deserialize<T>(Json);
        }
        catch (JsonException e)
        {
            Console.Error.WriteLine($"[Settings] invalid {typeof(T).Name}: {e.Message}");
            return null;
        }
    }

    internal static bool TryConvert<T>(JsonNode? node, out T value)
    {
        value = default!;
        if (node is not JsonValue scalar)
            return node != null && TryDeserializeValue(node, out value);

        if (typeof(T) == typeof(string))
        {
            value = (T)(object)(scalar.TryGetValue<string>(out var text) ? text : scalar.ToJsonString());
            return true;
        }

        if (scalar.TryGetValue<string>(out var raw) && Parse(typeof(T), raw) is T parsed)
        {
            value = parsed;
            return true;
        }

        return TryDeserializeValue(node, out value);
    }

    private static bool TryDeserializeValue<T>(JsonNode node, out T value)
    {
        value = default!;
        try
        {
            var result = node.Deserialize<T>(Json);
            if (result == null) return false;
            value = result;
            return true;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private static object? Parse(Type type, string raw)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (target.IsEnum) return Enum.TryParse(target, raw, true, out var e) ? e : null;
        if (target == typeof(bool)) return bool.TryParse(raw, out var b) ? b : null;
        if (target == typeof(int))
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
        if (target == typeof(long))
            return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : null;
        if (target == typeof(float))
            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : null;
        if (target == typeof(double))
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
        return null;
    }
}
