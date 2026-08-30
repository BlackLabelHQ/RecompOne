namespace RecompOne.Runtime.Hle;

public sealed class VramShadow //in ram vram
{
    public const int Width = 1024;
    public const int Height = 512;

    // raw bgr555 buffer, index = y * Width + x
    public readonly ushort[] Pixels = new ushort[Width * Height];

    public ushort this[int x, int y]
    {
        get => Pixels[(y & (Height - 1)) * Width + (x & (Width - 1))];
        set => Pixels[(y & (Height - 1)) * Width + (x & (Width - 1))] = value;
    }

    public void Clear()
    {
        Array.Clear(Pixels);
    }
}