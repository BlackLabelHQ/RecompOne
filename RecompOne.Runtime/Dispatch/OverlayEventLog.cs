namespace RecompOne.Runtime.Dispatch;

public enum OverlayEventKind
{
    Loaded,
    Unloaded,
    Overwritten,
    VramCollision
}

public readonly record struct OverlayEvent(
    long TimestampMs,
    string OverlayName,
    OverlayEventKind Kind,
    string? DisplacedBy
);

public sealed class OverlayEventLog
{
    private const int MaxEntries = 500;

    private readonly List<OverlayEvent> _events = new(MaxEntries);
    private readonly object _lock = new();

    private static readonly long _startMs = Environment.TickCount64;

    public void Record(string name, OverlayEventKind kind, string? displacedBy = null)
    {
        var ev = new OverlayEvent(Environment.TickCount64 - _startMs, name, kind, displacedBy);
        lock (_lock)
        {
            if (_events.Count >= MaxEntries) _events.RemoveAt(0);
            _events.Add(ev);
        }
    }

    public void Read(List<OverlayEvent> dest)
    {
        lock (_lock)
        {
            dest.AddRange(_events);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _events.Clear();
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _events.Count;
            }
        }
    }
}