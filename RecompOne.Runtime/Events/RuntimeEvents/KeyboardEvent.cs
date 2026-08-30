namespace RecompOne.Runtime.Events;

/// <summary>A host keyboard key was pressed</summary>
public sealed class KeyboardEvent : IEvent
{
    public int Key;
    public bool Pressed;
    public bool Repeat;
}