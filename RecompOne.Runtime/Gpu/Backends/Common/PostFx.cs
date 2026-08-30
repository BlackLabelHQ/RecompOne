namespace RecompOne.Runtime.Hle;

//obj for pfx dat
public static class PostFx
{
    private static readonly object _gate = new();
    private static string? _source;
    private static int _version;
    private static readonly Dictionary<string, float> _params = new(StringComparer.Ordinal);
    private static int _paramVersion;

    public static string? Name { get; private set; }

    public static string? Error { get; internal set; }

    public static bool Active
    {
        get
        {
            lock (_gate)
            {
                return _source != null;
            }
        }
    }

    public static int Version
    {
        get
        {
            lock (_gate)
            {
                return _version;
            }
        }
    }

    public static string? Source
    {
        get
        {
            lock (_gate)
            {
                return _source;
            }
        }
    }

    public static int ParamVersion
    {
        get
        {
            lock (_gate)
            {
                return _paramVersion;
            }
        }
    }

    public static void Set(string? fragmentSource, string? name = null)
    {
        lock (_gate)
        {
            _source = string.IsNullOrWhiteSpace(fragmentSource) ? null : fragmentSource;
            Name = _source == null ? null : name;
            Error = null;
            _params.Clear();
            _version++;
            _paramVersion++;
        }
    }

    public static void Clear()
    {
        Set(null);
    }

    public static void SetParam(string name, float value)
    {
        lock (_gate)
        {
            if (_params.TryGetValue(name, out var old) && old == value) return;
            _params[name] = value;
            _paramVersion++;
        }
    }

    public static void SetParams(IEnumerable<KeyValuePair<string, float>> values)
    {
        lock (_gate)
        {
            foreach (var kv in values) _params[kv.Key] = kv.Value;
            _paramVersion++;
        }
    }

    internal static (string Name, float Value)[] SnapshotParams()
    {
        lock (_gate)
        {
            var arr = new (string, float)[_params.Count];
            var i = 0;
            foreach (var kv in _params) arr[i++] = (kv.Key, kv.Value);
            return arr;
        }
    }

    public const string FragmentHeader = """
                                         #version 330 core
                                         in vec2 vUv;
                                         out vec4 oColor;
                                         uniform sampler2D uTex;
                                         uniform vec2 uTexSize;
                                         uniform vec2 uOutputSize;
                                         uniform float uTime;
                                         uniform int uFrame;
                                         """;
}