using System.Runtime.InteropServices;
using RecompOne.Runtime.Hle;

namespace RecompOne.Runtime.Interp;

public sealed class InterpBackend : IGpuBackend
{
    private readonly IGpuBackend _inner;
    private readonly System.Diagnostics.Stopwatch _watch = System.Diagnostics.Stopwatch.StartNew();
    
    private int _source;
    private readonly TransformInterp _transforms = new();
    private readonly PrimTracker _prims = new();
    
    private readonly Lock _gate = new();
    private readonly Stack<FrameGraph> _free = new();
    
    private FrameGraph _recording = new();
    private readonly Queue<FrameGraph> _ready = new();
    
    private readonly Dictionary<int, int> _groups = new();
    private readonly HashSet<(int Group, uint Material)> _materials = [];
    
    private bool _active;
    private HleDrawEnv _lastEnv;
    private bool _hasEnv;
    private HleDrawEnv _entryEnv;
    private bool _hasEntryEnv;
    private const int TargetHistory = 4;
    private readonly (int X, int Y, int W, int H)[] _targets = new (int, int, int, int)[TargetHistory];
    private int _targetCount;
    private (int X, int Y, int W, int H) _shown;
    private bool _hasShown;
    
    public InterpBackend(IGpuBackend inner)
    {
        _inner = inner;
        _active = inner.Ready;
        Interp.Backend = this;
    }
    
    public bool Ready => _inner.Ready;
    
    public int SourceRate => _source;
    
    internal InterpStats Stats { get; } = new();
    
    private int _swaps;
    
    public void SetDrawEnv(in HleDrawEnv env)
    {
        _lastEnv = env;
        _hasEnv = true;
        if (!_active)
        {
            _inner.SetDrawEnv(env);
            return;
        }
        if (_recording.Envs.Count > 0)
        {
            var last = _recording.Envs[^1];
            if (last.ClipX0 == env.ClipX0 && last.ClipY0 == env.ClipY0 &&
                last.ClipX1 == env.ClipX1 && last.ClipY1 == env.ClipY1 &&
                last.TwMaskX == env.TwMaskX && last.TwMaskY == env.TwMaskY &&
                last.TwOffX == env.TwOffX && last.TwOffY == env.TwOffY &&
                last.SetMask == env.SetMask && last.CheckMask == env.CheckMask && last.Dither == env.Dither) return;
        }
        _recording.Envs.Add(env);
        _recording.EnvDraws.Add(0);
        _recording.Add(GraphOp.DrawEnv, _recording.Envs.Count - 1);
    }
    
    public void DrawTri(in HleVertex a, in HleVertex b, in HleVertex c, in PrimFlags f)
    {
        if (!_active)
        {
            _inner.DrawTri(in a, in b, in c, in f);
            return;
        }
        
        var offsetX = (float)(Runtime.Gpu?.DrawOffsetX ?? 0);
        var offsetY = (float)(Runtime.Gpu?.DrawOffsetY ?? 0);
        
        var slot = Slot(a.Transform, true);
        var slotB = Slot(b.Transform, false);
        var slotC = Slot(c.Transform, false);
        if (slot == 0 || slotB == 0 || slotC == 0) slot = slotB = slotC = 0;
        Mix(slot, in f, in a, in b, in c);
        
        var record = new TriRecord
        {
            Transform = slot,
            TransformB = slotB,
            TransformC = slotC,
            A = Detach(in a, offsetX, offsetY),
            B = Detach(in b, offsetX, offsetY),
            C = Detach(in c, offsetX, offsetY),
            Flags = f,
            OffsetX = offsetX,
            OffsetY = offsetY
        };
        _recording.Tris.Add(record);
        _recording.Drawn();
        
        _recording.Add(GraphOp.Tri, _recording.Tris.Count - 1);
    }
    
    private void Mix(int slot, in PrimFlags f, in HleVertex a, in HleVertex b, in HleVertex c)
    {
        if (slot <= 0) return;
        
        var held = _recording.Transforms[slot - 1];
        var material = (uint)f.TPage | ((uint)f.Clut << 16);
        var key = (material ^ (f.Textured ? 0x9E3779B9u : 0u)) * 16777619u;
        if (_materials.Add((slot, key))) held.Key = unchecked(held.Key + key);
        held.Pages |= 1u << (int)(f.TPage & 0x1Fu);
        
        if (f.Textured)
        {
            held.U0 = MathF.Min(held.U0, MathF.Min(a.U, MathF.Min(b.U, c.U)));
            held.U1 = MathF.Max(held.U1, MathF.Max(a.U, MathF.Max(b.U, c.U)));
            held.V0 = MathF.Min(held.V0, MathF.Min(a.V, MathF.Min(b.V, c.V)));
            held.V1 = MathF.Max(held.V1, MathF.Max(a.V, MathF.Max(b.V, c.V)));
        }
        
        _recording.Transforms[slot - 1] = held;
    }
    
    private int Slot(int serial, bool primary)
    {
        if (serial <= 0) return 0;
        
        if (_groups.TryGetValue(serial, out var index))
        {
            if (!primary) return index + 1;
            var held = _recording.Transforms[index];
            held.Tris++;
            _recording.Transforms[index] = held;
            return index + 1;
        }
        
        Span<short> rotation = stackalloc short[9];
        Span<int> translation = stackalloc int[3];
        Span<int> view = stackalloc int[3];
        if (!Gte.Snapshot(serial, rotation, translation, view)) return 0;
        Gte.SnapshotId(serial, out var id, out var kind);
        Span<short> camera = stackalloc short[9];
        Span<int> eye = stackalloc int[3];
        var hasCamera = Gte.SnapshotCamera(serial, camera, eye);
        
        index = _recording.Transforms.Count;
        _recording.Transforms.Add(new TransformRecord
        {
            HasCamera = hasCamera,
            C0 = camera[0], C1 = camera[1], C2 = camera[2],
            C3 = camera[3], C4 = camera[4], C5 = camera[5],
            C6 = camera[6], C7 = camera[7], C8 = camera[8],
            CX = eye[0], CY = eye[1], CZ = eye[2],
            Serial = serial,
            Id = id,
            Kind = kind,
            R0 = rotation[0], R1 = rotation[1], R2 = rotation[2],
            R3 = rotation[3], R4 = rotation[4], R5 = rotation[5],
            R6 = rotation[6], R7 = rotation[7], R8 = rotation[8],
            TX = translation[0], TY = translation[1], TZ = translation[2],
            H = view[0], OFX = view[1], OFY = view[2],
            Tris = primary ? 1 : 0,
            Match = -1,
            U0 = float.MaxValue, U1 = float.MinValue,
            V0 = float.MaxValue, V1 = float.MinValue
        });
        
        _groups[serial] = index;
        return index + 1;
    }
    
    public void DrawRect(in HleRect r, in PrimFlags f)
    {
        if (!_active)
        {
            _inner.DrawRect(in r, in f);
            return;
        }
        _recording.Rects.Add(new RectRecord { Rect = r, Flags = f });
        _recording.Drawn();
        _recording.Add(GraphOp.Rect, _recording.Rects.Count - 1);
    }
    
    public void DrawLine(in HleVertex a, in HleVertex b, in PrimFlags f)
    {
        if (!_active)
        {
            _inner.DrawLine(in a, in b, in f);
            return;
        }
        
        var offsetX = (float)(Runtime.Gpu?.DrawOffsetX ?? 0);
        var offsetY = (float)(Runtime.Gpu?.DrawOffsetY ?? 0);
        var slot = Slot(a.Transform, true);
        var slotB = Slot(b.Transform, false);
        if (slot == 0 || slotB == 0) slot = slotB = 0;
        Mix(slot, in f, in a, in b, in b);
        
        var record = new LineRecord
        {
            A = Detach(in a, offsetX, offsetY),
            B = Detach(in b, offsetX, offsetY),
            Flags = f,
            Transform = slot,
            TransformB = slotB,
            OffsetX = offsetX,
            OffsetY = offsetY
        };
        _recording.Lines.Add(record);
        _recording.Drawn();
        _recording.Add(GraphOp.Line, _recording.Lines.Count - 1);
    }
    
    public void FillRect(int x, int y, int w, int h, ushort color15)
    {
        if (!_active)
        {
            _inner.FillRect(x, y, w, h, color15);
            return;
        }
        _recording.Fills.Add(new FillRecord { X = x, Y = y, W = w, H = h, Color = color15 });
        _recording.Add(GraphOp.Fill, _recording.Fills.Count - 1);
    }
    
    public void FillDisplayMargins(int x, int y, int w, int h, ushort color15)
    {
        if (!_active)
        {
            _inner.FillDisplayMargins(x, y, w, h, color15);
            return;
        }
        _recording.Fills.Add(new FillRecord { X = x, Y = y, W = w, H = h, Color = color15 });
        _recording.Add(GraphOp.DisplayMargins, _recording.Fills.Count - 1);
    }
    
    public void CopyVram(int sx, int sy, int dx, int dy, int w, int h)
    {
        if (!_active)
        {
            _inner.CopyVram(sx, sy, dx, dy, w, h);
            return;
        }
        _recording.Copies.Add(new CopyRecord { Sx = sx, Sy = sy, Dx = dx, Dy = dy, W = w, H = h });
        _recording.Add(GraphOp.CopyVram, _recording.Copies.Count - 1);
    }
    
    public void WriteVram(int x, int y, int w, int h, ReadOnlySpan<ushort> px)
    {
        if (!_active)
        {
            _inner.WriteVram(x, y, w, h, px);
            return;
        }
        var offset = _recording.AddPixels(px);
        _recording.Writes.Add(new WriteRecord { X = x, Y = y, W = w, H = h, Offset = offset, Length = px.Length });
        _recording.Add(GraphOp.WriteVram, _recording.Writes.Count - 1);
    }
    
    public void ReadVram(int x, int y, int w, int h, Span<ushort> px)
    {
        SettlePending();
        
        _inner.ReadVram(x, y, w, h, px);
    }
    
    public int RegisterImage(ReadOnlySpan<byte> rgba, int width, int height)
    {
        return _inner.RegisterImage(rgba, width, height);
    }

    private readonly Dictionary<int, IReadOnlyList<CaptureOverlay>> _captureOverlays = [];

    public int ReserveImage()
    {
        return _inner.ReserveImage();
    }

    public void CaptureTarget(int image, IReadOnlyList<CaptureOverlay> overlays)
    {
        if (!_active)
        {
            _inner.CaptureTarget(image, overlays);
            return;
        }

        _captureOverlays[image] = overlays;
        _recording.Add(GraphOp.Capture, image);
    }

    public bool TryGetCapture(int image, out CapturedImage capture)
    {
        return _inner.TryGetCapture(image, out capture);
    }

    public void ReleaseImage(int image)
    {
        _inner.ReleaseImage(image);
    }
    
    public void Flush()
    {
        Settle();
        _inner.Flush();
    }
    
    public void Present(in HleDispEnv disp)
    {
        _inner.Present(in disp);
    }
    
    private const double StallMs = 100.0;
    
    public double PaceMs { get; private set; }
    
    public void Publish()
    {
        _swaps = 0;
        var presentAt = Interp.PresentAt;
        Interp.PresentAt = double.NaN;
        Gte.BeginFrame();
        if (!_active) return;
        
        Stats.Published++;
        Stats.LastTris = _recording.Tris.Count;
        if (_recording.Tris.Count == 0) Stats.PublishedEmpty++;
        
        Classify(_recording);
        Target(_recording);
        
        _recording.Time = double.IsNaN(presentAt) ? Now() : presentAt;
        if (Runtime.Gpu is { } gpu)
        {
            _recording.HasDisplay = true;
            _recording.Display = new DisplayState(gpu.DisplayX, gpu.DisplayY, gpu.DisplayWidth, gpu.DisplayHeight,
                gpu.Display24Bit, gpu.DisplayEnabled);
        }
        _recording.HasCamera = CameraBlend.Pick(_recording, out _recording.Camera);
        
        lock (_gate)
        {
            _ready.Enqueue(_recording);
            _recording = _free.Count > 0 ? _free.Pop() : new FrameGraph();
            _recording.Clear();
            _groups.Clear();
            _materials.Clear();
        }
        
        _entryEnv = _lastEnv;
        _hasEntryEnv = _hasEnv;
    }
    
    public void NoteSwap()
    {
        if (!_active) return;
        
        if (++_swaps <= 1 || _recording.IsEmpty) return;
        
        Stats.SwapPublished++;
        Publish();
        _swaps = 1;
    }
    
    private void Classify(FrameGraph graph)
    {
        var writes = CollectionsMarshal.AsSpan(graph.Writes);
        if (writes.Length == 0) return;
        
        var envs = CollectionsMarshal.AsSpan(graph.Envs);
        var fills = CollectionsMarshal.AsSpan(graph.Fills);
        var copies = CollectionsMarshal.AsSpan(graph.Copies);
        var draws = graph.Tris.Count > 0 || graph.Rects.Count > 0 || graph.Lines.Count > 0;
        var unknown = draws && !_hasEntryEnv && envs.Length == 0 || graph.Ops.Contains(GraphOp.Capture);
        
        for (var i = 0; i < writes.Length; i++)
        {
            ref var write = ref writes[i];
            write.Static = false;
            if (unknown) continue;
            
            var x0 = write.X;
            var y0 = write.Y;
            var x1 = write.X + write.W;
            var y1 = write.Y + write.H;
            var clean = true;
            
            if (draws && _hasEntryEnv && Hits(in _entryEnv, x0, y0, x1, y1)) clean = false;
            for (var e = 0; clean && draws && e < envs.Length; e++)
                if (Hits(in envs[e], x0, y0, x1, y1)) clean = false;
            for (var f = 0; clean && f < fills.Length; f++)
                if (Overlaps(x0, y0, x1, y1, fills[f].X, fills[f].Y, fills[f].X + fills[f].W, fills[f].Y + fills[f].H)) clean = false;
            for (var c = 0; clean && c < copies.Length; c++)
            {
                ref var copy = ref copies[c];
                if (Overlaps(x0, y0, x1, y1, copy.Dx, copy.Dy, copy.Dx + copy.W, copy.Dy + copy.H) ||
                    Overlaps(x0, y0, x1, y1, copy.Sx, copy.Sy, copy.Sx + copy.W, copy.Sy + copy.H)) clean = false;
            }
            for (var o = 0; clean && o < writes.Length; o++)
            {
                if (o == i) continue;
                ref var other = ref writes[o];
                if (Overlaps(x0, y0, x1, y1, other.X, other.Y, other.X + other.W, other.Y + other.H)) clean = false;
            }
            
            write.Static = clean;
        }
    }
    
    private void Target(FrameGraph graph)
    {
        if (graph.Tris.Count == 0 && graph.Rects.Count == 0) return;
        
        var best = 0;
        var envs = CollectionsMarshal.AsSpan(graph.Envs);
        var draws = CollectionsMarshal.AsSpan(graph.EnvDraws);
        if (_hasEntryEnv) Consider(graph, in _entryEnv, graph.EntryDraws, ref best);
        for (var i = 0; i < envs.Length; i++) Consider(graph, in envs[i], draws[i], ref best);
        if (!graph.HasTarget) return;
        
        var target = (graph.TargetX, graph.TargetY, graph.TargetW, graph.TargetH);
        lock (_gate)
        {
            for (var i = 0; i < _targetCount; i++)
                if (_targets[i] == target) return;
            
            if (_targetCount < TargetHistory) _targets[_targetCount++] = target;
            else
            {
                Array.Copy(_targets, 1, _targets, 0, TargetHistory - 1);
                _targets[TargetHistory - 1] = target;
            }
        }
    }
    
    private static void Consider(FrameGraph graph, in HleDrawEnv env, int draws, ref int best)
    {
        var w = env.ClipX1 - env.ClipX0 + 1;
        var h = env.ClipY1 - env.ClipY0 + 1;
        if (w <= 0 || h <= 0 || draws <= best) return;
        
        best = draws;
        graph.HasTarget = true;
        graph.TargetX = env.ClipX0;
        graph.TargetY = env.ClipY0;
        graph.TargetW = w;
        graph.TargetH = h;
    }
    
    public bool TryShownCamera(Span<float> rotation, Span<float> translation)
    {
        if (!Interp.Enabled) return false;

        lock (_gate)
        {
            if (_shownGraph is not { HasCamera: true } graph) return false;
            var from = _shownPrevious is { HasCamera: true } previous && _shownWeight < 1f ? previous.Camera : graph.Camera;
            CameraBlend.Blend(in from, in graph.Camera, Math.Clamp(_shownWeight, 0f, 1f), rotation, translation);
            return true;
        }
    }

    public bool TryShownDisplay(out DisplayState display)
    {
        display = default;
        if (!Interp.Enabled) return false;

        lock (_gate)
        {
            if (_shownGraph is not { HasDisplay: true } graph) return false;
            display = graph.Display;
            return true;
        }
    }

    public bool MapDisplay(ref int x, ref int y, int w, int h)
    {
        if (!Interp.Enabled) return false;
        
        lock (_gate)
        {
            Stats.LiveX = x;
            Stats.LiveY = y;
            Stats.ShownX = _shown.X;
            Stats.ShownY = _shown.Y;
            if (!_hasShown || Holds(_shown, x, y)) return false;
            
            for (var i = 0; i < _targetCount; i++)
            {
                var other = _targets[i];
                if (other.W != _shown.W || other.H != _shown.H || !Holds(other, x, y)) continue;
                
                x = _shown.X + (x - other.X);
                y = _shown.Y + (y - other.Y);
                Stats.Remaps++;
                return true;
            }
            
            Stats.Unmapped++;
        }
        
        return false;
    }
    
    private static bool Holds((int X, int Y, int W, int H) area, int x, int y)
    {
        return x >= area.X && y >= area.Y && x < area.X + area.W && y < area.Y + area.H;
    }
    
    private static bool Hits(in HleDrawEnv env, int x0, int y0, int x1, int y1)
    {
        return Overlaps(x0, y0, x1, y1, env.ClipX0, env.ClipY0, env.ClipX1 + 1, env.ClipY1 + 1);
    }
    
    private static bool Overlaps(int ax0, int ay0, int ax1, int ay1, int bx0, int by0, int bx1, int by1)
    {
        return ax0 < bx1 && bx0 < ax1 && ay0 < by1 && by0 < ay1;
    }
    
    private const int MaxDelay = 6;
    private const double DelayMargin = 0.35;
    private const int IntervalHistory = 24;
    private const double DelayStep = 0.1;
    private readonly double[] _intervals = new double[IntervalHistory];
    private int _intervalCount;
    private int _intervalCursor;
    
    public bool Marker { get; set; }
    
    private readonly List<FrameGraph> _timeline = [];
    private FrameGraph? _pairPrevious;
    private FrameGraph? _pairCurrent;
    private bool _pairReady;
    private FrameGraph? _shownGraph;
    private FrameGraph? _shownPrevious;
    private FrameGraph? _held;
    private float _shownWeight = -1f;
    private double _lastAdmitted = -1.0;
    private double _interval;
    private double _lastTime;
    
    public double Delay { get; private set; } = 2.0 + DelayMargin;
    
    public bool Present(out bool fresh)
    {
        fresh = false;
        if (!_active) return false;
        
        lock (_gate)
        {
            Stats.BeginFrame();
            var shown = PresentLocked(ref fresh);
            if (shown) Stats.EndFrame(_watch.Elapsed.TotalMilliseconds, _shownWeight);
            return shown;
        }
    }
    
    private bool PresentLocked(ref bool fresh)
    {
        {
            while (_ready.Count > 0) Admit(_ready.Dequeue());
            if (_timeline.Count == 0) return false;
            
            if (!Interp.Enabled)
            {
                PaceMs = 0.0;
                var newest = _timeline.Count - 1;
                if (_timeline[newest] == _shownGraph) return false;
                
                CatchUp(newest);
                Retire(newest);
                return Show(_timeline[0], null, 1f, ref fresh);
            }
            
            var target = Interp.EffectiveTarget;
            PaceMs = target > 0 ? 1000.0 / target : 0.0;
            
            var time = Math.Max(Now() - Delay, _lastTime);
            _lastTime = time;
            
            var keep = 0;
            while (keep + 1 < _timeline.Count && _timeline[keep + 1].Time <= time) keep++;
            CatchUp(keep);
            Retire(keep);
            
            if (_timeline.Count == 1)
            {
                if (time > _timeline[0].Time && _held != _timeline[0])
                {
                    _held = _timeline[0];
                    Stats.Endpoints++;
                }
                return Show(_timeline[0], null, 1f, ref fresh);
            }
            
            var previous = _timeline[0];
            var current = _timeline[1];
            var span = current.Time - previous.Time;
            var weight = span > 0.0 ? (float)Math.Clamp((time - previous.Time) / span, 0.0, 1.0) : 1f;
            
            if (previous != _pairPrevious || current != _pairCurrent) Pair(previous, current);
            
            if (!_pairReady)
                return weight > 0f || current.Uploaded ? Show(current, null, 1f, ref fresh) : Show(previous, null, 1f, ref fresh);
            
            if (weight >= 1f) return Show(current, null, 1f, ref fresh);
            
            if (!previous.Uploaded) Replay(previous, null, 1f);
            fresh |= !current.Uploaded;
            _transforms.Build(current, previous, weight);
            Hle.GpuHle.Hold();
            Replay(current, previous, weight);
            DrawMarker(current, weight);
            _shownGraph = current;
            _shownPrevious = previous;
            _shownWeight = weight;
            Stats.Interpolated++;
            return true;
        }
    }
    
    private bool Show(FrameGraph graph, FrameGraph? previous, float weight, ref bool fresh)
    {
        if (graph == _shownGraph && weight == _shownWeight) return false;
        
        fresh |= !graph.Uploaded;
        Replay(graph, previous, weight);
        DrawMarker(graph, weight);
        _shownGraph = graph;
        _shownPrevious = previous;
        _shownWeight = weight;
        return true;
    }
    
    private void Admit(FrameGraph graph)
    {
        Stats.Acquired++;
        if (_lastAdmitted >= 0.0)
        {
            var interval = Math.Clamp(graph.Time - _lastAdmitted, 1.0, MaxDelay);
            _interval = _interval > 0.0 ? _interval * 0.8 + interval * 0.2 : interval;
            _intervals[_intervalCursor] = interval;
            _intervalCursor = (_intervalCursor + 1) % IntervalHistory;
            if (_intervalCount < IntervalHistory) _intervalCount++;
            var wanted = Math.Clamp(Percentile(0.9) + DelayMargin, 1.0, MaxDelay);
            Delay += Math.Clamp(wanted - Delay, -DelayStep, DelayStep);
            _source = (int)Math.Round((Runtime.Gpu?.Pal == true ? 50 : 60) / _interval);
            Stats.Pair((int)Math.Round(interval), Delay);
        }
        _lastAdmitted = graph.Time;
        _timeline.Add(graph);
    }
    
    private double Percentile(double fraction)
    {
        Span<double> sorted = stackalloc double[_intervalCount];
        _intervals.AsSpan(0, _intervalCount).CopyTo(sorted);
        sorted.Sort();
        return sorted[Math.Min(_intervalCount - 1, (int)(fraction * _intervalCount))];
    }
    
    private void DrawMarker(FrameGraph graph, float weight)
    {
        if (!Marker || !graph.HasTarget) return;
        
        const int length = 64;
        var x = graph.TargetX + 4;
        var y = graph.TargetY + 4;
        _inner.FillRect(x, y, length + 2, 6, 0);
        _inner.FillRect(x + 1, y + 1, Math.Max(1, (int)(weight * length)), 4, weight >= 1f ? (ushort)0x03E0 : (ushort)0x001F);
    }
    
    private void CatchUp(int index)
    {
        for (var i = 0; i < index; i++)
        {
            if (_timeline[i].Uploaded) continue;
            Stats.Skipped++;
            Replay(_timeline[i], null, 1f);
        }
    }
    
    private void Retire(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var graph = _timeline[0];
            _timeline.RemoveAt(0);
            if (graph == _pairPrevious) _pairPrevious = null;
            if (graph == _pairCurrent) _pairCurrent = null;
            if (graph == _shownGraph) _shownGraph = null;
            if (graph == _held) _held = null;
            Recycle(graph);
        }
    }
    
    private void Pair(FrameGraph previous, FrameGraph current)
    {
        _pairPrevious = previous;
        _pairCurrent = current;
        _pairReady = current.Interpolatable && previous.Interpolatable && !current.IsEmpty && previous.Tris.Count > 0;
        
        if (!_pairReady)
        {
            Stats.Skip(!current.Interpolatable || !previous.Interpolatable ? InterpSkip.NotInterpolatable :
                current.IsEmpty ? InterpSkip.EmptyCurrent : InterpSkip.EmptyPrevious);
            return;
        }
        
        Prepare(current, previous);
        Stats.Skip(InterpSkip.None);
    }
    
    private static double Now()
    {
        var frame = Host.FrameClock.FrameMs;
        var due = Host.FrameClock.Due;
        var count = Host.FrameClock.Count;
        if (due <= 0.0) return count;
        
        return count + Math.Clamp((Host.FrameClock.Now - (due - frame)) / frame, 0.0, MaxDelay);
    }
    
    private void Recycle(FrameGraph graph)
    {
        _prims.Clear();
        graph.Clear();
        _free.Push(graph);
    }
    
    private void Prepare(FrameGraph current, FrameGraph previous)
    {
        _transforms.Match(current, previous);
        Stats.LastGroups = _transforms.Groups;
        Stats.LastIdMatched = _transforms.IdMatched;
        Stats.LastHeuristicMatched = _transforms.HeuristicMatched;
        Stats.LastRejected = _transforms.Rejected;
        Stats.LastCameraOnly = _transforms.CameraOnly;
        Stats.LastCameraJumps = _transforms.CameraJumps;
        Stats.Matching();
    }
    
    public void EndPresent()
    {
        Hle.GpuHle.Release();
        Begin();
    }
    
    private void Begin()
    {
        var wanted = _inner.Ready;
        if (wanted == _active) return;
        
        _active = wanted;
        lock (_gate)
        {
            while (_ready.Count > 0) Recycle(_ready.Dequeue());
            Reset();
        }
        _recording.Clear();
        _groups.Clear();
        _materials.Clear();
    }
    
    private void Reset()
    {
        foreach (var graph in _timeline) Recycle(graph);
        _timeline.Clear();
        _pairPrevious = _pairCurrent = _shownGraph = _shownPrevious = _held = null;
        _pairReady = false;
        _shownWeight = -1f;
        _lastTime = 0.0;
        Stats.Resyncs++;
    }
    
    private void Settle()
    {
        if (!_active) return;
        
        lock (_gate)
        {
            if (_timeline.Count == 0 && _ready.Count == 0) return;
            
            Stats.Settles++;
            while (_ready.Count > 0) Admit(_ready.Dequeue());
            Catch();
        }
    }
    
    private void SettlePending()
    {
        if (!_active) return;
        
        Stats.Readbacks++;
        
        lock (_gate)
        {
            while (_ready.Count > 0) Admit(_ready.Dequeue());
            Catch();
            
            if (!_recording.IsEmpty)
            {
                Replay(_recording, null, 1f);
                _recording.Clear();
                _recording.Interpolatable = false;
                _groups.Clear();
                _materials.Clear();
            }
        }
    }
    
    private void Catch()
    {
        if (_timeline.Count == 0) return;
        
        var last = _timeline.Count - 1;
        CatchUp(last);
        var newest = _timeline[last];
        if (newest != _shownGraph || _shownWeight < 1f) Replay(newest, null, 1f);
        Retire(last);
        newest.Interpolatable = false;
        _shownGraph = newest;
        _shownPrevious = null;
        _shownWeight = 1f;
        _pairPrevious = _pairCurrent = null;
        _lastTime = 0.0;
    }
    
    private void Replay(FrameGraph graph, FrameGraph? previous, float weight)
    {
        _inner.BeginScene();
        var pixels = CollectionsMarshal.AsSpan(graph.Pixels);
        var ops = CollectionsMarshal.AsSpan(graph.Ops);
        var slots = CollectionsMarshal.AsSpan(graph.Slots);
        var envs = CollectionsMarshal.AsSpan(graph.Envs);
        var rects = CollectionsMarshal.AsSpan(graph.Rects);
        var fills = CollectionsMarshal.AsSpan(graph.Fills);
        var copies = CollectionsMarshal.AsSpan(graph.Copies);
        var writes = CollectionsMarshal.AsSpan(graph.Writes);
        var lines = CollectionsMarshal.AsSpan(graph.Lines);
        var tris = CollectionsMarshal.AsSpan(graph.Tris);
        var repeat = graph.Uploaded;
        graph.Uploaded = true;
        if (previous != null && weight < 1f)
        {
            _prims.Build(graph, previous);
            Stats.LastPrimIds = _prims.Identified;
            Stats.LastPrimsPaired = _prims.Paired;
            Stats.LastPrimsTracked = _prims.Tracked;
            Stats.LastPrimsSpatial = _prims.Spatial;
            Stats.LastPrimsRejected = _prims.Rejected;
        }
        if (graph.HasTarget)
        {
            _shown = (graph.TargetX, graph.TargetY, graph.TargetW, graph.TargetH);
            _hasShown = true;
        }
        
        for (var i = 0; i < ops.Length; i++)
        {
            var slot = slots[i];
            
            switch (ops[i])
            {
                case GraphOp.DrawEnv:
                    _inner.SetDrawEnv(envs[slot]);
                    break;
                
                case GraphOp.Tri:
                    ReplayTri(slot, in tris[slot], previous, weight);
                    break;
                case GraphOp.Rect:
                {
                    ref var rect = ref rects[slot];
                    _inner.DrawRect(in rect.Rect, in rect.Flags);
                    break;
                }
                case GraphOp.Line:
                {
                    ReplayLine(in lines[slot], previous, weight);
                    break;
                }
                case GraphOp.Fill:
                {
                    ref var fill = ref fills[slot];
                    _inner.FillRect(fill.X, fill.Y, fill.W, fill.H, fill.Color);
                    break;
                }
                case GraphOp.DisplayMargins:
                {
                    ref var margin = ref fills[slot];
                    _inner.FillDisplayMargins(margin.X, margin.Y, margin.W, margin.H, margin.Color);
                    break;
                }
                case GraphOp.Capture:
                    _inner.CaptureTarget(slot, _captureOverlays.GetValueOrDefault(slot) ?? []);
                    break;
                case GraphOp.CopyVram:
                {
                    ref var copy = ref copies[slot];
                    _inner.CopyVram(copy.Sx, copy.Sy, copy.Dx, copy.Dy, copy.W, copy.H);
                    break;
                }
                case GraphOp.WriteVram:
                {
                    ref var write = ref writes[slot];
                    if (repeat && write.Static) break;
                    _inner.WriteVram(write.X, write.Y, write.W, write.H, pixels.Slice(write.Offset, write.Length));
                    break;
                }
            }
        }
    }
    
    private void ReplayTri(int index, in TriRecord source, FrameGraph? previous, float weight)
    {
        if (previous == null || weight >= 1f)
        {
            Emit(in source, in source.A, in source.B, in source.C);
            return;
        }
        
        var mode = _prims.TryBlend(index, in source, previous, weight, out var blended);
        if (mode == PrimTracker.Mode.Screen)
        {
            Stats.PrimsBlended++;
            Emit(in source, in blended.A, in blended.B, in blended.C);
            return;
        }
        
        if (mode == PrimTracker.Mode.Space) Stats.PrimsBlended++;
        ref readonly var tri = ref mode == PrimTracker.Mode.Space ? ref blended : ref source;
        if (tri.Transform <= 0) Stats.TrisUngrouped++;
        else if (!_transforms.Ready(tri.Transform) || !_transforms.Ready(tri.TransformB) || !_transforms.Ready(tri.TransformC)) Stats.TrisInvalidPose++;
        else if (_transforms.Warp(tri.Transform, in tri.A, out var wa) && _transforms.Warp(tri.TransformB, in tri.B, out var wb) &&
                 _transforms.Warp(tri.TransformC, in tri.C, out var wc))
        {
            Stats.TrisWarped++;
            Emit(in tri, in wa, in wb, in wc);
            return;
        }
        else Stats.TrisPlaceFailed++;

        
        Emit(in tri, in tri.A, in tri.B, in tri.C);
    }
    
    private void ReplayLine(in LineRecord line, FrameGraph? previous, float weight)
    {
        if (previous != null && weight < 1f)
        {
            if (line.Transform > 0 && _transforms.Warp(line.Transform, in line.A, out var wa) &&
                _transforms.Warp(line.TransformB, in line.B, out var wb))
            {
                _inner.DrawLine(Attach(in wa, line.OffsetX, line.OffsetY), Attach(in wb, line.OffsetX, line.OffsetY), in line.Flags);
                return;
            }

        }
        
        _inner.DrawLine(Attach(in line.A, line.OffsetX, line.OffsetY), Attach(in line.B, line.OffsetX, line.OffsetY), in line.Flags);
    }
    
    private void Emit(in TriRecord tri, in HleVertex a, in HleVertex b, in HleVertex c)
    {
        var va = Attach(in a, tri.OffsetX, tri.OffsetY);
        var vb = Attach(in b, tri.OffsetX, tri.OffsetY);
        var vc = Attach(in c, tri.OffsetX, tri.OffsetY);
        
        _inner.DrawTri(in va, in vb, in vc, in tri.Flags); //send interpolated tri to backend
    }
    
    private static HleVertex Detach(in HleVertex v, float offsetX, float offsetY)
    {
        var d = v;
        d.X -= offsetX;
        d.Y -= offsetY;
        return d;
    }
    
    private static HleVertex Attach(in HleVertex v, float offsetX, float offsetY)
    {
        var a = v;
        a.X += offsetX;
        a.Y += offsetY;
        return a;
    }
}
