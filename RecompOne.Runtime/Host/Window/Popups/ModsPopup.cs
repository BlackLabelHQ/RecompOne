using System.Diagnostics;
using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Modding;

namespace RecompOne.Runtime.Host.Window;

public sealed class ModsPopup : Popup
{
    const float IconSize = 56f;
    const float ToggleW = 38f;
    const float ToggleH = 21f;
    const float SmallBtn = 21f;
    const float Gap = 8f;

    static readonly Dictionary<string, uint> _icons = new();
    static readonly HashSet<string> _iconTried = new();
    static readonly Dictionary<uint, float> _anim = new();

    string? _settingsFor;

    protected override string TitleKey => "mods.title";
    protected override Vector2 Size => new(580f, 540f);

    protected override void DrawContent()
    {
        ModLoader.PollChanges();

        var mods = ModLoader.Mods;
        int active = 0;
        foreach (var mod in mods) if (mod.Loaded) active++;

        ImGui.TextDisabled(mods.Count == 1 ? Localization.T("mods.count_one") : Localization.T("mods.count", mods.Count));
        if (active > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"-  {Localization.T("mods.active", active)}");
        }

        var folder = FontSet.Or(FontSet.FolderOpen, "...");
        float folderWidth = ImGui.CalcTextSize(folder).X + ImGui.GetStyle().FramePadding.X * 2f;
        ImGui.SameLine(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - folderWidth);
        if (ImGui.SmallButton(folder)) OpenFolder();

        ImGui.Separator();
        ImGui.Spacing();

        if (mods.Count == 0)
        {
            ImGui.Spacing();
            CenteredDisabled(Localization.T("mods.empty"));
            CenteredDisabled(Localization.T("mods.empty_hint"));
            return;
        }

        foreach (var mod in mods) DrawMod(mod);
    }

    void DrawMod(ModEntry mod)
    {
        ImGui.PushID(mod.Info.Id);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, ImGui.GetStyle().FrameRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12f, 12f));
        ImGui.BeginChild("card", new Vector2(0, 0), ImGuiChildFlags.Border | ImGuiChildFlags.AutoResizeY);

        float avail = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorPos();
        float rightX = origin.X + avail;
        float textX = origin.X + IconSize + 12f;

        bool showGear = mod.Loaded && mod.HasSettings;
        float controlsW = ToggleW + Gap + SmallBtn + (showGear ? Gap + SmallBtn : 0f);

        bool dim = !mod.Enabled;
        if (dim) ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f);

        DrawIcon(mod, IconSize);

        ImGui.SetCursorPos(new Vector2(textX, origin.Y));
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(rightX - controlsW - Gap);

        StatusDot(mod);
        ImGui.SameLine(0f, 6f);
        ImGui.TextUnformatted(string.IsNullOrWhiteSpace(mod.Info.Name) ? mod.Info.Id : mod.Info.Name);

        ImGui.SetCursorPosX(textX);
        var meta = string.IsNullOrEmpty(mod.Info.Author) ? $"v{mod.Info.Version}" : $"v{mod.Info.Version}  -  {mod.Info.Author}";
        ImGui.TextDisabled(meta);

        ImGui.PopTextWrapPos();
        ImGui.EndGroup();

        if (!string.IsNullOrWhiteSpace(mod.Info.Description))
        {
            ImGui.SetCursorPosX(textX);
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            ImGui.PushTextWrapPos(rightX);
            ImGui.TextWrapped(mod.Info.Description);
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
        }

        if (ImGui.GetCursorPosY() < origin.Y + IconSize)
            ImGui.SetCursorPosY(origin.Y + IconSize);

        if (dim) ImGui.PopStyleVar();

        var bottom = ImGui.GetCursorPos();

        //top right controls
        float x = rightX - ToggleW;
        ImGui.SetCursorPos(new Vector2(x, origin.Y));
        bool enabled = mod.Enabled;
        if (Toggle("##enable", ref enabled))
        {
            ModLoader.SetEnabled(mod.Info.Id, enabled);
            if (!enabled && _settingsFor == mod.Info.Id) _settingsFor = null;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Localization.T(enabled ? "mods.disable" : "mods.enable"));

        if (showGear)
        {
            x -= Gap + SmallBtn;
            ImGui.SetCursorPos(new Vector2(x, origin.Y));
            bool selected = _settingsFor == mod.Info.Id;
            if (GearButton("##gear", selected))
                _settingsFor = selected ? null : mod.Info.Id;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Localization.T("mods.settings"));
        }

        x -= Gap + SmallBtn;
        ImGui.SetCursorPos(new Vector2(x, origin.Y));
        if (DotsButton("##opts"))
            ImGui.OpenPopup("opts");

        if (ImGui.BeginPopup("opts"))
        {
            if (!mod.Enabled) ImGui.BeginDisabled();
            if (ImGui.MenuItem(Localization.T("mods.reload"))) ModLoader.Reload(mod.Info.Id);
            if (!mod.Enabled) ImGui.EndDisabled();
            ImGui.Separator();
            ImGui.TextDisabled(mod.Info.Id);
            ImGui.TextDisabled(Localization.T("mods.hooks", mod.HookCount));
            ImGui.EndPopup();
        }

        ImGui.SetCursorPos(bottom);

        if (_settingsFor == mod.Info.Id && mod.Loaded && mod.HasSettings)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            mod.DrawSettings();
            ImGui.Spacing();
        }

        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.Spacing();
        ImGui.PopID();
    }

    // enabled = green, failed = red, disabled = gray
    static void StatusDot(ModEntry mod)
    {
        var col = !mod.Enabled ? new Vector4(0.55f, 0.55f, 0.55f, 1f)
            : mod.Loaded ? new Vector4(0.35f, 0.80f, 0.42f, 1f)
            : new Vector4(0.95f, 0.35f, 0.35f, 1f);

        float h = ImGui.GetTextLineHeight();
        var pos = ImGui.GetCursorScreenPos();
        float cy = pos.Y + h * 0.55f;
        ImGui.GetWindowDrawList().AddRectFilled(new Vector2(pos.X + 1f, cy - 3f), new Vector2(pos.X + 7f, cy + 3f), ImGui.ColorConvertFloat4ToU32(col));
        ImGui.Dummy(new Vector2(8f, h));
        if (mod.Enabled && !mod.Loaded && ImGui.IsItemHovered())
            ImGui.SetTooltip(mod.LoadError ?? Localization.T("mods.load_failed"));
    }

    void DrawIcon(ModEntry mod, float size) //draw generic icon when no icon is proveded, its like the youtube one, first initial and colorful background based on modid hash
    {
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        uint tex = Icon(mod);

        if (tex != 0)
        {
            dl.AddImage((nint)tex, pos, pos + new Vector2(size, size));
        }
        else
        {
            uint hash = 2166136261;
            foreach (char c in mod.Info.Id) hash = (hash ^ c) * 16777619;
            ImGui.ColorConvertHSVtoRGB(hash % 360 / 360f, 0.42f, 0.42f, out float r, out float g, out float b);
            dl.AddRectFilled(pos, pos + new Vector2(size, size), ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, 1f)));

            string letter = char.ToUpperInvariant(string.IsNullOrWhiteSpace(mod.Info.Name) ? mod.Info.Id[0] : mod.Info.Name[0]).ToString();
            float fontSize = size * 0.5f;
            var ts = ImGui.GetFont().CalcTextSizeA(fontSize, float.MaxValue, 0f, letter);
            dl.AddText(ImGui.GetFont(), fontSize,
                new Vector2(pos.X + (size - ts.X) * 0.5f, pos.Y + (size - ts.Y) * 0.5f),
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.9f)), letter);
        }

        ImGui.Dummy(new Vector2(size, size));
    }

    static bool Toggle(string strId, ref bool value) //why doesnt igui have a nice toggle by default? :<
    {
        uint id = ImGui.GetID(strId);
        var pos = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.InvisibleButton(strId, new Vector2(ToggleW, ToggleH));
        if (clicked) value = !value;
        bool hovered = ImGui.IsItemHovered();

        _anim.TryGetValue(id, out float t);
        float target = value ? 1f : 0f;
        t = MathF.Abs(t - target) < 0.01f ? target : t + (target - t) * MathF.Min(1f, ImGui.GetIO().DeltaTime * 16f);
        _anim[id] = t;

        var style = ImGui.GetStyle();
        var off = style.Colors[(int)(hovered ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg)];
        var on = style.Colors[(int)ImGuiCol.CheckMark];
        if (hovered) on = Vector4.Lerp(on, Vector4.One, 0.2f);

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2(ToggleW, ToggleH), ImGui.ColorConvertFloat4ToU32(Vector4.Lerp(off, on, t)), ToggleH * 0.5f);

        var knob = new Vector2(pos.X + ToggleH * 0.5f + t * (ToggleW - ToggleH), pos.Y + ToggleH * 0.5f);
        dl.AddCircleFilled(knob, ToggleH * 0.5f - 3f, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.95f)));
        return clicked;
    }

    static bool GearButton(string strId, bool active)
    {
        var pos = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.InvisibleButton(strId, new Vector2(SmallBtn, SmallBtn));
        bool hovered = ImGui.IsItemHovered();

        var style = ImGui.GetStyle();
        var col = active ? style.Colors[(int)ImGuiCol.CheckMark]
            : style.Colors[(int)(hovered ? ImGuiCol.Text : ImGuiCol.TextDisabled)];

        Glyph(pos, FontSet.Gear, ImGui.ColorConvertFloat4ToU32(col));
        return clicked;
    }

    static void Glyph(Vector2 pos, string icon, uint color)
    {
        var size = ImGui.CalcTextSize(icon);
        ImGui.GetWindowDrawList().AddText(
            new Vector2(pos.X + (SmallBtn - size.X) * 0.5f, pos.Y + (SmallBtn - size.Y) * 0.5f), color, icon);
    }

    static bool DotsButton(string strId)
    {
        var pos = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.InvisibleButton(strId, new Vector2(SmallBtn, SmallBtn));
        bool hovered = ImGui.IsItemHovered();

        var style = ImGui.GetStyle();
        Glyph(pos, FontSet.Ellipsis, ImGui.ColorConvertFloat4ToU32(style.Colors[(int)(hovered ? ImGuiCol.Text : ImGuiCol.TextDisabled)]));
        return clicked;
    }

    static void CenteredDisabled(string text)
    {
        float off = (ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(text).X) * 0.5f;
        if (off > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + off);
        ImGui.TextDisabled(text);
    }

    static void OpenFolder()
    {
        try
        {
            var root = ModLoader.Root;
            if (string.IsNullOrEmpty(root)) root = Path.GetFullPath("mods");
            Process.Start(new ProcessStartInfo(root) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[Mods] failed to open mods folder: {e.Message}");
        }
    }

    static uint Icon(ModEntry mod)
    {
        if (mod.IconData == null) return 0;
        if (_icons.TryGetValue(mod.Info.Id, out var t)) return t;
        if (!_iconTried.Add(mod.Info.Id)) return 0;
        var tex = HostWindow.UploadPng(mod.IconData);
        if (tex != 0) _icons[mod.Info.Id] = tex;
        return tex;
    }
}
