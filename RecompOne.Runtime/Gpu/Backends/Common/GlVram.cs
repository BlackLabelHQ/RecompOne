using Silk.NET.OpenGL;

namespace RecompOne.Runtime.Hle;

public static class GlVram
{
    public static int Scale { get; set; } = 4;
    public static bool TrueColor { get; set; }
    public static InternalFormat Storage => TrueColor ? InternalFormat.Rgba8 : InternalFormat.Rgb5A1;
    public static int Width => VramShadow.Width * Scale;
    public static int Height => VramShadow.Height * Scale;
}