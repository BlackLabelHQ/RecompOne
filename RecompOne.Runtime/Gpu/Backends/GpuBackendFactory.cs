using Silk.NET.OpenGL;

namespace RecompOne.Runtime.Hle;

//the idea is to drop gl45 is gl33 is stable enought, in the future maybe add vulkan and dx backend?
public enum GlBackendKind
{
    Auto,
    Gl45,
    Gl33,
    Gl21
}

public static class GpuBackendFactory //fkn hate these factories
{
    public static GlBackendKind Selected { get; private set; }

    public static IGpuBackend Create(GL gl, GlBackendKind requested)
    {
        var has45 = Supports45(gl);
        var has33 = ContextAtLeast(gl, 3, 3);

        var kind = requested == GlBackendKind.Auto
            ? has45 ? GlBackendKind.Gl45 : has33 ? GlBackendKind.Gl33 : GlBackendKind.Gl21
            : requested;

        if (kind == GlBackendKind.Gl45 && !has45)
        {
            Console.WriteLine("[Gpu] gl45 requested but the support by your gpu is below 4.5, falling back to gl33");
            kind = GlBackendKind.Gl33;
        }

        if (kind == GlBackendKind.Gl33 && !has33)
        {
            Console.WriteLine("[Gpu] gl33 requested but the context is below 3.3, falling back to gl21");
            kind = GlBackendKind.Gl21;
        }

        if (kind == GlBackendKind.Gl21) ClampScaleToLimit(gl);

        Selected = kind;
        IGlVram vram = kind switch
        {
            GlBackendKind.Gl45 => new Gl45Vram(gl),
            GlBackendKind.Gl33 => new Gl33Vram(gl),
            _ => new Gl21Vram(gl)
        };
        Console.WriteLine($"[Gpu] backend: {kind}");
        return new GlCore(gl, vram, kind == GlBackendKind.Gl21);
    }

    private static bool ContextAtLeast(GL gl, int wantMajor, int wantMinor)
    {
        try
        {
            var major = gl.GetInteger(GLEnum.MajorVersion);
            var minor = gl.GetInteger(GLEnum.MinorVersion);
            if (major > 0) return major > wantMajor || (major == wantMajor && minor >= wantMinor);
        }
        catch
        {
        }

        var version = Str(gl, StringName.Version);
        var parts = version.Split('.', ' ');
        if (parts.Length >= 2 && int.TryParse(parts[0], out var m) && int.TryParse(parts[1], out var n))
            return m > wantMajor || (m == wantMajor && n >= wantMinor);
        return false;
    }

    //old gpus limit at 2048 so the 4x vram texture would not fit, im too tired to think about a better solutino so for now capping it to the limit
    private static void ClampScaleToLimit(GL gl)
    {
        int max;
        try
        {
            max = gl.GetInteger(GLEnum.MaxTextureSize);
        }
        catch
        {
            return;
        }

        if (max <= 0) return;

        while (GlVram.Scale > 1 &&
               (VramShadow.Width * GlVram.Scale > max || VramShadow.Height * GlVram.Scale > max))
        {
            GlVram.Scale /= 2;
            Console.WriteLine($"[Gpu] max texture size support is {max}, dropping the vram scale to {GlVram.Scale}");
        }
    }

    private static bool Supports45(GL gl)
    {
        int major = 0, minor = 0;
        try
        {
            major = gl.GetInteger(GLEnum.MajorVersion);
            minor = gl.GetInteger(GLEnum.MinorVersion);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Gpu] could not read the context version: {e.Message}");
        }

        var version = Str(gl, StringName.Version);
        var renderer = Str(gl, StringName.Renderer);
        Console.WriteLine($"[Gpu] context: {major}.{minor} ({version}) on {renderer}");

        if (major > 4 || (major == 4 && minor >= 5)) return true;

        var barrier = HasExtension(gl, "GL_ARB_texture_barrier");
        var copyImage = HasExtension(gl, "GL_ARB_copy_image");
        if (barrier && copyImage)
        {
            Console.WriteLine("[Gpu] context is below 4.5 but exposes texture barrier and copy image");
            return true;
        }

        Console.WriteLine($"[Gpu] gl45 unavailable (texture barrier: {barrier}, copy image: {copyImage})");
        return false;
    }

    private static bool HasExtension(GL gl, string name)
    {
        try
        {
            var count = gl.GetInteger(GLEnum.NumExtensions);
            for (uint i = 0; i < count; i++)
                if (gl.GetStringS(StringName.Extensions, i) == name)
                    return true;
        }
        catch
        {
        }

        return false;
    }

    private static string Str(GL gl, StringName name)
    {
        try
        {
            return gl.GetStringS(name) ?? "?";
        }
        catch
        {
            return "?";
        }
    }

    public static GlBackendKind Parse(string? s)
    {
        return s?.ToLowerInvariant() switch
        {
            "gl45" => GlBackendKind.Gl45,
            "gl33" => GlBackendKind.Gl33,
            "gl21" => GlBackendKind.Gl21,
            _ => GlBackendKind.Auto
        };
    }
}