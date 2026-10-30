using System.Numerics;

namespace RecompOne.Runtime.Hle;

//servers for sending float data for the gpu for the game patches

public readonly record struct NativeVertex(Vector3 Camera, Vector2 Screen, int Transform)
{
    public bool IgnoreDepth { get; init; }
}

public readonly record struct SceneLight(Vector3 Position, Vector3 Color, float Radius);

public static class NativeGeometry
{
    private static readonly Lock LightGate = new();
    private static List<SceneLight> _lights = [];
    private static List<SceneLight> _finishedLights = [];

    private readonly record struct Entry(uint Word, NativeVertex Vertex);
    private static readonly Dictionary<uint, Entry> Vertices = new();
    private static Dictionary<uint, NativeVertex> _anchors = new();
    private static Dictionary<uint, NativeVertex> _staleAnchors = new();
    private static HashSet<uint> _conflicts = [];
    private static HashSet<uint> _staleConflicts = [];
    private const float AnchorTolerance = 0.01f;
    private static bool _enabled;
    public static bool DepthBuffer { get; set; }
    
    public static bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (value) return;
            Vertices.Clear();
            _anchors.Clear();
            _staleAnchors.Clear();
            _conflicts.Clear();
            _staleConflicts.Clear();
            lock (LightGate)
            {
                _lights.Clear();
                _finishedLights.Clear();
            }
        }
    }

    public static void Light(in SceneLight light)
    {
        if (Enabled) _lights.Add(light);
    }

    public static void SnapshotLights(List<SceneLight> into)
    {
        into.Clear();
        lock (LightGate) into.AddRange(_finishedLights);
    }

    public static bool HasAnchors => _anchors.Count > 0 || _staleAnchors.Count > 0;

    public static void Anchor(uint word, in NativeVertex vertex)
    {
        if (!Enabled) return;
        if (_anchors.TryGetValue(word, out var existing) && !Same(in existing, in vertex)) _conflicts.Add(word);
        _anchors[word] = vertex;
    }

    public static bool TryAnchor(int x, int y, out NativeVertex vertex)
    {
        var word = (uint)(ushort)x | (uint)(ushort)y << 16;
        if (_conflicts.Contains(word) || _staleConflicts.Contains(word))
        {
            vertex = default;
            return false;
        }

        var current = _anchors.TryGetValue(word, out vertex);
        if (!_staleAnchors.TryGetValue(word, out var stale)) return current;
        if (!current)
        {
            vertex = stale;
            return true;
        }

        return Same(in vertex, in stale);
    }

    private static bool Same(in NativeVertex a, in NativeVertex b)
    {
        return Vector3.Distance(a.Camera, b.Camera) <= MathF.Max(a.Camera.Z, b.Camera.Z) * AnchorTolerance;
    }

    public static void EndFrame()
    {
        (_anchors, _staleAnchors) = (_staleAnchors, _anchors);
        _anchors.Clear();
        (_conflicts, _staleConflicts) = (_staleConflicts, _conflicts);
        _conflicts.Clear();
        lock (LightGate)
        {
            (_lights, _finishedLights) = (_finishedLights, _lights);
            _lights.Clear();
        }
    }
    
    public static void Store(uint address, uint word, in NativeVertex vertex)
    {
        if (!Enabled) return;
        Vertices[address & 0x1FFFFFFFu] = new Entry(word, vertex);
    }
    
    public static void Store(uint address, uint word, Vector3 camera, Vector2 screen, int transform)
    {
        var vertex = new NativeVertex(camera, screen, transform);
        Store(address, word, in vertex);
    }
    
    public static bool TryTake(uint address, uint word, out NativeVertex vertex)
    {
        vertex = default;
        var key = address & 0x1FFFFFFFu;
        if (!Enabled || !Vertices.TryGetValue(key, out var entry) || entry.Word != word)
            return false;
        Vertices.Remove(key);
        vertex = entry.Vertex;
        return float.IsFinite(vertex.Camera.X) && float.IsFinite(vertex.Camera.Y) &&
            float.IsFinite(vertex.Camera.Z) && vertex.Camera.Z > 0f &&
            float.IsFinite(vertex.Screen.X) && float.IsFinite(vertex.Screen.Y);
    }
}
