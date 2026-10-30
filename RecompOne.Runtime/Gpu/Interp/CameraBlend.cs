using System.Numerics;

namespace RecompOne.Runtime.Interp;

internal static class CameraBlend
{
    private const float Unit = 4096f;

    public static bool Pick(FrameGraph graph, out TransformRecord camera)
    {
        camera = default;
        var best = -1;
        for (var i = 0; i < graph.Transforms.Count; i++)
        {
            var transform = graph.Transforms[i];
            if (transform.HasCamera && (best < 0 || transform.Tris > graph.Transforms[best].Tris)) best = i;
        }

        if (best < 0) return false;
        camera = graph.Transforms[best];
        return true;
    }

    public static void Blend(in TransformRecord from, in TransformRecord to, float weight, Span<float> rotation, Span<float> translation)
    {
        var mirrored = Mirrored(in to);
        var start = Mirrored(in from) == mirrored && !TransformInterp.CameraJump(in from, in to) ? from : to;
        var matrix = Matrix4x4.CreateFromQuaternion(Quaternion.Slerp(Orientation(in start, mirrored), Orientation(in to, mirrored), weight));

        Span<float> scale = stackalloc float[3];
        scale[0] = float.Lerp(Length(start.C0, start.C1, start.C2), Length(to.C0, to.C1, to.C2), weight);
        scale[1] = float.Lerp(Length(start.C3, start.C4, start.C5), Length(to.C3, to.C4, to.C5), weight);
        scale[2] = float.Lerp(Length(start.C6, start.C7, start.C8), Length(to.C6, to.C7, to.C8), weight) * (mirrored ? -1f : 1f);

        rotation[0] = matrix.M11 * scale[0]; rotation[1] = matrix.M12 * scale[0]; rotation[2] = matrix.M13 * scale[0];
        rotation[3] = matrix.M21 * scale[1]; rotation[4] = matrix.M22 * scale[1]; rotation[5] = matrix.M23 * scale[1];
        rotation[6] = matrix.M31 * scale[2]; rotation[7] = matrix.M32 * scale[2]; rotation[8] = matrix.M33 * scale[2];

        translation[0] = float.Lerp(start.CX, to.CX, weight);
        translation[1] = float.Lerp(start.CY, to.CY, weight);
        translation[2] = float.Lerp(start.CZ, to.CZ, weight);
    }

    private static float Length(short a, short b, short c)
    {
        return MathF.Sqrt((float)a * a + (float)b * b + (float)c * c) / Unit;
    }

    private static bool Mirrored(in TransformRecord camera)
    {
        var r0 = new Vector3(camera.C0, camera.C1, camera.C2);
        var r1 = new Vector3(camera.C3, camera.C4, camera.C5);
        var r2 = new Vector3(camera.C6, camera.C7, camera.C8);
        return Vector3.Dot(Vector3.Cross(r0, r1), r2) < 0f;
    }

    private static Quaternion Orientation(in TransformRecord camera, bool mirrored)
    {
        var r0 = Vector3.Normalize(new Vector3(camera.C0, camera.C1, camera.C2));
        var r1 = Vector3.Normalize(new Vector3(camera.C3, camera.C4, camera.C5));
        var r2 = Vector3.Normalize(new Vector3(camera.C6, camera.C7, camera.C8)) * (mirrored ? -1f : 1f);
        var matrix = new Matrix4x4(r0.X, r0.Y, r0.Z, 0f, r1.X, r1.Y, r1.Z, 0f, r2.X, r2.Y, r2.Z, 0f, 0f, 0f, 0f, 1f);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(matrix));
    }
}
