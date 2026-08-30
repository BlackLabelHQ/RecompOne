using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Events;

public interface IEvent
{
    string Id => GetType().Name;
}

public class GameEvent : IEvent
{
    private string? _id;

    public string Id
    {
        get => _id ?? GetType().Name;
        set => _id = value;
    }

    public CpuContext Context = null!;
    public IMemory Memory = null!;
}