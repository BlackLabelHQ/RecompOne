namespace RecompOne.Runtime.Events;

/// <summary>fires when the game changes the display buffer</summary>
public sealed class DispEnvEvent : GameEvent
{
    public int X, Y, W, H;
}