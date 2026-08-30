namespace RecompOne.Runtime.Events;

public enum VramTransfer
{
    Load,
    Store,
    Move
}

/// <summary>fires on a vrram copy by: LoadImage, StoreImage or MoveImage</summary>
//TODO: wire this, add stuff to libgpu
public sealed class VramTransferEvent : GameEvent
{
    public int X, Y, W, H;
    public VramTransfer Direction;
}