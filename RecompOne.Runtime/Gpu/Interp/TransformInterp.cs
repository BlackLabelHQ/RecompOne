using RecompOne.Runtime.Hle;

namespace RecompOne.Runtime.Interp;

internal sealed class TransformInterp
{
    private const float Scale = 1f / 4096f;
    private const float MagnitudeThreshold = 10f;
    private const float ScreenTolerance = 8f;
    private const float Reach = 1024f;
    private const int Doubts = 2;
    private const float MaxTurn = 0.5f;
    private const float OrdPenalty = 64f;
    private const float KeyPenalty = 2048f;
    private const float UvPenalty = 48f;
    private const float IdMaxTurn = 1.5f;
    private const int PageCount = 32; //performance improv
    
    private struct Pose
    {
        public bool Valid;
        public float M0, M1, M2, M3, M4, M5, M6, M7, M8;
        public float TX, TY, TZ;
        public float CX, CY, CZ;
        public float H, OFX, OFY;
    }
    
    private Pose[] _poses = [];
    private int[] _bestCur = [];
    private int[] _bestPrev = [];
    private float[] _scoreCur = [];
    private float[] _scorePrev = [];
    private float[] _secondCur = [];
    private float[] _secondPrev = [];
    private readonly List<int>[] _previousByPage = CreatePageBuckets(); 
    private readonly List<int> _pageCandidates = []; //performance improv, the ida is to instead of lookup poly by poly check only plausible candidates
    private int[] _candidateMarks = [];
    private int _candidateGeneration;
    

    public int IdMatched { get; private set; }

    public int HeuristicMatched { get; private set; }

    public int Rejected { get; private set; }




    public int CameraJumps { get; private set; }

    public int CameraOnly { get; private set; }
    
    public int Groups { get; private set; }
    
    
    
    
    
    
    private readonly Dictionary<uint, int> _order = new();
    private readonly Dictionary<uint, int> _byId = new();
    private bool[] _done = [];
    private bool[] _taken = [];
    
    private const int EndpointStride = 22;
    private const int WorldOffset = 9;
    private const int CameraOffset = WorldOffset + EndpointStride;
    private const int MotionOffset = CameraOffset + EndpointStride;
    private const int PreparedStride = MotionOffset + 12;
    private const float CameraCut = 2048f;
    private const float NearDepth = 1f;
    private const float CameraTurn = 0.6f;
    private float[] _prepared = [];
    private bool[] _preparedValid = [];
    private bool[] _split = [];

    private void Prepare(FrameGraph current, FrameGraph previous)
    {
        var count = current.Transforms.Count;
        if (_preparedValid.Length < count)
        {
            _preparedValid = new bool[count];
            _split = new bool[count];
            _prepared = new float[count * PreparedStride];
        }
        
        Span<float> a = stackalloc float[9];
        Span<float> b = stackalloc float[9];
        CameraJumps = CameraOnly = 0;
        MapCameras(current, previous);
        for (var i = 0; i < count; i++)
        {
            var group = current.Transforms[i];
            _preparedValid[i] = false;
            _split[i] = false;
            if (group.Kind == TransformKind.Ignored) continue;
            var data = _prepared.AsSpan(i * PreparedStride, PreparedStride);
            
            if (group.Match < 0 || !group.Lerp)
            {
                if (!group.HasCamera || !_cameras.TryGetValue(CameraKey(in group), out var source)) continue;
                var eye = previous.Transforms[source];
                if (CameraJump(in eye, in group) || !Invert(in group, data[..9]) || !Static(in eye, in group, data)) continue;
                _split[i] = true;
                _preparedValid[i] = true;
                CameraOnly++;
                continue;
            }
            
            var from = previous.Transforms[group.Match];
            if (!Invert(in group, data[..9])) continue;
            
            if (group.HasCamera && from.HasCamera)
            {
                if (CameraJump(in from, in group))
                {
                    CameraJumps++;
                    continue;
                }
                if (Split(in from, in group, data))
                {
                    _split[i] = true;
                    _preparedValid[i] = true;
                    continue;
                }
            }
            
            Rotation(in from, a);
            Rotation(in group, b);
            _preparedValid[i] = Endpoints(a, b, data.Slice(WorldOffset, EndpointStride));
        }
    }
    
    private readonly Dictionary<ulong, int> _cameras = new();
    
    private void MapCameras(FrameGraph current, FrameGraph previous)
    {
        _cameras.Clear();
        for (var i = 0; i < current.Transforms.Count; i++)
        {
            var group = current.Transforms[i];
            if (!group.HasCamera || group.Match < 0) continue;
            
            var from = previous.Transforms[group.Match];
            if (!from.HasCamera) continue;
            
            _cameras.TryAdd(CameraKey(in group), group.Match);
        }
    }
    
    private static ulong CameraKey(in TransformRecord m)
    {
        var hash = 1469598103934665603ul;
        void Add(int value) => hash = (hash ^ (uint)value) * 1099511628211ul;
        Add(m.C0); Add(m.C1); Add(m.C2); Add(m.C3); Add(m.C4); Add(m.C5); Add(m.C6); Add(m.C7); Add(m.C8);
        Add(m.CX); Add(m.CY); Add(m.CZ);
        return hash;
    }
    
    private static bool Static(in TransformRecord eye, in TransformRecord to, Span<float> data)
    {
        Span<float> cameraA = stackalloc float[9];
        Span<float> cameraB = stackalloc float[9];
        Span<float> inverseB = stackalloc float[9];
        Span<float> rotation = stackalloc float[9];
        Span<float> world = stackalloc float[9];
        
        Camera(in eye, cameraA);
        Camera(in to, cameraB);
        if (!Invert(cameraB, inverseB)) return false;
        
        Rotation(in to, rotation);
        Multiply(inverseB, rotation, world);
        if (!Endpoints(world, world, data.Slice(WorldOffset, EndpointStride))) return false;
        if (!Endpoints(cameraA, cameraB, data.Slice(CameraOffset, EndpointStride))) return false;
        
        var motion = data.Slice(MotionOffset, 12);
        World(inverseB, in to, motion[..3]);
        motion[..3].CopyTo(motion.Slice(3, 3));
        motion[6] = eye.CX;
        motion[7] = eye.CY;
        motion[8] = eye.CZ;
        motion[9] = to.CX;
        motion[10] = to.CY;
        motion[11] = to.CZ;
        return true;
    }
    
    internal static bool CameraJump(in TransformRecord from, in TransformRecord to)
    {
        var dx = (float)to.CX - from.CX;
        var dy = (float)to.CY - from.CY;
        var dz = (float)to.CZ - from.CZ;
        if (MathF.Sqrt(dx * dx + dy * dy + dz * dz) > CameraCut) return true;
        
        var turn =
            Row(to.C0, to.C1, to.C2, from.C0, from.C1, from.C2) +
            Row(to.C3, to.C4, to.C5, from.C3, from.C4, from.C5) +
            Row(to.C6, to.C7, to.C8, from.C6, from.C7, from.C8);
        return turn > CameraTurn;
    }
    
    private static bool Split(in TransformRecord from, in TransformRecord to, Span<float> data)
    {
        Span<float> cameraA = stackalloc float[9];
        Span<float> cameraB = stackalloc float[9];
        Span<float> inverseA = stackalloc float[9];
        Span<float> inverseB = stackalloc float[9];
        Span<float> rotation = stackalloc float[9];
        Span<float> worldA = stackalloc float[9];
        Span<float> worldB = stackalloc float[9];
        
        Camera(in from, cameraA);
        Camera(in to, cameraB);
        if (!Invert(cameraA, inverseA) || !Invert(cameraB, inverseB)) return false;
        
        Rotation(in from, rotation);
        Multiply(inverseA, rotation, worldA);
        Rotation(in to, rotation);
        Multiply(inverseB, rotation, worldB);
        
        if (!Endpoints(worldA, worldB, data.Slice(WorldOffset, EndpointStride))) return false;
        if (!Endpoints(cameraA, cameraB, data.Slice(CameraOffset, EndpointStride))) return false;
        
        var motion = data.Slice(MotionOffset, 12);
        World(inverseA, in from, motion[..3]);
        World(inverseB, in to, motion.Slice(3, 3));
        motion[6] = from.CX;
        motion[7] = from.CY;
        motion[8] = from.CZ;
        motion[9] = to.CX;
        motion[10] = to.CY;
        motion[11] = to.CZ;
        return true;
    }
    
    private static void World(ReadOnlySpan<float> inverse, in TransformRecord m, Span<float> result)
    {
        result[0] = inverse[0] * m.TX + inverse[1] * m.TY + inverse[2] * m.TZ + m.CX;
        result[1] = inverse[3] * m.TX + inverse[4] * m.TY + inverse[5] * m.TZ + m.CY;
        result[2] = inverse[6] * m.TX + inverse[7] * m.TY + inverse[8] * m.TZ + m.CZ;
    }
    
    private static void Rotation(in TransformRecord m, Span<float> result)
    {
        result[0] = m.R0 * Scale; result[1] = m.R1 * Scale; result[2] = m.R2 * Scale;
        result[3] = m.R3 * Scale; result[4] = m.R4 * Scale; result[5] = m.R5 * Scale;
        result[6] = m.R6 * Scale; result[7] = m.R7 * Scale; result[8] = m.R8 * Scale;
    }
    
    private static void Camera(in TransformRecord m, Span<float> result)
    {
        result[0] = m.C0 * Scale; result[1] = m.C1 * Scale; result[2] = m.C2 * Scale;
        result[3] = m.C3 * Scale; result[4] = m.C4 * Scale; result[5] = m.C5 * Scale;
        result[6] = m.C6 * Scale; result[7] = m.C7 * Scale; result[8] = m.C8 * Scale;
    }
    
    private static void Multiply(ReadOnlySpan<float> a, ReadOnlySpan<float> b, Span<float> result)
    {
        for (var row = 0; row < 3; row++)
        for (var col = 0; col < 3; col++)
            result[row * 3 + col] = a[row * 3] * b[col] + a[row * 3 + 1] * b[3 + col] + a[row * 3 + 2] * b[6 + col];
    }

    private static bool Endpoints(ReadOnlySpan<float> from, ReadOnlySpan<float> to, Span<float> data)
    {
        Span<float> rotation = stackalloc float[9];
        var qa = data[..4];
        var qb = data.Slice(4, 4);

        if (!Decompose(from, rotation, data.Slice(10, 6))) return false;
        Quaternion(rotation, qa);
        if (!Decompose(to, rotation, data.Slice(16, 6))) return false;
        Quaternion(rotation, qb);

        var dot = qa[0] * qb[0] + qa[1] * qb[1] + qa[2] * qb[2] + qa[3] * qb[3];
        if (dot < 0f)
        {
            dot = -dot;
            for (var i = 0; i < 4; i++) qb[i] = -qb[i];
        }

        var angle = dot > 0.9995f ? 0f : MathF.Acos(MathF.Min(dot, 1f));
        data[8] = angle;
        data[9] = angle > 0f ? 1f / MathF.Sin(angle) : 0f;
        return true;
    }

    public void Match(FrameGraph current, FrameGraph previous)
    {
        
        IdMatched = HeuristicMatched = Rejected = 0;
        Groups = current.Transforms.Count;
        
        Order(current);
        Order(previous);
        
        EnsureBestCapacity(current.Transforms.Count, previous.Transforms.Count);
        MatchIds(current, previous);
        Assign(current, previous);
        Prepare(current, previous);
    }
    
    private void Order(FrameGraph graph)
    {
        _order.Clear();
        
        for (var i = 0; i < graph.Transforms.Count; i++)
        {
            var group = graph.Transforms[i];
            _order.TryGetValue(group.Key, out var seen);
            group.Ord = seen;
            _order[group.Key] = seen + 1;
            graph.Transforms[i] = group;
        }
    }
    
    private void MatchIds(FrameGraph current, FrameGraph previous)
    {
        var here = current.Transforms.Count;
        var there = previous.Transforms.Count;
        Array.Clear(_done, 0, here);
        Array.Clear(_taken, 0, there);

        _byId.Clear();
        for (var j = 0; j < there; j++)
        {
            var group = previous.Transforms[j];
            if (group.Id == 0 || group.Kind == TransformKind.Ignored) continue;
            _byId[group.Id] = _byId.ContainsKey(group.Id) ? -1 : j;
        }

        for (var i = 0; i < here; i++)
        {
            var group = current.Transforms[i];
            if (group.Kind == TransformKind.Ignored)
            {
                _done[i] = true;
                continue;
            }

            if (group.Kind == TransformKind.Explicit) _done[i] = true;
            if (group.Id == 0 || !_byId.TryGetValue(group.Id, out var j) || j < 0 || _taken[j]) continue;

            var from = previous.Transforms[j];
            if (from.Kind != group.Kind) continue;

            _done[i] = true;
            if (!Compatible(in group, in from))
            {
                Rejected++;
                continue;
            }

            _taken[j] = true;
            Accept(current, previous, i, j);
            IdMatched++;
        }
    }

    private static bool Compatible(in TransformRecord current, in TransformRecord previous)
    {
        if (current.H != previous.H) return false;
        if (Determinant(in current) * Determinant(in previous) < 0f) return false;

        var dx = current.TX - (previous.TX + previous.Vx);
        var dy = current.TY - (previous.TY + previous.Vy);
        var dz = current.TZ - (previous.TZ + previous.Vz);
        if (MathF.Sqrt(dx * dx + dy * dy + dz * dz) > Reach) return false;

        var orientation =
            Row(current.R0, current.R1, current.R2, previous.R0, previous.R1, previous.R2) +
            Row(current.R3, current.R4, current.R5, previous.R3, previous.R4, previous.R5) +
            Row(current.R6, current.R7, current.R8, previous.R6, previous.R7, previous.R8);

        return orientation <= IdMaxTurn;
    }

    private void Assign(FrameGraph current, FrameGraph previous)
    {
        var here = current.Transforms.Count;
        var there = previous.Transforms.Count;

        ResetBest(here, there);
        BuildPageIndex(previous);

        for (var i = 0; i < here; i++)
        {
            if (_done[i]) continue;
            var currentGroup = current.Transforms[i];
            var pageCount = AddPageCandidates(currentGroup.Pages);
            for (var candidateIndex = 0; candidateIndex < pageCount; candidateIndex++)
            {
                var j = _pageCandidates[candidateIndex];
                if (_taken[j]) continue;
                var previousGroup = previous.Transforms[j];
                if (previousGroup.Kind != TransformKind.Auto) continue;
                if (!CanMatch(in currentGroup, in previousGroup)) continue;
                if (!Score(in currentGroup, in previousGroup, out var score)) continue;

                if (score < _scoreCur[i])
                {
                    _secondCur[i] = _scoreCur[i];
                    _scoreCur[i] = score;
                    _bestCur[i] = j;
                }

                else if (score < _secondCur[i]) _secondCur[i] = score;

                if (score < _scorePrev[j])
                {
                    _secondPrev[j] = _scorePrev[j];
                    _scorePrev[j] = score;
                    _bestPrev[j] = i;
                }
                else if (score < _secondPrev[j]) _secondPrev[j] = score;
            }
        }

        for (var i = 0; i < here; i++)
        {
            if (_done[i]) continue;
            var j = _bestCur[i];
            if (j < 0 || _bestPrev[j] != i ||
                _secondCur[i] - _scoreCur[i] <= 0.001f ||
                _secondPrev[j] - _scorePrev[j] <= 0.001f) continue;
            
            Accept(current, previous, i, j);
            HeuristicMatched++;
        }

    }

    private static List<int>[] CreatePageBuckets()
    {
        var buckets = new List<int>[PageCount];
        for (var i = 0; i < buckets.Length; i++) buckets[i] = [];
        return buckets;
    }

    private void BuildPageIndex(FrameGraph previous)
    {
        foreach (var bucket in _previousByPage) bucket.Clear();

        for (var index = 0; index < previous.Transforms.Count; index++)
        {
            var pages = previous.Transforms[index].Pages;
            while (pages != 0)
            {
                var page = System.Numerics.BitOperations.TrailingZeroCount(pages);
                _previousByPage[page].Add(index);
                pages &= pages - 1;
            }
        }

        if (_candidateMarks.Length < previous.Transforms.Count)
            _candidateMarks = new int[previous.Transforms.Count];
    }

    private int AddPageCandidates(uint pages)
    {
        _pageCandidates.Clear();
        if (pages == 0) return 0;

        if (++_candidateGeneration == int.MaxValue)
        {
            Array.Clear(_candidateMarks);
            _candidateGeneration = 1;
        }

        var pageBits = pages;
        while (pageBits != 0)
        {
            var page = System.Numerics.BitOperations.TrailingZeroCount(pageBits);
            foreach (var index in _previousByPage[page])
            {
                if (_candidateMarks[index] == _candidateGeneration) continue;
                _candidateMarks[index] = _candidateGeneration;
                _pageCandidates.Add(index);
            }

            pageBits &= pageBits - 1;
        }

        return _pageCandidates.Count;
    }

    private void EnsureBestCapacity(int here, int there)
    {
        if (_bestCur.Length < here)
        {
            _bestCur = new int[here];
            _scoreCur = new float[here];
            _secondCur = new float[here];
            _done = new bool[here];
        }

        if (_bestPrev.Length < there)
        {
            _bestPrev = new int[there];
            _scorePrev = new float[there];
            _secondPrev = new float[there];
            _taken = new bool[there];
        }
    }

    private void ResetBest(int here, int there)
    {
        for (var i = 0; i < here; i++)
        {
            _bestCur[i] = -1;
            _scoreCur[i] = _secondCur[i] = float.MaxValue;
        }

        for (var i = 0; i < there; i++)
        {
            _bestPrev[i] = -1;
            _scorePrev[i] = _secondPrev[i] = float.MaxValue;
        }
    }
    
    private void Accept(FrameGraph current, FrameGraph previous, int index, int partner)
    {
        var group = current.Transforms[index];
        var from = previous.Transforms[partner];
        
        group.Match = partner;
        group.Vx = group.TX - from.TX;
        group.Vy = group.TY - from.TY;
        group.Vz = group.TZ - from.TZ;
        
        Project(ref group);
        
        if (group.Screened && from.Screened)
        {
            group.Vsx = group.Sx - from.Sx;
            group.Vsy = group.Sy - from.Sy;
        }
        
        var steady = Continues(in group, in from);
        
        group.Held = steady ? 0 : from.Held + 1;
        group.Lerp = steady || group.Held < Doubts;
        
        current.Transforms[index] = group;
    }
    
    private static bool CanMatch(in TransformRecord current, in TransformRecord previous)
    {
        if ((current.Pages & previous.Pages) == 0u) return false;

        var few = Math.Min(current.Tris, previous.Tris);
        var many = Math.Max(current.Tris, previous.Tris);
        if (current.Key != previous.Key && many > few * 2) return false;

        if (current.H != previous.H) return false;
        return Determinant(in current) * Determinant(in previous) >= 0f;
    }

    private static bool Score(in TransformRecord current, in TransformRecord previous, out float score)
    {
        score = float.MaxValue;
        
        var dx = current.TX - (previous.TX + previous.Vx);
        var dy = current.TY - (previous.TY + previous.Vy);
        var dz = current.TZ - (previous.TZ + previous.Vz);
        
        var distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        if (distance > Reach) return false;
        
        var orientation =
            Row(current.R0, current.R1, current.R2, previous.R0, previous.R1, previous.R2) +
            Row(current.R3, current.R4, current.R5, previous.R3, previous.R4, previous.R5) +
            Row(current.R6, current.R7, current.R8, previous.R6, previous.R7, previous.R8);
        
        if (orientation > MaxTurn) return false;
        
        score = distance + orientation * 4096f + MathF.Abs(current.Ord - previous.Ord) * OrdPenalty;
        if (current.Key != previous.Key) score += KeyPenalty;
        score += Uv(in current, in previous) * UvPenalty;
        
        return true;
    }
    
    private static float Uv(in TransformRecord current, in TransformRecord previous)
    {
        if (current.U0 > current.U1 || previous.U0 > previous.U1) return 0f;
        
        return MathF.Abs(current.U0 - previous.U0) + MathF.Abs(current.U1 - previous.U1) +
               MathF.Abs(current.V0 - previous.V0) + MathF.Abs(current.V1 - previous.V1);
    }
    
    private static void Project(ref TransformRecord group)
    {
        group.Screened = false;
        if (group.H <= 0 || group.TZ < group.H / 2) return;
        
        group.Sx = group.OFX / 65536f + (float)group.TX * group.H / group.TZ;
        group.Sy = group.OFY / 65536f + (float)group.TY * group.H / group.TZ;
        group.Screened = true;
    }
    
    private static float Screen(in TransformRecord group)
    {
        if (group.Screened) return MathF.Sqrt(group.Vsx * group.Vsx + group.Vsy * group.Vsy);
        
        var speed = MathF.Sqrt(group.Vx * group.Vx + group.Vy * group.Vy + group.Vz * group.Vz);
        var depth = MathF.Max(MathF.Abs(group.TZ), group.H);
        
        return depth <= 0f ? 0f : speed * group.H / depth;
    }
    
    private static bool Continues(in TransformRecord current, in TransformRecord previous)
    {
        const float epsilon = 1e-3f;
        
        if (!current.Screened || !previous.Screened) return true;
        
        var speed = MathF.Sqrt(current.Vsx * current.Vsx + current.Vsy * current.Vsy);
        if (speed < ScreenTolerance) return true;
        
        var before = MathF.Sqrt(previous.Vsx * previous.Vsx + previous.Vsy * previous.Vsy);
        if (before < epsilon) return true;
        
        var dot = (current.Vsx * previous.Vsx + current.Vsy * previous.Vsy) / (speed * before);
        
        return speed / MathF.Max(dot, epsilon) / before < MagnitudeThreshold;
    }
    
    public void Build(FrameGraph current, FrameGraph previous, float weight)
    {
        if (_poses.Length < current.Transforms.Count) _poses = new Pose[current.Transforms.Count];
        
        Span<float> blended = stackalloc float[9];
        Span<float> world = stackalloc float[9];
        Span<float> camera = stackalloc float[9];
        
        for (var i = 0; i < current.Transforms.Count; i++)
        {
            _poses[i].Valid = false;
            
            var group = current.Transforms[i];
            if (!_preparedValid[i]) continue;
            
            var prepared = _prepared.AsSpan(i * PreparedStride, PreparedStride);
            var inverse = prepared[..9];
            float tx, ty, tz;
            if (_split[i])
            {
                if (!Blend(prepared.Slice(WorldOffset, EndpointStride), weight, world) ||
                    !Blend(prepared.Slice(CameraOffset, EndpointStride), weight, camera)) continue;
                Multiply(camera, world, blended);
                
                var motion = prepared.Slice(MotionOffset, 12);
                var wx = motion[0] + (motion[3] - motion[0]) * weight - (motion[6] + (motion[9] - motion[6]) * weight);
                var wy = motion[1] + (motion[4] - motion[1]) * weight - (motion[7] + (motion[10] - motion[7]) * weight);
                var wz = motion[2] + (motion[5] - motion[2]) * weight - (motion[8] + (motion[11] - motion[8]) * weight);
                tx = camera[0] * wx + camera[1] * wy + camera[2] * wz;
                ty = camera[3] * wx + camera[4] * wy + camera[5] * wz;
                tz = camera[6] * wx + camera[7] * wy + camera[8] * wz;
            }
            else
            {
                if (!Blend(prepared.Slice(WorldOffset, EndpointStride), weight, blended)) continue;
                var from = previous.Transforms[group.Match];
                tx = from.TX + (group.TX - from.TX) * weight;
                ty = from.TY + (group.TY - from.TY) * weight;
                tz = from.TZ + (group.TZ - from.TZ) * weight;
            }
            
            ref var pose = ref _poses[i];
            
            pose.M0 = blended[0] * inverse[0] + blended[1] * inverse[3] + blended[2] * inverse[6];
            pose.M1 = blended[0] * inverse[1] + blended[1] * inverse[4] + blended[2] * inverse[7];
            pose.M2 = blended[0] * inverse[2] + blended[1] * inverse[5] + blended[2] * inverse[8];
            pose.M3 = blended[3] * inverse[0] + blended[4] * inverse[3] + blended[5] * inverse[6];
            pose.M4 = blended[3] * inverse[1] + blended[4] * inverse[4] + blended[5] * inverse[7];
            pose.M5 = blended[3] * inverse[2] + blended[4] * inverse[5] + blended[5] * inverse[8];
            pose.M6 = blended[6] * inverse[0] + blended[7] * inverse[3] + blended[8] * inverse[6];
            pose.M7 = blended[6] * inverse[1] + blended[7] * inverse[4] + blended[8] * inverse[7];
            pose.M8 = blended[6] * inverse[2] + blended[7] * inverse[5] + blended[8] * inverse[8];
            
            pose.CX = group.TX;
            pose.CY = group.TY;
            pose.CZ = group.TZ;
            
            pose.TX = tx;
            pose.TY = ty;
            pose.TZ = tz;
            
            pose.H = group.H;
            pose.OFX = group.OFX / 65536f;
            pose.OFY = group.OFY / 65536f;
            pose.Valid = pose.H > 0f;
        }
    }
    
    public bool Ready(int group)
    {
        var index = group - 1;
        return index >= 0 && index < _poses.Length && _poses[index].Valid;
    }
    
    public bool Warp(int group, in HleVertex vertex, out HleVertex result)
    {
        result = vertex;
        
        var index = group - 1;
        if (index < 0 || index >= _poses.Length || !_poses[index].Valid) return false;
        
        return Place(ref _poses[index], in vertex, ref result);
    }
    
    private static bool Place(ref Pose pose, in HleVertex vertex, ref HleVertex result)
    {
        if (!float.IsFinite(vertex.Depth) || vertex.Depth <= 0f) return false;
        
        float vx;
        float vy;
        float vz;
        if (vertex.Native)
        {
            vx = vertex.Camera.X - pose.CX;
            vy = vertex.Camera.Y - pose.CY;
            vz = vertex.Camera.Z - pose.CZ;
        }
        else
        {
            var depth = vertex.Depth;
            vx = (vertex.X - pose.OFX) * depth / pose.H - pose.CX;
            vy = (vertex.Y - pose.OFY) * depth / pose.H - pose.CY;
            vz = depth - pose.CZ;
        }
        
        var nx = pose.M0 * vx + pose.M1 * vy + pose.M2 * vz + pose.TX;
        var ny = pose.M3 * vx + pose.M4 * vy + pose.M5 * vz + pose.TY;
        var nz = pose.M6 * vx + pose.M7 * vy + pose.M8 * vz + pose.TZ;
        
        if (!float.IsFinite(nz) || nz < NearDepth) return false;
        
        var x = pose.OFX + nx * pose.H / nz;
        var y = pose.OFY + ny * pose.H / nz;
        
        if (!float.IsFinite(x) || !float.IsFinite(y)) return false;
        
        result.X = x;
        result.Y = y;
        result.Z = vertex.HasGteZ ? nz : vertex.Z;
        result.Depth = nz;
        result.Camera = new System.Numerics.Vector3(nx, ny, nz);
        result.Native = vertex.Native;
        result.HasGteZ = true;
        
        return true;
    }
    
    private static bool Blend(Span<float> data, float weight, Span<float> result)
    {
        var angle = data[8];

        float wa, wb;
        if (angle <= 0f)
        {
            wa = 1f - weight;
            wb = weight;
        }
        else
        {
            wa = MathF.Sin((1f - weight) * angle) * data[9];
            wb = MathF.Sin(weight * angle) * data[9];
        }

        var x = data[0] * wa + data[4] * wb;
        var y = data[1] * wa + data[5] * wb;
        var z = data[2] * wa + data[6] * wb;
        var w = data[3] * wa + data[7] * wb;

        var length = x * x + y * y + z * z + w * w;
        if (length < 1e-12f) return false;

        var scale = 2f / length;
        var r0 = 1f - scale * (y * y + z * z);
        var r1 = scale * (x * y - z * w);
        var r2 = scale * (x * z + y * w);
        var r3 = scale * (x * y + z * w);
        var r4 = 1f - scale * (x * x + z * z);
        var r5 = scale * (y * z - x * w);
        var r6 = scale * (x * z - y * w);
        var r7 = scale * (y * z + x * w);
        var r8 = 1f - scale * (x * x + y * y);

        var ka = data.Slice(10, 6);
        var kb = data.Slice(16, 6);
        var k0 = ka[0] + (kb[0] - ka[0]) * weight;
        var k1 = ka[1] + (kb[1] - ka[1]) * weight;
        var k2 = ka[2] + (kb[2] - ka[2]) * weight;
        var k3 = ka[3] + (kb[3] - ka[3]) * weight;
        var k4 = ka[4] + (kb[4] - ka[4]) * weight;
        var k5 = ka[5] + (kb[5] - ka[5]) * weight;
        
        result[0] = r0 * k0;
        result[1] = r0 * k1 + r1 * k3;
        result[2] = r0 * k2 + r1 * k4 + r2 * k5;
        result[3] = r3 * k0;
        result[4] = r3 * k1 + r4 * k3;
        result[5] = r3 * k2 + r4 * k4 + r5 * k5;
        result[6] = r6 * k0;
        result[7] = r6 * k1 + r7 * k3;
        result[8] = r6 * k2 + r7 * k4 + r8 * k5;
        
        return true;
    }
    
    private static bool Decompose(ReadOnlySpan<float> m, Span<float> q, Span<float> k)
    {
        var c0x = m[0];
        var c0y = m[3];
        var c0z = m[6];
        var c1x = m[1];
        var c1y = m[4];
        var c1z = m[7];
        var c2x = m[2];
        var c2y = m[5];
        var c2z = m[8];
        
        var k00 = MathF.Sqrt(c0x * c0x + c0y * c0y + c0z * c0z);
        if (k00 < 1e-4f) return false;
        
        var q0x = c0x / k00;
        var q0y = c0y / k00;
        var q0z = c0z / k00;
        
        var k01 = q0x * c1x + q0y * c1y + q0z * c1z;
        var e1x = c1x - k01 * q0x;
        var e1y = c1y - k01 * q0y;
        var e1z = c1z - k01 * q0z;
        
        var k11 = MathF.Sqrt(e1x * e1x + e1y * e1y + e1z * e1z);
        if (k11 < 1e-4f) return false;
        
        var q1x = e1x / k11;
        var q1y = e1y / k11;
        var q1z = e1z / k11;
        
        var k02 = q0x * c2x + q0y * c2y + q0z * c2z;
        var k12 = q1x * c2x + q1y * c2y + q1z * c2z;
        var e2x = c2x - k02 * q0x - k12 * q1x;
        var e2y = c2y - k02 * q0y - k12 * q1y;
        var e2z = c2z - k02 * q0z - k12 * q1z;
        
        var k22 = MathF.Sqrt(e2x * e2x + e2y * e2y + e2z * e2z);
        if (k22 < 1e-4f) return false;
        
        var q2x = e2x / k22;
        var q2y = e2y / k22;
        var q2z = e2z / k22;
        
        var determinant = q0x * (q1y * q2z - q1z * q2y) - q1x * (q0y * q2z - q0z * q2y) +q2x * (q0y * q1z - q0z * q1y);
        
        if (determinant < 0f)
        {
            q2x = -q2x;
            q2y = -q2y;
            q2z = -q2z;
            k22 = -k22;
        }
        
        q[0] = q0x; q[1] = q1x; q[2] = q2x;
        q[3] = q0y; q[4] = q1y; q[5] = q2y;
        q[6] = q0z; q[7] = q1z; q[8] = q2z;
        
        k[0] = k00;
        k[1] = k01;
        k[2] = k02;
        k[3] = k11;
        k[4] = k12;
        k[5] = k22;
        
        return true;
    }
    
    private static bool Invert(in TransformRecord m, Span<float> result)
    {
        Span<float> rotation = stackalloc float[9];
        Rotation(in m, rotation);
        return Invert(rotation, result);
    }
    
    private static bool Invert(ReadOnlySpan<float> m, Span<float> result)
    {
        var a = m[0];
        var b = m[1];
        var c = m[2];
        var d = m[3];
        var e = m[4];
        var f = m[5];
        var g = m[6];
        var h = m[7];
        var i = m[8];
        
        var determinant = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
        if (MathF.Abs(determinant) < 1e-6f) return false;
        
        var inverse = 1f / determinant;
        
        result[0] = (e * i - f * h) * inverse;
        result[1] = (c * h - b * i) * inverse;
        result[2] = (b * f - c * e) * inverse;
        result[3] = (f * g - d * i) * inverse;
        result[4] = (a * i - c * g) * inverse;
        result[5] = (c * d - a * f) * inverse;
        result[6] = (d * h - e * g) * inverse;
        result[7] = (b * g - a * h) * inverse;
        result[8] = (a * e - b * d) * inverse;
        
        return true;
    }
    
    private static bool Quaternion(Span<float> m, Span<float> q)
    {
        var m00 = m[0];
        var m01 = m[1];
        var m02 = m[2];
        var m10 = m[3];
        var m11 = m[4];
        var m12 = m[5];
        var m20 = m[6];
        var m21 = m[7];
        var m22 = m[8];
        
        var trace = m00 + m11 + m22;
        
        if (trace > 0f)
        {
            var s = MathF.Sqrt(trace + 1f) * 2f;
            q[3] = 0.25f * s;
            q[0] = (m21 - m12) / s;
            q[1] = (m02 - m20) / s;
            q[2] = (m10 - m01) / s;
            return true;
        }
        
        if (m00 > m11 && m00 > m22)
        {
            var s = MathF.Sqrt(1f + m00 - m11 - m22) * 2f;
            q[3] = (m21 - m12) / s;
            q[0] = 0.25f * s;
            q[1] = (m01 + m10) / s;
            q[2] = (m02 + m20) / s;
            return true;
        }
        
        if (m11 > m22)
        {
            var s = MathF.Sqrt(1f + m11 - m00 - m22) * 2f;
            q[3] = (m02 - m20) / s;
            q[0] = (m01 + m10) / s;
            q[1] = 0.25f * s;
            q[2] = (m12 + m21) / s;
            return true;
        }
        
        var t = MathF.Sqrt(1f + m22 - m00 - m11) * 2f;
        q[3] = (m10 - m01) / t;
        q[0] = (m02 + m20) / t;
        q[1] = (m12 + m21) / t;
        q[2] = 0.25f * t;
        return true;
    }
    
    private static float Row(short ax, short ay, short az, short bx, short by, short bz)
    {
        var a = MathF.Sqrt((float)ax * ax + (float)ay * ay + (float)az * az);
        var b = MathF.Sqrt((float)bx * bx + (float)by * by + (float)bz * bz);
        if (a < 1e-6f || b < 1e-6f) return 1f;
        
        return 1f - ((float)ax * bx + (float)ay * by + (float)az * bz) / (a * b);
    }
    
    private static float Determinant(in TransformRecord m)
    {
        return m.R0 * ((float)m.R4 * m.R8 - (float)m.R5 * m.R7) -
               m.R1 * ((float)m.R3 * m.R8 - (float)m.R5 * m.R6) +
               m.R2 * ((float)m.R3 * m.R7 - (float)m.R4 * m.R6);
    }
}
