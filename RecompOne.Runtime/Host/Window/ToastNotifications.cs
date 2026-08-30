using System.Numerics;
using ImGuiNET;

namespace RecompOne.Runtime.Host.Window;

public static class ToastNotifications
{
    private const float SlideIn = 0.28f;
    private const float SlideOut = 0.22f;
    private const float DefaultDuration = 5f;
    private const float Width = 330f;
    private const float Margin = 12f;
    private const float Spacing = 8f;
    private const float IconSize = 34f;
    private const float AccentBar = 3f;

    private sealed class Toast
    {
        public int Id;
        public string Title = "";
        public string Message = "";
        public Func<uint>? Icon;
        public uint Texture;
        public bool TextureResolved;
        public float Duration;
        public float Enter;
        public float Age;
        public float Fade;
        public float Y;
        public bool Placed;
        public bool Hovered;
        public bool Closing;
    }

    private static readonly List<Toast> _toasts = [];
    private static readonly object _gate = new();
    private static int _nextId;

    public static void Show(string titleKey, string messageKey, Func<uint>? icon = null,
        float duration = DefaultDuration)
    {
        ShowText(Localization.T(titleKey), Localization.T(messageKey), icon, duration);
    }

    public static void ShowText(string title, string message, Func<uint>? icon = null, float duration = DefaultDuration)
    {
        lock (_gate)
        {
            _toasts.Add(new Toast
            {
                Id = ++_nextId,
                Title = title ?? "",
                Message = message ?? "",
                Icon = icon,
                Duration = duration <= 0f ? DefaultDuration : duration
            });
        }
    }

    public static void Clear()
    {
        lock (_gate)
        {
            _toasts.Clear();
        }
    }

    public static void Draw() //draw in panel not outside
    {
        Toast[] toasts;
        lock (_gate)
        {
            if (_toasts.Count == 0) return;
            toasts = _toasts.ToArray();
        }

        var origin = ImGui.GetWindowPos();
        var areaMin = origin + ImGui.GetWindowContentRegionMin();
        var areaMax = origin + ImGui.GetWindowContentRegionMax();
        if (areaMax.X - areaMin.X < 1f || areaMax.Y - areaMin.Y < 1f) return;

        var style = ImGui.GetStyle();
        var dt = ImGui.GetIO().DeltaTime;
        var scale = Theme.Scale;
        var margin = Margin * scale;
        var spacing = Spacing * scale;
        var width = MathF.Min(Width * scale, areaMax.X - areaMin.X - margin * 2f);

        var windowHovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows);
        var right = areaMax.X - margin;
        var stackY = areaMin.Y + margin;

        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(areaMin, areaMax, true);

        foreach (var toast in toasts)
        {
            toast.Enter += dt;
            if (!toast.Closing && !toast.Hovered) toast.Age += dt;
            if (!toast.Closing && toast.Age >= toast.Duration) toast.Closing = true;
            if (toast.Closing) toast.Fade += dt;

            var progress = toast.Closing
                ? 1f - Ease(Math.Clamp(toast.Fade / SlideOut, 0f, 1f))
                : Ease(Math.Clamp(toast.Enter / SlideIn, 0f, 1f));

            var height = Measure(toast, width, scale, style, out var rowHeight, out var textHeight, out var textWidth);

            if (!toast.Placed)
            {
                toast.Y = stackY;
                toast.Placed = true;
            }
            else
            {
                toast.Y += (stackY - toast.Y) * Math.Min(1f, dt * 14f);
            }

            var slide = (1f - progress) * (width + margin * 2f);
            var min = new Vector2(right - width + slide, toast.Y);
            var max = min + new Vector2(width, height);

            Paint(draw, toast, min, max, progress, rowHeight, textHeight, textWidth, scale, style);

            toast.Hovered = windowHovered && ImGui.IsMouseHoveringRect(min, max);
            if (toast.Hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) toast.Closing = true;

            stackY += height + spacing;
        }

        draw.PopClipRect();

        lock (_gate)
        {
            _toasts.RemoveAll(t => t.Closing && t.Fade >= SlideOut);
        }
    }

    private static float Measure(Toast toast, float width, float scale, ImGuiStylePtr style, out float rowHeight,
        out float textHeight, out float textWidth) //icon calc
    {
        var icon = Texture(toast) != 0 ? IconSize * scale : 0f;
        var inner = AccentBar * scale + style.WindowPadding.X;

        textWidth = width - inner - style.WindowPadding.X;
        if (icon > 0f) textWidth -= icon + style.ItemSpacing.X;
        if (textWidth < 1f) textWidth = 1f;

        var titleHeight = toast.Title.Length > 0 ? ImGui.CalcTextSize(toast.Title, false, textWidth).Y : 0f;
        var messageHeight = toast.Message.Length > 0 ? ImGui.CalcTextSize(toast.Message, false, textWidth).Y : 0f;

        textHeight = titleHeight + messageHeight;
        if (titleHeight > 0f && messageHeight > 0f) textHeight += style.ItemSpacing.Y;

        rowHeight = MathF.Max(icon, textHeight);
        return rowHeight + style.WindowPadding.Y * 2f;
    }

    private static void Paint(ImDrawListPtr draw, Toast toast, Vector2 min, Vector2 max, float alpha,
        float rowHeight, float textHeight, float textWidth, float scale, ImGuiStylePtr style)
    {
        var rounding = style.WindowRounding;
        var bar = AccentBar * scale;

        draw.AddRectFilled(min, max, Fade(style.Colors[(int)ImGuiCol.PopupBg], alpha * 0.97f), rounding);

        draw.PushClipRect(min, new Vector2(min.X + bar, max.Y), true);
        draw.AddRectFilled(min, max, Fade(Theme.Accent, alpha), rounding, ImDrawFlags.RoundCornersLeft);
        draw.PopClipRect();

        draw.AddRect(min, max, Fade(style.Colors[(int)ImGuiCol.Border], alpha), rounding);

        var x = min.X + bar + style.WindowPadding.X;
        var top = min.Y + style.WindowPadding.Y;

        var texture = Texture(toast);
        if (texture != 0)
        {
            var size = IconSize * scale;
            var at = new Vector2(x, top + (rowHeight - size) * 0.5f);
            draw.AddImage((nint)texture, at, at + new Vector2(size, size), Vector2.Zero, Vector2.One,
                Fade(Vector4.One, alpha));
            x += size + style.ItemSpacing.X;
        }

        var font = ImGui.GetFont();
        var fontSize = ImGui.GetFontSize();
        var y = top + (rowHeight - textHeight) * 0.5f;

        if (toast.Title.Length > 0)
        {
            draw.AddText(font, fontSize, new Vector2(x, y), Fade(Theme.AccentText, alpha), toast.Title, textWidth);
            y += ImGui.CalcTextSize(toast.Title, false, textWidth).Y + style.ItemSpacing.Y;
        }

        if (toast.Message.Length > 0)
            draw.AddText(font, fontSize, new Vector2(x, y), Fade(style.Colors[(int)ImGuiCol.Text], alpha),
                toast.Message, textWidth);
    }

    private static uint Fade(Vector4 color, float alpha)
    {
        return ImGui.ColorConvertFloat4ToU32(color with { W = color.W * alpha });
    }

    private static uint Texture(Toast toast)
    {
        if (toast.TextureResolved) return toast.Texture;
        toast.TextureResolved = true;
        if (toast.Icon == null) return 0;

        try
        {
            toast.Texture = toast.Icon();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[Toast] icon failed: {e.Message}");
        }

        return toast.Texture;
    }

    private static float Ease(float t)
    {
        return 1f - MathF.Pow(1f - t, 3f);
        //easy the position to make it smoooooooth
    }
}