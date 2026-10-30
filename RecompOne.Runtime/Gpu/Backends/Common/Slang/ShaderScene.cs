using System.Numerics;

namespace RecompOne.Runtime.Hle;

public static class ShaderScene
{
    public static void SetSun(Vector3 towardLight, Vector3 color)
    {
        ShaderData.Set("SceneSunDirection", new Vector4(Normalize(towardLight), 1f));
        ShaderData.Set("SceneSunColor", color);
    }

    public static void SetFill(Vector3 towardLight, Vector3 color)
    {
        ShaderData.Set("SceneFillDirection", new Vector4(Normalize(towardLight), 1f));
        ShaderData.Set("SceneFillColor", color);
    }

    public static void SetAmbient(Vector3 color)
    {
        ShaderData.Set("SceneAmbient", new Vector4(color, 1f));
    }

    public static void SetFog(Vector3 color, float near, float far)
    {
        ShaderData.Set("SceneFogColor", new Vector4(color, 1f));
        ShaderData.Set("SceneFogRange", near, far);
    }

    public static void SetIndoor(float amount)
    {
        ShaderData.Set("SceneIndoor", Math.Clamp(amount, 0f, 1f), 1f);
    }

    public static void SetViewDistance(float distance)
    {
        ShaderData.Set("SceneViewDistance", distance, 1f);
    }

    public static void SetWaterLevel(float worldHeight)
    {
        ShaderData.Set("SceneWaterLevel", worldHeight, 1f);
    }

    public static void Clear()
    {
        foreach (var name in Names) ShaderData.Remove(name);
    }

    private static readonly string[] Names =
    [
        "SceneSunDirection", "SceneSunColor", "SceneFillDirection", "SceneFillColor", "SceneAmbient",
        "SceneFogColor", "SceneFogRange", "SceneIndoor", "SceneViewDistance", "SceneWaterLevel"
    ];

    private static Vector3 Normalize(Vector3 v)
    {
        var length = v.Length();
        return length > 1e-6f ? v / length : Vector3.Zero;
    }
}
