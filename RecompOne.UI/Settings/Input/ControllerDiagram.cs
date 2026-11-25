using System.Numerics;

namespace RecompOne.UI;

public readonly record struct DiagramResult(PadInput? Hovered, PadInput? Clicked);

public static class ControllerDiagram
{
    private const string DigitalResource = "Controllers/digital.svg";
    private const string AnalogResource = "Controllers/analog.svg";
    private const string PartPrefix = "pad-";
    private const float HighlightAlpha = 0.55f;
    private const float PressedAlpha = 0.8f;
    private const float PressedGlow = 0.35f;
    
    private sealed class Part(PadInput input, Vector2[] outline)
    {
        public readonly PadInput Input = input;
        public readonly Vector2[] Outline = outline;
        public readonly Vector2[] Screen = new Vector2[outline.Length];
        
        public bool Contains(Vector2 point)
        {
            var inside = false;
            for (int i = 0, j = Outline.Length - 1; i < Outline.Length; j = i++)
            {
                var a = Outline[i];
                var b = Outline[j];
                if (a.Y > point.Y == b.Y > point.Y) continue;
                if (point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            
            return inside;
        }
    }
    
    private sealed class Model(SvgIcon icon, Part[] parts)
    {
        public readonly SvgIcon Icon = icon;
        public readonly Part[] Parts = parts;
        public readonly Dictionary<int, uint> Textures = new();
    }
    
    private static readonly Dictionary<string, Model?> _models = new();
    
    public static DiagramResult Draw(string id, Vector2 size, bool analog, PadInput? highlighted, ushort pressed)
    {
        var min = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton(id, size);
        var hovering = ImGui.IsItemHovered();
        
        var model = Load(analog ? AnalogResource : DigitalResource);
        if (model == null) return default;
        
        var art = new Vector2(model.Icon.Width, model.Icon.Height);
        var scale = MathF.Min(size.X / art.X, size.Y / art.Y);
        var pixels = new Vector2(MathF.Max(1f, MathF.Round(art.X * scale)), MathF.Max(1f, MathF.Round(art.Y * scale)));
        scale = pixels.X / art.X;
        var origin = min + (size - pixels) * 0.5f;
        origin = new Vector2(MathF.Round(origin.X), MathF.Round(origin.Y));
        
        var draw = ImGui.GetWindowDrawList();
        var texture = Texture(model, (int)pixels.X, (int)pixels.Y);
        if (texture != 0) draw.AddImage(ImGuiUtils.Tex((nint)texture), origin, origin + pixels);
        
        PadInput? hovered = null;
        if (hovering)
        {
            var mouse = (ImGui.GetMousePos() - origin) / scale;
            for (var i = model.Parts.Length - 1; i >= 0; i--)
            {
                if (!model.Parts[i].Contains(mouse)) continue;
                hovered = model.Parts[i].Input;
                break;
            }
        }
        
        foreach (var part in model.Parts)
        {
            uint ink;
            if ((pressed & PadInputs.Mask(part.Input)) != 0)
                ink = Rgba(Vector4.Lerp(Theme.Accent, Vector4.One, PressedGlow) with { W = PressedAlpha });
            else if (part.Input == highlighted || part.Input == hovered)
                ink = Rgba(Theme.Accent with { W = HighlightAlpha });
            else
                continue;
            
            for (var i = 0; i < part.Outline.Length; i++) part.Screen[i] = origin + part.Outline[i] * scale;
            draw.AddConcavePolyFilled(ref part.Screen[0], part.Screen.Length, ink);
        }
        
        var clicked = hovered != null && ImGui.IsMouseClicked(ImGuiMouseButton.Left) ? hovered : null;
        return new DiagramResult(hovered, clicked);
    }
    
    private static uint Texture(Model model, int width, int height)
    {
        if (model.Textures.TryGetValue(width, out var cached)) return cached;
        var texture = HostWindow.UploadTexture(model.Icon.Rasterize(width, height), width, height);
        model.Textures[width] = texture;
        return texture;
    }
    
    private static Model? Load(string resource)
    {
        if (_models.TryGetValue(resource, out var model)) return model;
        model = null;
        try
        {
            using var stream = typeof(ControllerDiagram).Assembly.GetManifestResourceStream(resource);
            if (stream != null) model = Build(SvgIcon.Parse(new StreamReader(stream).ReadToEnd()));
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[ControllerDiagram] {resource}: {e.Message}");
        }
        
        _models[resource] = model;
        return model;
    }
    
    private static Model Build(SvgIcon icon)
    {
        var parts = new List<Part>();
        foreach (var (id, outline) in icon.Outlines)
        {
            if (!id.StartsWith(PartPrefix, StringComparison.Ordinal)) continue;
            if (!Enum.TryParse<PadInput>(id[PartPrefix.Length..], out var input)) continue;
            var points = Simplify(outline);
            if (points.Length >= 3) parts.Add(new Part(input, points));
        }
        
        return new Model(icon, parts.ToArray());
    }
    
    private static Vector2[] Simplify(Vector2[] outline)
    {
        var points = new List<Vector2>(outline.Length);
        foreach (var point in outline)
            if (points.Count == 0 || Vector2.DistanceSquared(points[^1], point) > 0.01f)
                points.Add(point);
        if (points.Count > 1 && Vector2.DistanceSquared(points[0], points[^1]) <= 0.01f) points.RemoveAt(points.Count - 1);
        return points.ToArray();
    }
    
    private static uint Rgba(Vector4 color) => ImGui.ColorConvertFloat4ToU32(color);
}
