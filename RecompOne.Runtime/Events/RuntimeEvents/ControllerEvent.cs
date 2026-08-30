namespace RecompOne.Runtime.Events;

/// <summary>raw input from a host gamepad, before it maps to psx pad</summary>
public sealed class ControllerEvent : IEvent
{
    public int Device;
    public int Button = -1;
    public bool Pressed;
    public int Axis = -1;
    public float Value;
}