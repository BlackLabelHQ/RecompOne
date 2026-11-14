using System.Globalization;
using System.Xml.Linq;

namespace RecompOne.UI.BigPicture.Glyphs.Svg;

internal readonly record struct SvgShape(List<List<Vector2>> Contours, byte R, byte G, byte B, float Alpha);

//svg renderer
public sealed class SvgIcon
{
    private readonly List<SvgShape> _shapes = [];

    internal IReadOnlyList<SvgShape> Shapes => _shapes;

    public float Width { get; private init; }
    public float Height { get; private init; }

    public static SvgIcon Parse(string xml)
    {
        var root = XDocument.Parse(xml).Root ?? throw new FormatException("empty svg");
        var box = ((string?)root.Attribute("viewBox"))?.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        var icon = new SvgIcon
        {
            Width = box is { Length: 4 } ? SvgPath.Number(box[2]) : SvgPath.Number((string?)root.Attribute("width") ?? "64"),
            Height = box is { Length: 4 } ? SvgPath.Number(box[3]) : SvgPath.Number((string?)root.Attribute("height") ?? "64")
        };

        foreach (var path in root.Descendants().Where(e => e.Name.LocalName == "path"))
        {
            var fill = Attr(path, "fill") ?? "#000000";
            if (fill == "none") continue;
            var alpha = Opacity(path, "fill-opacity") * Opacity(path, "opacity");
            if (alpha <= 0f) continue;
            var (r, g, b) = Color(fill);
            var contours = SvgPath.Contours((string?)path.Attribute("d") ?? "");
            if (contours.Count > 0) icon._shapes.Add(new SvgShape(contours, r, g, b, alpha));
        }

        return icon;
    }

    public byte[] Rasterize(int width, int height)
    {
        return SvgRasterizer.Rasterize(this, width, height);
    }

    private static string? Attr(XElement element, string name)
    {
        for (var e = element; e != null; e = e.Parent)
            if ((string?)e.Attribute(name) is { } value)
                return value;
        return null;
    }

    private static float Opacity(XElement element, string name)
    {
        var value = 1f;
        for (var e = element; e != null; e = e.Parent)
            if ((string?)e.Attribute(name) is { } own)
                value *= SvgPath.Number(own);
        return value;
    }

    private static (byte, byte, byte) Color(string fill)
    {
        var hex = fill.TrimStart('#');
        if (hex.Length == 3) hex = string.Concat(hex.Select(c => $"{c}{c}"));
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return (255, 255, 255);
        return ((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
}
