namespace RecompOne.UI.BigPicture.Glyphs.Svg;

internal static class SvgRasterizer
{
    private const int Samples = 4;

    public static byte[] Rasterize(SvgIcon icon, int width, int height)
    {
        var rgba = new byte[width * height * 4];
        var scale = new Vector2(width / icon.Width, height / icon.Height);
        var coverage = new int[width];
        var crossings = new List<(float X, int Dir)>();

        foreach (var shape in icon.Shapes)
        {
            var edges = new List<(Vector2 A, Vector2 B)>();
            foreach (var contour in shape.Contours)
                for (var i = 0; i < contour.Count; i++)
                    edges.Add((contour[i] * scale, contour[(i + 1) % contour.Count] * scale));

            for (var y = 0; y < height; y++)
            {
                Array.Clear(coverage);
                for (var sy = 0; sy < Samples; sy++)
                {
                    var line = y + (sy + 0.5f) / Samples;
                    crossings.Clear();
                    foreach (var (a, b) in edges)
                    {
                        if (a.Y <= line == b.Y <= line) continue;
                        var t = (line - a.Y) / (b.Y - a.Y);
                        crossings.Add((a.X + (b.X - a.X) * t, b.Y > a.Y ? 1 : -1));
                    }

                    crossings.Sort((l, r) => l.X.CompareTo(r.X));
                    var winding = 0;
                    for (var i = 0; i < crossings.Count - 1; i++)
                    {
                        winding += crossings[i].Dir;
                        if (winding != 0) Fill(coverage, crossings[i].X, crossings[i + 1].X, width);
                    }
                }

                for (var x = 0; x < width; x++)
                {
                    if (coverage[x] == 0) continue;
                    Blend(rgba, (y * width + x) * 4, shape, shape.Alpha * coverage[x] / (Samples * Samples));
                }
            }
        }

        return rgba;
    }

    private static void Fill(int[] coverage, float from, float to, int width)
    {
        var first = Math.Max(0, (int)MathF.Ceiling(from * Samples - 0.5f));
        var last = Math.Min(width * Samples, (int)MathF.Ceiling(to * Samples - 0.5f));
        for (var s = first; s < last; s++) coverage[s / Samples]++;
    }

    private static void Blend(byte[] rgba, int at, SvgShape shape, float alpha)
    {
        var below = rgba[at + 3] / 255f;
        var result = alpha + below * (1f - alpha);
        if (result <= 0f) return;
        rgba[at] = Mix(shape.R, rgba[at]);
        rgba[at + 1] = Mix(shape.G, rgba[at + 1]);
        rgba[at + 2] = Mix(shape.B, rgba[at + 2]);
        rgba[at + 3] = (byte)MathF.Round(result * 255f);

        byte Mix(byte top, byte bottom) => (byte)MathF.Round((top * alpha + bottom * below * (1f - alpha)) / result);
    }
}
