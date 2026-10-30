namespace RecompOne.Runtime.Hle;

internal sealed class ShaderCamera
{
    private const float Unit = 4096f;

    private readonly short[] _gteRotation = new short[9];
    private readonly int[] _gteTranslation = new int[3];

    public readonly float[] Rotation = new float[9];
    public readonly float[] Inverse = new float[9];
    public readonly float[] Translation = new float[3];
    public bool Available;

    public void Capture()
    {
        Available = Interp.Interp.Backend is { } interp && interp.TryShownCamera(Rotation, Translation);
        if (!Available && Gte.TryGetCamera(_gteRotation, _gteTranslation))
        {
            for (var i = 0; i < 9; i++) Rotation[i] = _gteRotation[i] / Unit;
            for (var i = 0; i < 3; i++) Translation[i] = _gteTranslation[i];
            Available = true;
        }

        if (Available) Available = Invert();
    }

    public void CopyFrom(ShaderCamera other)
    {
        other.Rotation.CopyTo(Rotation, 0);
        other.Inverse.CopyTo(Inverse, 0);
        other.Translation.CopyTo(Translation, 0);
        Available = other.Available;
    }

    private bool Invert()
    {
        var m = Rotation;
        var c0 = m[4] * m[8] - m[5] * m[7];
        var c1 = m[5] * m[6] - m[3] * m[8];
        var c2 = m[3] * m[7] - m[4] * m[6];
        var det = m[0] * c0 + m[1] * c1 + m[2] * c2;
        if (MathF.Abs(det) < 1e-8f) return false;

        var inv = 1f / det;
        Inverse[0] = c0 * inv;
        Inverse[1] = (m[2] * m[7] - m[1] * m[8]) * inv;
        Inverse[2] = (m[1] * m[5] - m[2] * m[4]) * inv;
        Inverse[3] = c1 * inv;
        Inverse[4] = (m[0] * m[8] - m[2] * m[6]) * inv;
        Inverse[5] = (m[2] * m[3] - m[0] * m[5]) * inv;
        Inverse[6] = c2 * inv;
        Inverse[7] = (m[1] * m[6] - m[0] * m[7]) * inv;
        Inverse[8] = (m[0] * m[4] - m[1] * m[3]) * inv;
        return true;
    }
}
