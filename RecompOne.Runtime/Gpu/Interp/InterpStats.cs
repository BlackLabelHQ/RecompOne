namespace RecompOne.Runtime.Interp;

internal enum InterpSkip
{
    None,
    NotInterpolatable,
    EmptyCurrent,
    EmptyPrevious
}

internal sealed class InterpRing(int capacity)
{
    public readonly float[] Data = new float[capacity];
    public int Head;
    public int Count;

    public int Offset => Count < Data.Length ? 0 : Head;

    public float Last => Count == 0 ? 0f : Data[(Head - 1 + Data.Length) % Data.Length];

    public void Add(float value)
    {
        Data[Head] = value;
        Head = (Head + 1) % Data.Length;
        if (Count < Data.Length) Count++;
    }

    public void Clear()
    {
        Head = 0;
        Count = 0;
    }
}

internal sealed class InterpStats
{
    public const int Samples = 600;
    public const int Seconds = 120;

    public readonly InterpRing FrameWeight = new(Samples);
    public readonly InterpRing FrameMs = new(Samples);
    public readonly InterpRing FrameWarped = new(Samples);
    public readonly InterpRing PairVblanks = new(Samples);
    public readonly InterpRing PairDelay = new(Samples);
    public readonly InterpRing PairGroups = new(Samples);
    public readonly InterpRing PairIdMatched = new(Samples);
    public readonly InterpRing PairHeuristic = new(Samples);
    public readonly InterpRing PairRejected = new(Samples);
    public readonly InterpRing PairCameraOnly = new(Samples);
    public readonly InterpRing PairCameraCuts = new(Samples);
    public readonly InterpRing PublishedRate = new(Seconds);
    public readonly InterpRing InterpolatedRate = new(Seconds);
    public readonly InterpRing PresentRate = new(Seconds);

    public long Published;
    public long PublishedEmpty;
    public long SwapPublished;
    public long Acquired;
    public long Skipped;
    public long Settles;
    public long Readbacks;
    public long Resyncs;
    public long Remaps;
    public long Unmapped;
    public long Interpolated;
    public long Endpoints;
    public long Presented;
    public int LiveX, LiveY, ShownX, ShownY;

    public readonly long[] SkipReasons = new long[Enum.GetValues<InterpSkip>().Length];
    public InterpSkip LastSkip;

    public int LastTris;
    public int LastGroups;
    public int LastIdMatched;
    public int LastHeuristicMatched;
    public int LastRejected;
    public int LastCameraOnly;
    public int LastCameraJumps;
    public int LastPrimIds;
    public int LastPrimsPaired;
    public int LastPrimsTracked;
    public int LastPrimsSpatial;
    public int LastPrimsRejected;

    public long TrisWarped;
    public long TrisUngrouped;
    public long TrisInvalidPose;
    public long TrisPlaceFailed;
    public long PrimsBlended;

    private long _frameTris;
    private long _frameWarped;
    private double _lastPresentMs;
    private double _lastSecondMs;
    private long _secondPublished;
    private long _secondInterpolated;
    private long _secondPresented;

    public void Reset()
    {
        Published = PublishedEmpty = SwapPublished = Acquired = Skipped = 0;
        Settles = Readbacks = Resyncs = Interpolated = Endpoints = Remaps = Unmapped = Presented = 0;
        TrisWarped = TrisUngrouped = TrisInvalidPose = TrisPlaceFailed = PrimsBlended = 0;
        Array.Clear(SkipReasons);
        _lastPresentMs = _lastSecondMs = 0.0;
        _secondPublished = _secondInterpolated = _secondPresented = 0;
        foreach (var ring in new[] { FrameWeight, FrameMs, FrameWarped, PairVblanks, PairDelay, PairGroups, PairIdMatched,
                     PairHeuristic, PairRejected, PairCameraOnly, PairCameraCuts, PublishedRate, InterpolatedRate, PresentRate })
            ring.Clear();
    }

    public void BeginFrame()
    {
        _frameTris = TrisWarped + TrisUngrouped + TrisInvalidPose + TrisPlaceFailed;
        _frameWarped = TrisWarped;
    }

    public void EndFrame(double nowMs, float weight)
    {
        Presented++;
        FrameWeight.Add(weight);
        if (_lastPresentMs > 0.0) FrameMs.Add((float)(nowMs - _lastPresentMs));
        _lastPresentMs = nowMs;

        var tris = TrisWarped + TrisUngrouped + TrisInvalidPose + TrisPlaceFailed - _frameTris;
        FrameWarped.Add(tris > 0 ? 100f * (TrisWarped - _frameWarped) / tris : 0f);

        if (_lastSecondMs <= 0.0)
        {
            _lastSecondMs = nowMs;
            _secondPublished = Published;
            _secondInterpolated = Interpolated;
            _secondPresented = Presented;
            return;
        }

        var span = (nowMs - _lastSecondMs) / 1000.0;
        if (span < 1.0) return;

        PublishedRate.Add((float)((Published - _secondPublished) / span));
        InterpolatedRate.Add((float)((Interpolated - _secondInterpolated) / span));
        PresentRate.Add((float)((Presented - _secondPresented) / span));
        _lastSecondMs = nowMs;
        _secondPublished = Published;
        _secondInterpolated = Interpolated;
        _secondPresented = Presented;
    }

    public void Matching()
    {
        PairGroups.Add(LastGroups);
        PairIdMatched.Add(LastIdMatched);
        PairHeuristic.Add(LastHeuristicMatched);
        PairRejected.Add(LastRejected);
        PairCameraOnly.Add(LastCameraOnly);
        PairCameraCuts.Add(LastCameraJumps);
    }

    public void Skip(InterpSkip reason)
    {
        LastSkip = reason;
        SkipReasons[(int)reason]++;
    }

    public void Pair(int vblanks, double delay)
    {
        PairVblanks.Add(vblanks);
        PairDelay.Add((float)delay);
    }
}
