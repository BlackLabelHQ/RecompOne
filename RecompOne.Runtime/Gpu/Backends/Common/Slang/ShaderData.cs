using System.Numerics;

namespace RecompOne.Runtime.Hle;

public static class ShaderData
{
    internal readonly record struct Texture(int Width, int Height, float[] Rgba, int Version);

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, Vector4> Values = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Matrix4x4> Matrices = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Texture> Textures = new(StringComparer.Ordinal);
    private static int _version;
    private static int _textureVersion;

    public static event Action? BeforeFrame;

    public static int Version => Volatile.Read(ref _version);

    public static void Set(string name, float x, float y = 0f, float z = 0f, float w = 0f)
    {
        Set(name, new Vector4(x, y, z, w));
    }

    public static void Set(string name, Vector2 value)
    {
        Set(name, new Vector4(value, 0f, 0f));
    }

    public static void Set(string name, Vector3 value)
    {
        Set(name, new Vector4(value, 0f));
    }

    public static void Set(string name, Vector4 value)
    {
        lock (Gate)
        {
            if (Values.TryGetValue(name, out var old) && old == value) return;
            Values[name] = value;
            _version++;
        }
    }

    public static void Set(string name, Matrix4x4 value)
    {
        lock (Gate)
        {
            if (Matrices.TryGetValue(name, out var old) && old == value) return;
            Matrices[name] = value;
            _version++;
        }
    }

    public static void SetTexture(string name, int width, int height, ReadOnlySpan<float> rgba)
    {
        if (width <= 0 || height <= 0 || rgba.Length < width * height * 4)
            throw new ArgumentException("rgba needs width * height * 4 floats", nameof(rgba));

        lock (Gate)
        {
            Textures[name] = new Texture(width, height, rgba[..(width * height * 4)].ToArray(), ++_textureVersion);
            _version++;
        }
    }

    public static void Remove(string name)
    {
        lock (Gate)
        {
            if (Values.Remove(name) | Matrices.Remove(name) | Textures.Remove(name)) _version++;
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Values.Clear();
            Matrices.Clear();
            Textures.Clear();
            _version++;
        }
    }

    internal static void RaiseBeforeFrame()
    {
        var handlers = BeforeFrame;
        if (handlers == null) return;

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action)handler)();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"[gpu-pfx] shader data hook failed: {e.Message}");
            }
        }
    }

    internal static void Snapshot(Dictionary<string, Vector4> values, Dictionary<string, Matrix4x4> matrices,
        Dictionary<string, Texture> textures)
    {
        lock (Gate)
        {
            values.Clear();
            matrices.Clear();
            textures.Clear();
            foreach (var (name, value) in Values) values[name] = value;
            foreach (var (name, value) in Matrices) matrices[name] = value;
            foreach (var (name, value) in Textures) textures[name] = value;
        }
    }
}
