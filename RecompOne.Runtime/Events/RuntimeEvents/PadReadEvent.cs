namespace RecompOne.Runtime.Events;

/// <summary>the game reads a controller port</summary>
public sealed class PadReadEvent : GameEvent
{
    public int Port;
    public ushort Buttons;
}