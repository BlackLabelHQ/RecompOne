using Silk.NET.OpenGL;

namespace RecompOne.Runtime.Hle;

public static class GpuGlAccess
{
    public static GL? Gl { get; internal set; }

    public static uint TargetFbo { get; internal set; }
    public static int TargetWidth { get; internal set; }
    public static int TargetHeight { get; internal set; }

    public static int TargetOriginX { get; internal set; }
    public static int TargetOriginY { get; internal set; }
    public static int TargetMargin { get; internal set; }

    public static int Scale => GlVram.Scale;
    public static bool Available => Gl != null && TargetFbo != 0;

    public static void Bind()
    {
        if (Gl == null) return;
        Gl.BindFramebuffer(FramebufferTarget.Framebuffer, TargetFbo);
        Gl.Viewport(0, 0, (uint)TargetWidth, (uint)TargetHeight);
    }
}