namespace RecompOne.Runtime.Events;

/// <summary>fires when the game sets the draw area or clip</summary>
public sealed class DrawEnvEvent : GameEvent
{
    public int ClipX, ClipY, ClipW, ClipH;
    public int OfsX, OfsY;
    public bool IsBackground;
}