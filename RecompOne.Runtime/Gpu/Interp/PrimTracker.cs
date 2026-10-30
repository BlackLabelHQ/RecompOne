using System.Numerics;
using System.Runtime.InteropServices;
using RecompOne.Runtime.Hle;

namespace RecompOne.Runtime.Interp;

internal sealed class PrimTracker
{
    private const float FirstStep = 8f;
    private const float FirstSizeStep = 2f;
    private const float Settle = 2f;
    private const float SizeSettle = 0.5f;

    private const float Fixed = 65536f;

    private const float SizeTolerance = 2f;

    private readonly record struct Entry(int First, int Count, Vector2 Center, float Half, uint Material);

    public enum Mode : byte
    {
        None,
        Screen,
        Space
    }

    private readonly Dictionary<uint, Entry> _previous = new();
    private readonly Dictionary<uint, Entry> _currentEntries = new();
    private Dictionary<uint, Vector2> _motion = new();
    private Dictionary<uint, Vector2> _nextMotion = new();
    private int[] _sources = [];
    private Vector3[] _offsets = [];
    private Mode[] _modes = [];
    private readonly List<float> _flowX = [];
    private readonly List<float> _flowY = [];
    private FrameGraph? _current;
    private FrameGraph? _previousGraph;
    private FrameGraph? _motionGraph;
    private int _currentTris = -1;
    private int _previousTris = -1;

    public int Tracked { get; private set; }
    public int Identified { get; private set; }
    public int Paired { get; private set; }
    public int Rejected { get; private set; }
    public int Spatial { get; private set; }

    public void Build(FrameGraph current, FrameGraph previous)
    {
        if (current == _current && previous == _previousGraph && current.Tris.Count == _currentTris &&
            previous.Tris.Count == _previousTris) return;

        _current = current;
        _previousGraph = previous;
        _currentTris = current.Tris.Count;
        _previousTris = previous.Tris.Count;
        Tracked = Identified = Paired = Rejected = Spatial = 0;

        if (_sources.Length < _currentTris)
        {
            _sources = new int[_currentTris];
            _offsets = new Vector3[_currentTris];
            _modes = new Mode[_currentTris];
        }

        Array.Fill(_sources, -1, 0, _currentTris);
        Array.Clear(_modes, 0, _currentTris);

        if (_motionGraph != previous) _motion.Clear();
        _nextMotion.Clear();
        _motionGraph = current;

        Collect(previous, _previous);
        Collect(current, _currentEntries);
        Identified = _currentEntries.Count;
        if (_previous.Count == 0)
        {
            _motion.Clear();
            return;
        }

        var flow = Flow();
        foreach (var (id, entry) in _currentEntries)
        {
            if (!_previous.TryGetValue(id, out var before) || before.Count != entry.Count) continue;
            if (before.Material != entry.Material || !SimilarSize(before.Half, entry.Half)) continue;

            Paired++;
            var motion = entry.Center - before.Center;
            var half = MathF.Max(entry.Half, before.Half);
            var known = _motion.TryGetValue(id, out var last);
            if (!Plausible(known, known ? last : flow, motion, half))
            {
                if (!known) _nextMotion[id] = motion;
                Rejected++;
                continue;
            }

            _nextMotion[id] = motion;
            var spatial = true;
            for (var k = 0; k < entry.Count; k++)
            {
                _sources[entry.First + k] = before.First + k;
                if (!Offset(current, previous, entry.First + k, before.First + k, out _offsets[entry.First + k]))
                    spatial = false;
            }

            var mode = spatial ? Mode.Space : Mode.Screen;
            for (var k = 0; k < entry.Count; k++) _modes[entry.First + k] = mode;
            if (spatial) Spatial++;
            Tracked++;
        }

        (_motion, _nextMotion) = (_nextMotion, _motion);
    }

    public Mode TryBlend(int index, in TriRecord tri, FrameGraph previous, float weight, out TriRecord blended)
    {
        blended = tri;
        if (index >= _currentTris || _modes[index] == Mode.None || previous != _previousGraph) return Mode.None;

        if (_modes[index] == Mode.Space)
        {
            ref readonly var transform = ref CollectionsMarshal.AsSpan(_current!.Transforms)[tri.Transform - 1];
            var offset = _offsets[index] * (1f - weight);
            if (Shift(ref blended.A, offset, in transform) && Shift(ref blended.B, offset, in transform) &&
                Shift(ref blended.C, offset, in transform))
                return Mode.Space;
            blended = tri;
        }

        ref var before = ref CollectionsMarshal.AsSpan(previous.Tris)[_sources[index]];
        blended.A = Blend(in before.A, in tri.A, weight);
        blended.B = Blend(in before.B, in tri.B, weight);
        blended.C = Blend(in before.C, in tri.C, weight);
        return Mode.Screen;
    }

    private static bool Offset(FrameGraph current, FrameGraph previous, int now, int then, out Vector3 offset)
    {
        offset = default;
        ref var after = ref CollectionsMarshal.AsSpan(current.Tris)[now];
        ref var before = ref CollectionsMarshal.AsSpan(previous.Tris)[then];
        if (!HasSpace(in after) || !HasSpace(in before)) return false;

        ref var to = ref CollectionsMarshal.AsSpan(current.Transforms)[after.Transform - 1];
        ref var from = ref CollectionsMarshal.AsSpan(previous.Transforms)[before.Transform - 1];
        Span<float> inverse = stackalloc float[9];
        if (!Invert(in from, inverse)) return false;
        var was = ToWorld(in from, inverse, before.A.Camera);
        if (!Invert(in to, inverse)) return false;
        var now3 = ToWorld(in to, inverse, after.A.Camera);
        offset = Rotate(in to, was - now3);
        return true;
    }

    private static bool HasSpace(in TriRecord tri)
    {
        return tri.Transform > 0 && tri.Transform == tri.TransformB && tri.Transform == tri.TransformC &&
               tri.A.Camera.Z > 0f && tri.A.Camera == tri.B.Camera && tri.A.Camera == tri.C.Camera &&
               tri.A.Depth > 0f && tri.B.Depth > 0f && tri.C.Depth > 0f;
    }

    private static bool Invert(in TransformRecord m, Span<float> inverse)
    {
        float a = m.R0 / 4096f, b = m.R1 / 4096f, c = m.R2 / 4096f;
        float d = m.R3 / 4096f, e = m.R4 / 4096f, f = m.R5 / 4096f;
        float g = m.R6 / 4096f, h = m.R7 / 4096f, i = m.R8 / 4096f;
        var det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
        if (MathF.Abs(det) < 1e-6f) return false;

        var s = 1f / det;
        inverse[0] = (e * i - f * h) * s;
        inverse[1] = (c * h - b * i) * s;
        inverse[2] = (b * f - c * e) * s;
        inverse[3] = (f * g - d * i) * s;
        inverse[4] = (a * i - c * g) * s;
        inverse[5] = (c * d - a * f) * s;
        inverse[6] = (d * h - e * g) * s;
        inverse[7] = (b * g - a * h) * s;
        inverse[8] = (a * e - b * d) * s;
        return true;
    }

    private static Vector3 ToWorld(in TransformRecord m, ReadOnlySpan<float> inverse, Vector3 view)
    {
        var x = view.X - m.TX;
        var y = view.Y - m.TY;
        var z = view.Z - m.TZ;
        return new Vector3(
            inverse[0] * x + inverse[1] * y + inverse[2] * z,
            inverse[3] * x + inverse[4] * y + inverse[5] * z,
            inverse[6] * x + inverse[7] * y + inverse[8] * z);
    }

    private static Vector3 Rotate(in TransformRecord m, Vector3 v)
    {
        return new Vector3(
            (m.R0 * v.X + m.R1 * v.Y + m.R2 * v.Z) / 4096f,
            (m.R3 * v.X + m.R4 * v.Y + m.R5 * v.Z) / 4096f,
            (m.R6 * v.X + m.R7 * v.Y + m.R8 * v.Z) / 4096f);
    }

    private static bool Shift(ref HleVertex vertex, Vector3 offset, in TransformRecord m)
    {
        var depth = vertex.Depth;
        if (!float.IsFinite(depth) || depth <= 0f || m.H <= 0) return false;

        var target = depth + offset.Z;
        if (!float.IsFinite(target) || target < 1f) return false;

        var ofx = m.OFX / Fixed;
        var ofy = m.OFY / Fixed;
        var vx = (vertex.X - ofx) * depth / m.H + offset.X;
        var vy = (vertex.Y - ofy) * depth / m.H + offset.Y;
        vertex.X = ofx + vx * m.H / target;
        vertex.Y = ofy + vy * m.H / target;
        if (vertex.HasGteZ) vertex.Z = target;
        vertex.Depth = target;
        vertex.Camera += offset;
        return true;
    }

    public void Clear()
    {
        _current = _previousGraph = _motionGraph = null;
        _currentTris = _previousTris = -1;
    }

    private static bool Plausible(bool known, Vector2 expected, Vector2 motion, float half)
    {
        var change = (motion - expected).Length();
        return known
            ? change <= MathF.Max(Settle, half * SizeSettle)
            : change <= MathF.Max(FirstStep, half * FirstSizeStep);
    }

    private Vector2 Flow()
    {
        _flowX.Clear();
        _flowY.Clear();
        foreach (var (id, entry) in _currentEntries)
        {
            if (!_previous.TryGetValue(id, out var before)) continue;
            var motion = entry.Center - before.Center;
            _flowX.Add(motion.X);
            _flowY.Add(motion.Y);
        }

        if (_flowX.Count == 0) return Vector2.Zero;
        _flowX.Sort();
        _flowY.Sort();
        return new Vector2(_flowX[_flowX.Count / 2], _flowY[_flowY.Count / 2]);
    }

    private static void Collect(FrameGraph graph, Dictionary<uint, Entry> into)
    {
        into.Clear();
        var tris = CollectionsMarshal.AsSpan(graph.Tris);
        var i = 0;
        while (i < tris.Length)
        {
            var id = Id(in tris[i]);
            if (id == 0)
            {
                i++;
                continue;
            }

            var first = i;
            var min = new Vector2(float.MaxValue);
            var max = new Vector2(float.MinValue);
            while (i < tris.Length && Id(in tris[i]) == id)
            {
                ref var tri = ref tris[i];
                Extend(in tri.A, ref min, ref max);
                Extend(in tri.B, ref min, ref max);
                Extend(in tri.C, ref min, ref max);
                i++;
            }

            var size = max - min;
            var flags = tris[first].Flags;
            var material = flags.Textured ? flags.TPage & 0x19Fu | (uint)flags.Clut << 16 | 0x80000000u : 0u;
            into.TryAdd(id, new Entry(first, i - first, (min + max) * 0.5f, MathF.Max(size.X, size.Y) * 0.5f, material));
        }
    }

    private static bool SimilarSize(float a, float b)
    {
        if (a < 1f && b < 1f) return true;
        var ratio = MathF.Max(a, b) / MathF.Max(MathF.Min(a, b), 0.5f);
        return ratio <= SizeTolerance;
    }

    private static uint Id(in TriRecord tri)
    {
        var id = tri.A.Prim;
        return id != 0 && tri.B.Prim == id && tri.C.Prim == id && !tri.A.Native ? id : 0u;
    }

    private static void Extend(in HleVertex v, ref Vector2 min, ref Vector2 max)
    {
        min = Vector2.Min(min, new Vector2(v.X, v.Y));
        max = Vector2.Max(max, new Vector2(v.X, v.Y));
    }

    private static HleVertex Blend(in HleVertex before, in HleVertex after, float weight)
    {
        var v = after;
        v.X = before.X + (after.X - before.X) * weight;
        v.Y = before.Y + (after.Y - before.Y) * weight;
        if (before.HasGteZ && after.HasGteZ) v.Z = before.Z + (after.Z - before.Z) * weight;
        if (before.Depth > 0f && after.Depth > 0f) v.Depth = before.Depth + (after.Depth - before.Depth) * weight;
        return v;
    }
}
