using System.Globalization;
using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Config;

namespace RecompOne.Runtime.Host.Window;

//theme variables here
public static class Theme
{
    public readonly record struct Preset(string LabelKey, Vector4 Color);

    public static readonly Preset[] Accents =
    [
        new("theme.blue", Rgb(0x3B82F6)),
        new("theme.purple", Rgb(0x8B5CF6)),
        new("theme.crimson", Rgb(0xE1385C)),
        new("theme.emerald", Rgb(0x22A06B)),
        new("theme.amber", Rgb(0xD98324)),
        new("theme.teal", Rgb(0x14A3AD)),
        new("theme.slate", Rgb(0x64748B))
    ];

    public static readonly Preset[] Backgrounds =
    [
        new("background.dark", Rgb(0x1B1B1D)),
        new("background.black", Rgb(0x000000)),
        new("background.gray", Rgb(0x3A3A3E)),
        new("background.light", Rgb(0xF2F2F4))
    ];

    public static Vector4 Accent { get; private set; } = Accents[0].Color;

    public static Vector4 Background { get; private set; } = Backgrounds[0].Color;

    public static bool IsLight => Luminance(Background) > 0.5f;

    public static Vector4 TitleBar => Shade(Accent, IsLight ? 0.05f : -0.15f);

    public static Vector4 AccentText => IsLight
        ? Vector4.Lerp(Accent, Vector4.Zero, 0.25f) with { W = 1f }
        : Vector4.Lerp(Accent, Vector4.One, 0.45f) with { W = 1f }; //text be correct color thee

    public static Vector4 TitleBarText => Luminance(TitleBar) > 0.6f
        ? new Vector4(0.06f, 0.06f, 0.07f, 1f)
        : new Vector4(1f, 1f, 1f, 0.95f);

    public static void Load()
    {
        Accent = Parse(ConfigManager.View.Accent) ?? Accents[0].Color;
        Background = Parse(ConfigManager.View.Background) ?? Backgrounds[0].Color;
        Apply();
    }

    public static void SetAccent(Vector4 accent)
    {
        Accent = accent with { W = 1f };
        ConfigManager.View.Accent = ToHex(Accent);
        Apply();
    }

    public static void SetBackground(Vector4 background)
    {
        Background = background with { W = 1f };
        ConfigManager.View.Background = ToHex(Background);
        Apply();
    }

    public static int MatchAccent()
    {
        return Match(Accents, Accent);
    }

    public static int MatchBackground()
    {
        return Match(Backgrounds, Background);
    }

    private static int Match(Preset[] presets, Vector4 color)
    {
        for (var i = 0; i < presets.Length; i++)
            if (Vector4.Distance(presets[i].Color, color) < 0.004f)
                return i;
        return -1;
    }

    public static void Apply()
    {
        var style = ImGui.GetStyle();

        style.WindowRounding = 6f;
        style.ChildRounding = 4f;
        style.FrameRounding = 4f;
        style.PopupRounding = 6f;
        style.GrabRounding = 4f;
        style.TabRounding = 4f;
        style.ScrollbarRounding = 8f;

        style.WindowBorderSize = 1f;
        style.ChildBorderSize = 1f;
        style.PopupBorderSize = 1f;
        style.FrameBorderSize = 0f;

        style.WindowPadding = new Vector2(12f, 10f);
        style.FramePadding = new Vector2(8f, 4f);
        style.ItemSpacing = new Vector2(8f, 6f);
        style.ItemInnerSpacing = new Vector2(6f, 4f);
        style.CellPadding = new Vector2(6f, 4f);
        style.IndentSpacing = 18f;
        style.ScrollbarSize = 12f;
        style.GrabMinSize = 10f;
        style.SeparatorTextPadding = new Vector2(18f, 4f);
        style.WindowTitleAlign = new Vector2(0f, 0.5f);
        style.WindowMenuButtonPosition = ImGuiDir.Left;

        var accent = Accent;
        var text = IsLight ? new Vector4(0.10f, 0.10f, 0.12f, 1f) : new Vector4(0.90f, 0.91f, 0.93f, 1f);

        Set(ImGuiCol.Text, text);
        Set(ImGuiCol.TextDisabled, Vector4.Lerp(text, Background, 0.50f) with { W = 1f });
        Set(ImGuiCol.TextSelectedBg, accent with { W = 0.35f });

        Set(ImGuiCol.WindowBg, Surface(0f));
        Set(ImGuiCol.ChildBg, Surface(0.045f));
        Set(ImGuiCol.PopupBg, Surface(0.05f));
        Set(ImGuiCol.MenuBarBg, Surface(0.055f));
        Set(ImGuiCol.Border, Surface(0.22f));
        Set(ImGuiCol.BorderShadow, new Vector4(0f, 0f, 0f, 0f));

        Set(ImGuiCol.FrameBg, Surface(0.10f));
        Set(ImGuiCol.FrameBgHovered, Mix(Surface(0.10f), accent, 0.30f));
        Set(ImGuiCol.FrameBgActive, Mix(Surface(0.10f), accent, 0.45f));

        Set(ImGuiCol.TitleBg, Surface(0.04f));
        Set(ImGuiCol.TitleBgActive, TitleBar);
        Set(ImGuiCol.TitleBgCollapsed, Surface(0.04f));

        Set(ImGuiCol.Header, accent with { W = 0.42f });
        Set(ImGuiCol.HeaderHovered, accent with { W = 0.62f });
        Set(ImGuiCol.HeaderActive, accent with { W = 0.80f });

        Set(ImGuiCol.Button, Mix(Surface(0.12f), accent, 0.18f));
        Set(ImGuiCol.ButtonHovered, Mix(Surface(0.12f), accent, 0.65f));
        Set(ImGuiCol.ButtonActive, accent);

        Set(ImGuiCol.CheckMark, Vector4.Lerp(accent, IsLight ? Vector4.Zero : Vector4.One, 0.20f) with { W = 1f });
        Set(ImGuiCol.SliderGrab, Shade(accent, IsLight ? -0.05f : 0.10f));
        Set(ImGuiCol.SliderGrabActive, accent);

        Set(ImGuiCol.Separator, Surface(0.22f));
        Set(ImGuiCol.SeparatorHovered, accent with { W = 0.70f });
        Set(ImGuiCol.SeparatorActive, accent);

        Set(ImGuiCol.Tab, Surface(0.06f));
        Set(ImGuiCol.TabHovered, accent with { W = 0.70f });
        Set(ImGuiCol.TabActive, Mix(Surface(0.06f), accent, 0.55f));
        Set(ImGuiCol.TabUnfocused, Surface(0.04f));
        Set(ImGuiCol.TabUnfocusedActive, Mix(Surface(0.04f), accent, 0.30f));

        Set(ImGuiCol.DockingPreview, accent with { W = 0.45f });
        Set(ImGuiCol.DockingEmptyBg, Surface(-0.03f));

        Set(ImGuiCol.ScrollbarBg, Surface(0.02f));
        Set(ImGuiCol.ScrollbarGrab, Surface(0.20f));
        Set(ImGuiCol.ScrollbarGrabHovered, Surface(0.28f));
        Set(ImGuiCol.ScrollbarGrabActive, accent);

        Set(ImGuiCol.ResizeGrip, Surface(0.20f));
        Set(ImGuiCol.ResizeGripHovered, accent with { W = 0.70f });
        Set(ImGuiCol.ResizeGripActive, accent);

        Set(ImGuiCol.PlotLines, Vector4.Lerp(accent, Vector4.One, 0.30f));
        Set(ImGuiCol.PlotLinesHovered, Vector4.One);
        Set(ImGuiCol.PlotHistogram, accent);
        Set(ImGuiCol.PlotHistogramHovered, Vector4.Lerp(accent, Vector4.One, 0.30f));

        Set(ImGuiCol.TableHeaderBg, Surface(0.07f));
        Set(ImGuiCol.TableBorderStrong, Surface(0.20f));
        Set(ImGuiCol.TableBorderLight, Surface(0.12f));
        Set(ImGuiCol.TableRowBg, new Vector4(0f, 0f, 0f, 0f));
        Set(ImGuiCol.TableRowBgAlt, IsLight
            ? new Vector4(0f, 0f, 0f, 0.03f)
            : new Vector4(1f, 1f, 1f, 0.025f));

        Set(ImGuiCol.NavHighlight, accent);
        Set(ImGuiCol.ModalWindowDimBg, IsLight
            ? new Vector4(0.35f, 0.35f, 0.38f, 0.55f)
            : new Vector4(0.04f, 0.04f, 0.05f, 0.65f));

        style.ScaleAllSizes(Scale);
    }

    public static float Scale => HostWindow.DpiScale * ConfigManager.View.UiScale;

    public static float TitleBarHeight
    {
        get
        {
            var style = ImGui.GetStyle();
            return ImGui.GetFontSize() + style.FramePadding.Y * 2f + style.ItemSpacing.Y;
        }
    }

    private static void Set(ImGuiCol target, Vector4 color)
    {
        ImGui.GetStyle().Colors[(int)target] = color;
    }

    private static Vector4 Surface(float step)
    {
        var target = IsLight ? Vector4.Zero : Vector4.One;
        return Vector4.Lerp(Background, target, step) with { W = 1f };
    }

    private static Vector4 Mix(Vector4 a, Vector4 b, float amount)
    {
        return Vector4.Lerp(a, b, amount) with { W = a.W };
    }

    private static Vector4 Shade(Vector4 color, float amount)
    {
        return Vector4.Lerp(color, amount >= 0f ? Vector4.One : Vector4.Zero, MathF.Abs(amount)) with { W = color.W };
    }

    private static float Luminance(Vector4 c)
    {
        return c.X * 0.2126f + c.Y * 0.7152f + c.Z * 0.0722f;
    }

    private static Vector4 Rgb(uint hex)
    {
        return new Vector4(
            ((hex >> 16) & 0xFF) / 255f,
            ((hex >> 8) & 0xFF) / 255f,
            (hex & 0xFF) / 255f,
            1f);
    }

    private static string ToHex(Vector4 c)
    {
        return $"#{(int)(Math.Clamp(c.X, 0f, 1f) * 255f + 0.5f):X2}" +
               $"{(int)(Math.Clamp(c.Y, 0f, 1f) * 255f + 0.5f):X2}" +
               $"{(int)(Math.Clamp(c.Z, 0f, 1f) * 255f + 0.5f):X2}";
    }

    private static Vector4? Parse(string value)
    {
        var hex = value.TrimStart('#');
        if (hex.Length != 6) return null;
        return uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb) ? Rgb(rgb) : null;
    }
}