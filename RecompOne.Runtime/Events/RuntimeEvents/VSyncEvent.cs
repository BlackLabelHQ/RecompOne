namespace RecompOne.Runtime.Events;

/// <summary>fires once per frame on vsync.</summary>
public sealed class VSyncEvent : GameEvent
{
    public long Frame;
}