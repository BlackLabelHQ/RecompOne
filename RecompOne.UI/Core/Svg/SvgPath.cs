using System.Globalization;
using System.Numerics;

namespace RecompOne.UI;

internal static class SvgPath
{
    private const int CurveSteps = 12;

    public static float Number(string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    public static List<List<Vector2>> Contours(string d)
    {
        var contours = new List<List<Vector2>>();
        List<Vector2>? contour = null;
        var tokens = Tokens(d);
        var at = 0;
        var command = 'M';
        var current = Vector2.Zero;
        var start = Vector2.Zero;
        var control = Vector2.Zero;
        var previous = ' ';

        while (at < tokens.Count)
        {
            if (tokens[at] is [var letter] && char.IsLetter(letter))
            {
                command = letter;
                at++;
                if (char.ToUpperInvariant(command) == 'Z')
                {
                    current = start;
                    contour = null;
                    previous = 'Z';
                    continue;
                }
            }

            var relative = char.IsLower(command);
            var origin = relative ? current : Vector2.Zero;
            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                    current = origin + Point();
                    start = current;
                    contour = [current];
                    contours.Add(contour);
                    command = relative ? 'l' : 'L';
                    break;
                case 'L':
                    LineTo(origin + Point());
                    break;
                case 'H':
                    LineTo(new Vector2((relative ? current.X : 0f) + Next(), current.Y));
                    break;
                case 'V':
                    LineTo(new Vector2(current.X, (relative ? current.Y : 0f) + Next()));
                    break;
                case 'Q':
                {
                    var c = origin + Point();
                    Quad(c, origin + Point());
                    break;
                }
                case 'T':
                {
                    var c = previous is 'Q' or 'T' ? current * 2f - control : current;
                    Quad(c, origin + Point());
                    break;
                }
                case 'C':
                {
                    var c1 = origin + Point();
                    var c2 = origin + Point();
                    Cubic(c1, c2, origin + Point());
                    break;
                }
                case 'S':
                {
                    var c1 = previous is 'C' or 'S' ? current * 2f - control : current;
                    var c2 = origin + Point();
                    Cubic(c1, c2, origin + Point());
                    break;
                }
                default:
                    at++;
                    break;
            }

            previous = char.ToUpperInvariant(command);
        }

        contours.RemoveAll(c => c.Count < 3);
        return contours;

        float Next() => at < tokens.Count ? Number(tokens[at++]) : 0f;
        Vector2 Point() => new(Next(), Next());

        void LineTo(Vector2 to)
        {
            if (contour == null)
            {
                contour = [current];
                contours.Add(contour);
            }

            contour.Add(to);
            current = to;
        }

        void Quad(Vector2 c, Vector2 to)
        {
            var from = current;
            for (var i = 1; i <= CurveSteps; i++)
            {
                var t = i / (float)CurveSteps;
                var u = 1f - t;
                LineTo(from * (u * u) + c * (2f * u * t) + to * (t * t));
            }

            control = c;
        }

        void Cubic(Vector2 c1, Vector2 c2, Vector2 to)
        {
            var from = current;
            for (var i = 1; i <= CurveSteps; i++)
            {
                var t = i / (float)CurveSteps;
                var u = 1f - t;
                LineTo(from * (u * u * u) + c1 * (3f * u * u * t) + c2 * (3f * u * t * t) + to * (t * t * t));
            }

            control = c2;
        }
    }

    private static List<string> Tokens(string d)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < d.Length)
        {
            var c = d[i];
            if (char.IsWhiteSpace(c) || c == ',')
            {
                i++;
                continue;
            }

            if (char.IsLetter(c) && c is not ('e' or 'E'))
            {
                tokens.Add(c.ToString());
                i++;
                continue;
            }

            var begin = i;
            if (c is '-' or '+') i++;
            var dot = false;
            while (i < d.Length)
            {
                var n = d[i];
                if (char.IsDigit(n)) i++;
                else if (n == '.' && !dot)
                {
                    dot = true;
                    i++;
                }
                else if (n is 'e' or 'E')
                {
                    i++;
                    if (i < d.Length && d[i] is '-' or '+') i++;
                }
                else break;
            }

            if (i == begin) i++;
            else tokens.Add(d[begin..i]);
        }

        return tokens;
    }
}
