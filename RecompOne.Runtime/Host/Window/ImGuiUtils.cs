using System.Numerics;
using ImGuiNET;

namespace RecompOne.Runtime.Host.Window;

internal static class ImGuiUtils
{
    public static readonly Vector4 Green = new(0.40f, 0.85f, 0.45f, 1f);
    public static readonly Vector4 Amber = new(0.95f, 0.70f, 0.30f, 1f);
    public static readonly Vector4 Red = new(0.95f, 0.38f, 0.38f, 1f);
    public static readonly Vector4 Gray = new(0.55f, 0.55f, 0.55f, 1f);

    private static readonly Dictionary<uint, float> _anim = new();

    public static string Title(string titleKey, string id)
    {
        return $"{Localization.T(titleKey)}###{id}";
    }

    public ref struct Scope(Action? close)
    {
        public void Dispose()
        {
            close?.Invoke();
            close = null;
        }
    }

    public static Scope Disabled(bool when = true)
    {
        if (!when) return new Scope(null);
        ImGui.BeginDisabled();
        return new Scope(ImGui.EndDisabled);
    }

    public static Scope Id(string id)
    {
        ImGui.PushID(id);
        return new Scope(ImGui.PopID);
    }

    public static Scope Color(ImGuiCol target, Vector4 color)
    {
        ImGui.PushStyleColor(target, color);
        return new Scope(() => ImGui.PopStyleColor());
    }

    public static Scope Style(ImGuiStyleVar target, float value)
    {
        ImGui.PushStyleVar(target, value);
        return new Scope(() => ImGui.PopStyleVar());
    }

    public static Scope Style(ImGuiStyleVar target, Vector2 value)
    {
        ImGui.PushStyleVar(target, value);
        return new Scope(() => ImGui.PopStyleVar());
    }

    public static Scope Wrap(float widthInFonts = 24f)
    {
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * widthInFonts);
        return new Scope(ImGui.PopTextWrapPos);
    }

    public static void Width(float width)
    {
        ImGui.SetNextItemWidth(width * Theme.Scale);
    }

    //this stupid piece of shit crashing on my face now shutup and work
    public static void TextDisabled(string s)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextUnformatted(s);
        ImGui.PopStyleColor();
    }

    public static void TextColored(Vector4 col, string s)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, col);
        ImGui.TextUnformatted(s);
        ImGui.PopStyleColor();
    }

    public static void TextWrappedColored(Vector4 col, string s)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, col);
        ImGui.TextWrapped(s);
        ImGui.PopStyleColor();
    }

    public static void Hint(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    public static void Section(string title)
    {
        ImGui.Spacing();
        TextDisabled(title);
        ImGui.Separator();
    }

    public static void Tag(string text, Vector4 color, bool rounded = true)
    {
        var dl = ImGui.GetWindowDrawList();
        var pad = new Vector2(6f, 1f) * Theme.Scale;
        var pos = ImGui.GetCursorScreenPos();
        var size = ImGui.CalcTextSize(text) + pad * 2f;
        dl.AddRectFilled(pos, pos + size, ImGui.ColorConvertFloat4ToU32(color with { W = 0.22f }), rounded ? size.Y * 0.5f : 0f);
        dl.AddText(pos + pad, ImGui.ColorConvertFloat4ToU32(color), text);
        ImGui.Dummy(size);
    }

    public static void Tags(params (string Text, Vector4 Color)[] tags)
    {
        for (var i = 0; i < tags.Length; i++)
        {
            if (i > 0) ImGui.SameLine();
            Tag(tags[i].Text, tags[i].Color);
        }
    }

    public static void Banner(string text, Vector4 color)
    {
        var dl = ImGui.GetWindowDrawList();
        var pad = ImGui.GetStyle().FramePadding;
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - pad.X * 2f);
        var size = ImGui.CalcTextSize(text, width - pad.X * 2f);
        dl.AddRectFilled(pos, pos + new Vector2(width, size.Y + pad.Y * 2f),
            ImGui.ColorConvertFloat4ToU32(color with { W = 0.18f }), ImGui.GetStyle().FrameRounding);
        ImGui.SetCursorScreenPos(pos + pad);
        ImGui.TextWrapped(text);
        ImGui.PopTextWrapPos();
        ImGui.SetCursorScreenPos(pos + new Vector2(0f, size.Y + pad.Y * 2f + ImGui.GetStyle().ItemSpacing.Y));
        ImGui.Dummy(Vector2.Zero);
    }

    public static void Tooltip(string tip)
    {
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) return;
        ImGui.BeginTooltip();
        using (Wrap()) ImGui.TextUnformatted(tip);
        ImGui.EndTooltip();
    }

    public static void Help(string tip)
    {
        ImGui.SameLine();
        ImGui.TextDisabled(FontSet.Or(FontSet.Info, "(?)"));
        if (!ImGui.IsItemHovered()) return;
        ImGui.BeginTooltip();
        using (Wrap()) ImGui.TextUnformatted(tip);
        ImGui.EndTooltip();
    }

    public static bool Button(string label, bool enabled = true, string? tip = null)
    {
        bool clicked;
        using (Disabled(!enabled)) clicked = ImGui.Button(label);
        if (tip != null) Tooltip(tip);
        return clicked && enabled;
    }

    public static bool Danger(string label, bool enabled = true, bool wide = false, Vector2? size = null)
    {
        bool clicked;
        using (Disabled(!enabled))
        using (Color(ImGuiCol.Text, Red))
            clicked = ImGui.Button(label, wide ? new Vector2(-1f, 0f) : size ?? Vector2.Zero);
        return clicked && enabled;
    }

    public static bool Wide(string label, bool enabled = true)
    {
        bool clicked;
        using (Disabled(!enabled)) clicked = ImGui.Button(label, new Vector2(-1f, 0f));
        return clicked && enabled;
    }

    public static bool Search(string id, ref string text, string hint = "Search", float width = 170f, uint max = 64)
    {
        Width(width);
        return ImGui.InputTextWithHint(id, hint, ref text, max);
    }

    public static bool Combo(string id, ref int index, IReadOnlyList<string> items, float width = 130f)
    {
        var changed = false;
        Width(width);
        if (!ImGui.BeginCombo(id, index >= 0 && index < items.Count ? items[index] : "")) return false;
        for (var i = 0; i < items.Count; i++)
            if (ImGui.Selectable(items[i], i == index) && i != index)
            {
                index = i;
                changed = true;
            }

        ImGui.EndCombo();
        return changed;
    }

    public static bool Filter(string id, ref string? value, IReadOnlyCollection<string> options, string any = "All",
        string? none = null, float width = 130f)
    {
        if (value is { Length: > 0 } && !options.Contains(value)) value = null;
        var current = value == null ? any : value.Length == 0 ? none ?? any : value;
        var picked = value;

        Width(width);
        if (!ImGui.BeginCombo(id, current)) return false;
        if (ImGui.Selectable(any, value == null)) picked = null;
        if (none != null && ImGui.Selectable(none, value == "")) picked = "";
        if (options.Count > 0) ImGui.Separator();
        foreach (var option in options)
            if (ImGui.Selectable(option, value == option))
                picked = option;
        ImGui.EndCombo();

        if (picked == value) return false;
        value = picked;
        return true;
    }

    public static bool Segmented(string id, ref int value, params string[] labels)
    {
        var changed = false;
        using var _ = Id(id);
        var active = ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive];
        for (var i = 0; i < labels.Length; i++)
        {
            if (i > 0) ImGui.SameLine(0f, 0f);
            var selected = value == i;
            if (selected)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, active);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, active);
            }

            if (ImGui.Button(labels[i]) && value != i)
            {
                value = i;
                changed = true;
            }

            if (selected) ImGui.PopStyleColor(2);
        }

        return changed;
    }

    public static bool Toggle(string id, ref bool value) //why doesnt igui have a nice toggle by default? :<
    {
        var scale = Theme.Scale;
        var w = 36f * scale;
        var h = 20f * scale;
        var gid = ImGui.GetID(id);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(w, h));
        if (clicked) value = !value;
        var hovered = ImGui.IsItemHovered();

        _anim.TryGetValue(gid, out var t);
        var target = value ? 1f : 0f;
        t = MathF.Abs(t - target) < 0.01f ? target : t + (target - t) * MathF.Min(1f, ImGui.GetIO().DeltaTime * 16f);
        _anim[gid] = t;

        var style = ImGui.GetStyle();
        var off = style.Colors[(int)(hovered ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg)];
        var on = style.Colors[(int)ImGuiCol.CheckMark];
        if (hovered) on = Vector4.Lerp(on, Vector4.One, 0.2f);

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2(w, h), ImGui.ColorConvertFloat4ToU32(Vector4.Lerp(off, on, t)), h * 0.5f);
        var knob = new Vector2(pos.X + h * 0.5f + t * (w - h), pos.Y + h * 0.5f);
        dl.AddCircleFilled(knob, h * 0.5f - 3f, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.95f)));
        return clicked;
    }

    public static bool Toggle(string label, string id, ref bool value)
    {
        var clicked = Toggle(id, ref value);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        return clicked;
    }

    public static void Checker(ImDrawListPtr dl, Vector2 min, Vector2 max)
    {
        var cell = 8f * Theme.Scale;
        uint a = ImGui.ColorConvertFloat4ToU32(new Vector4(0.30f, 0.30f, 0.30f, 1f));
        uint b = ImGui.ColorConvertFloat4ToU32(new Vector4(0.40f, 0.40f, 0.40f, 1f));
        dl.PushClipRect(min, max, true);
        dl.AddRectFilled(min, max, a);
        var row = 0;
        for (var y = min.Y; y < max.Y; y += cell, row++)
        for (var x = min.X + (row & 1) * cell; x < max.X; x += cell * 2)
            dl.AddRectFilled(new Vector2(x, y), new Vector2(x + cell, y + cell), b);
        dl.PopClipRect();
    }

    public static void ImageBox(uint tex, int w, int h, Vector2 size, string? empty = null)
    {
        var pos = ImGui.GetCursorScreenPos();
        ImGui.Dummy(size);
        var dl = ImGui.GetWindowDrawList();
        Checker(dl, pos, pos + size);
        if (tex != 0 && w > 0 && h > 0)
        {
            var fit = MathF.Min(size.X / w, size.Y / h);
            var drawn = new Vector2(w * fit, h * fit);
            var at = pos + (size - drawn) * 0.5f;
            dl.AddImage((nint)tex, at, at + drawn);
        }
        else if (empty != null)
        {
            var ts = ImGui.CalcTextSize(empty);
            dl.AddText(pos + (size - ts) * 0.5f, ImGui.ColorConvertFloat4ToU32(Gray), empty);
        }

        dl.AddRect(pos, pos + size, ImGui.GetColorU32(ImGuiCol.Border));
    }

    public static void LetterIcon(string seed, string name, float size)
    {
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var hash = 2166136261;
        foreach (var c in seed) hash = (hash ^ c) * 16777619;
        ImGui.ColorConvertHSVtoRGB(hash % 360 / 360f, 0.42f, 0.42f, out var r, out var g, out var b);
        dl.AddRectFilled(pos, pos + new Vector2(size, size), ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, 1f)),
            ImGui.GetStyle().FrameRounding);
        var letter = char.ToUpperInvariant(name.Length > 0 ? name[0] : '?').ToString();
        var fontSize = size * 0.5f;
        var ts = ImGui.GetFont().CalcTextSizeA(fontSize, float.MaxValue, 0f, letter);
        dl.AddText(ImGui.GetFont(), fontSize, pos + (new Vector2(size, size) - ts) * 0.5f,
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.9f)), letter);
        ImGui.Dummy(new Vector2(size, size));
    }
}
