using System.Numerics;

namespace RecompOne.UI;

public sealed record InlineIcon(Func<float, float> Width, Action<ImDrawListPtr, Vector2, float, uint> Draw);

public static class InlineIcons //text icon wraper manager thing 
{
    public const string Open = "[[";
    public const string Close = "]]";
    public const float IconScale = 1.3f;

    public static Func<string, InlineIcon?>? Resolver { get; set; }

    public static bool Has(string text) => text.Contains(Open, StringComparison.Ordinal);

    public static string Token(string name) => Open + name + Close;

    public static InlineIcon? Find(string name) => Resolver?.Invoke(name);

    public static Vector2 Measure(ImFontPtr font, float fontSize, string text, float wrap = 0f)
    {
        var lines = Layout(font, fontSize, text, wrap, out var lineHeight);
        var width = 0f;
        foreach (var line in lines) width = MathF.Max(width, line.Width);
        return new Vector2(width, lines.Count * lineHeight);
    }

    public static void Draw(ImDrawListPtr draw, ImFontPtr font, float fontSize, Vector2 pos, uint color, string text,
        float wrap = 0f, bool centered = false)
    {
        var lines = Layout(font, fontSize, text, wrap, out var lineHeight);
        var y = pos.Y;
        foreach (var line in lines)
        {
            var x = pos.X;
            if (centered && wrap > 0f) x += MathF.Round((wrap - line.Width) * 0.5f);
            DrawLine(draw, font, fontSize, new Vector2(x, y), lineHeight, color, line);
            y += lineHeight;
        }
    }

    public static void Text(string text, bool centered = false)
    {
        var font = ImGui.GetFont();
        var fontSize = ImGui.GetFontSize();
        var wrap = ImGui.GetContentRegionAvail().X;
        var pos = ImGui.GetCursorScreenPos();
        var size = Measure(font, fontSize, text, wrap);
        Draw(ImGui.GetWindowDrawList(), font, fontSize, pos, ImGui.GetColorU32(ImGuiCol.Text), text, wrap, centered);
        ImGui.Dummy(new Vector2(centered ? wrap : size.X, size.Y));
    }

    private readonly record struct Piece(string Text, InlineIcon? Icon, float Width, bool Space);

    private sealed class Line
    {
        public readonly List<Piece> Pieces = [];
        public float Width;
    }

    private static List<Line> Layout(ImFontPtr font, float fontSize, string text, float wrap, out float lineHeight)
    {
        var iconSize = IconSize(fontSize);
        lineHeight = MathF.Max(fontSize, iconSize) + ImGui.GetStyle().ItemSpacing.Y * 0.5f;
        var space = ImGuiUtils.TextSize(font, fontSize, " ").X;
        var lines = new List<Line>();

        foreach (var paragraph in text.Split('\n'))
        {
            var line = new Line();
            lines.Add(line);
            foreach (var word in paragraph.Split(' '))
            {
                var pieces = Pieces(font, fontSize, iconSize, word);
                var width = 0f;
                foreach (var piece in pieces) width += piece.Width;

                var lead = line.Pieces.Count > 0 ? space : 0f;
                if (wrap > 0f && line.Pieces.Count > 0 && line.Width + lead + width > wrap)
                {
                    line = new Line();
                    lines.Add(line);
                    lead = 0f;
                }

                if (lead > 0f) line.Pieces.Add(new Piece(" ", null, lead, true));
                line.Pieces.AddRange(pieces);
                line.Width += lead + width;
            }
        }

        return lines;
    }

    private static List<Piece> Pieces(ImFontPtr font, float fontSize, float iconSize, string word)
    {
        var pieces = new List<Piece>();
        var at = 0;
        while (at < word.Length)
        {
            var open = word.IndexOf(Open, at, StringComparison.Ordinal);
            var close = open < 0 ? -1 : word.IndexOf(Close, open + Open.Length, StringComparison.Ordinal);
            if (open < 0 || close < 0)
            {
                Add(word[at..]);
                break;
            }

            if (open > at) Add(word[at..open]);
            var name = word[(open + Open.Length)..close];
            var icon = Find(name);
            if (icon != null) pieces.Add(new Piece(name, icon, icon.Width(iconSize), false));
            else Add(name);
            at = close + Close.Length;
        }

        return pieces;

        void Add(string part)
        {
            if (part.Length == 0) return;
            pieces.Add(new Piece(part, null, ImGuiUtils.TextSize(font, fontSize, part).X, false));
        }
    }

    private static float IconSize(float fontSize) => MathF.Round(fontSize * IconScale);

    private static void DrawLine(ImDrawListPtr draw, ImFontPtr font, float fontSize, Vector2 pos, float lineHeight,
        uint color, Line line)
    {
        var x = pos.X;
        var iconSize = IconSize(fontSize);
        var textY = MathF.Round(pos.Y + (lineHeight - fontSize) * 0.5f);
        var iconY = MathF.Round(pos.Y + (lineHeight - iconSize) * 0.5f);
        foreach (var piece in line.Pieces)
        {
            if (piece.Icon != null) piece.Icon.Draw(draw, new Vector2(MathF.Round(x), iconY), iconSize, color);
            else if (!piece.Space) ImGuiUtils.AddText(draw, font, fontSize, new Vector2(x, textY), color, piece.Text);
            x += piece.Width;
        }
    }
}
