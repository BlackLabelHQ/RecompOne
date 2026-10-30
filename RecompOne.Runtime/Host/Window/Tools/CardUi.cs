using System.Numerics;
using ImGuiNET;

namespace RecompOne.Runtime.Host.Window;

//generic card UI for some windows
internal sealed class Card
{
    public string Title = "";
    public string Meta = "";
    public string? Description;
    public Vector4 Status;
    public string? StatusTip;
    public bool Enabled;
    public bool CanToggle = true;
    public string? ToggleTip;
    public bool Draggable;
    public Action<float>? Icon;
    public (string Glyph, string Tip, bool Active)[] Actions = [];
    public Action? Footer;
}

internal readonly record struct CardResult(bool Toggled, int Action, bool DragStarted, bool Dropped);

internal static class CardUi
{
    public const string DragType = "RECOMP_CARD";

    private static readonly Dictionary<uint, bool> _hovered = new();

    public static float Scale => Theme.Scale;

    public static void Header(string text, params (string Glyph, string Tip, Action Click)[] buttons)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled(text);

        var size = IconSize;
        var gap = 4f * Scale;
        var total = buttons.Length * size + Math.Max(0, buttons.Length - 1) * gap;
        ImGui.SameLine(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - total);
        for (var i = 0; i < buttons.Length; i++)
        {
            if (i > 0) ImGui.SameLine(0f, gap);
            if (IconButton($"##hdr{i}", buttons[i].Glyph, buttons[i].Tip)) buttons[i].Click();
        }

        ImGui.Separator();
        ImGui.Spacing();
    }

    public static float IconSize => 24f * Scale;

    public static bool IconButton(string id, string glyph, string tip, bool active = false)
    {
        var size = IconSize;
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
        var hovered = ImGui.IsItemHovered();

        var style = ImGui.GetStyle();
        var dl = ImGui.GetWindowDrawList();
        if (hovered)
            dl.AddRectFilled(pos, pos + new Vector2(size, size), ImGui.GetColorU32(ImGuiCol.ButtonHovered),
                style.FrameRounding);

        var color = active ? style.Colors[(int)ImGuiCol.CheckMark]
            : style.Colors[(int)(hovered ? ImGuiCol.Text : ImGuiCol.TextDisabled)];
        var text = ImGui.CalcTextSize(glyph);
        dl.AddText(pos + (new Vector2(size, size) - text) * 0.5f, ImGui.ColorConvertFloat4ToU32(color), glyph);

        if (hovered && tip.Length > 0) ImGui.SetTooltip(tip);
        return clicked;
    }

    public static void Empty(string title, string hint)
    {
        ImGui.Spacing();
        ImGui.Spacing();
        Centered(title, false);
        Centered(hint, true);
    }

    private static void Centered(string text, bool disabled)
    {
        var off = (ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(text).X) * 0.5f;
        if (off > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + off);
        if (disabled) ImGui.TextDisabled(text);
        else ImGui.TextUnformatted(text);
    }

    public static CardResult Draw(string id, Card card)
    {
        var scale = Scale;
        var style = ImGui.GetStyle();
        var gid = ImGui.GetID(id);
        _hovered.TryGetValue(gid, out var wasHovered);

        var baseColor = style.Colors[(int)ImGuiCol.FrameBg] with { W = 0.35f };
        var hoverColor = style.Colors[(int)ImGuiCol.FrameBgHovered] with { W = 0.55f };

        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, style.FrameRounding + 2f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12f, 10f) * scale);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, wasHovered ? hoverColor : baseColor);
        ImGui.BeginChild(id, Vector2.Zero, ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY,
            ImGuiWindowFlags.NoScrollbar);

        var toggled = false;
        var action = -1;
        var dragStarted = false;
        var dropped = false;

        var iconSize = 52f * scale;
        var avail = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorPos();
        var right = origin.X + avail;
        var x = origin.X;

        if (card.Draggable)
        {
            ImGui.SetCursorPos(new Vector2(x, origin.Y + (iconSize - IconSize) * 0.5f));
            IconButton("##grip", FontSet.Or(FontSet.Grip, "::"), "Drag to reorder");
            if (ImGui.BeginDragDropSource())
            {
                ImGui.SetDragDropPayload(DragType, IntPtr.Zero, 0);
                ImGui.TextUnformatted(card.Title);
                ImGui.EndDragDropSource();
                dragStarted = true;
            }

            x += IconSize + 6f * scale;
        }

        var toggleW = 36f * scale;
        var controls = (card.CanToggle ? toggleW : 0f) + card.Actions.Length * (IconSize + 4f * scale);
        var dim = !card.Enabled;
        if (dim) ImGui.PushStyleVar(ImGuiStyleVar.Alpha, style.Alpha * 0.55f);

        ImGui.SetCursorPos(new Vector2(x, origin.Y));
        if (card.Icon != null) card.Icon(iconSize);
        else ImGui.Dummy(new Vector2(iconSize, iconSize));

        var textX = x + iconSize + 12f * scale;
        ImGui.SetCursorPos(new Vector2(textX, origin.Y + 2f * scale));
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(right - controls - 8f * scale);
        Dot(card.Status, card.StatusTip);
        ImGui.SameLine(0f, 6f * scale);
        ImGui.TextUnformatted(card.Title);
        if (card.Meta.Length > 0) ImGui.TextDisabled(card.Meta);
        if (!string.IsNullOrWhiteSpace(card.Description))
        {
            ImGuiUtils.Hint(card.Description);
        }

        ImGui.PopTextWrapPos();
        ImGui.EndGroup();
        if (dim) ImGui.PopStyleVar();

        var bottom = MathF.Max(ImGui.GetCursorPosY(), origin.Y + iconSize + style.ItemSpacing.Y);

        //top right controls
        var cx = right;
        if (card.CanToggle)
        {
            cx -= toggleW;
            ImGui.SetCursorPos(new Vector2(cx, origin.Y + 2f * scale));
            var enabled = card.Enabled;
            if (ImGuiUtils.Toggle("##toggle", ref enabled)) toggled = true;
            if (card.ToggleTip != null) ImGuiUtils.Tooltip(card.ToggleTip);
        }

        for (var i = card.Actions.Length - 1; i >= 0; i--)
        {
            cx -= IconSize + 4f * scale;
            ImGui.SetCursorPos(new Vector2(cx, origin.Y));
            var (glyph, tip, active) = card.Actions[i];
            if (IconButton($"##act{i}", glyph, tip, active)) action = i;
        }

        ImGui.SetCursorPos(new Vector2(origin.X, bottom));
        ImGui.Dummy(Vector2.Zero);

        if (card.Footer != null)
        {
            ImGui.Separator();
            ImGui.Spacing();
            card.Footer();
        }

        _hovered[gid] = ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(2);

        if (card.Draggable && ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                if (ImGui.AcceptDragDropPayload(DragType).NativePtr != null) dropped = true;
            }

            ImGui.EndDragDropTarget();
        }

        ImGui.Spacing();
        return new CardResult(toggled, action, dragStarted, dropped);
    }

    private static void Dot(Vector4 color, string? tip)
    {
        var h = ImGui.GetTextLineHeight();
        var pos = ImGui.GetCursorScreenPos();
        var r = 3.5f * Scale;
        ImGui.GetWindowDrawList().AddCircleFilled(new Vector2(pos.X + r, pos.Y + h * 0.5f), r,
            ImGui.ColorConvertFloat4ToU32(color));
        ImGui.Dummy(new Vector2(r * 2f, h));
        if (tip != null && ImGui.IsItemHovered()) ImGui.SetTooltip(tip);
    }
}
