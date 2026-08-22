using ImGuiNET;

namespace RecompOne.Runtime.Host.Window;

abstract class MenuEntry
{
    public string? Key;
    public string? AnchorKey;
    public bool AnchorBefore;

    public abstract void Draw();
}

sealed class MenuItemEntry : MenuEntry
{
    readonly string _labelKey;
    readonly Action _onClick;

    public string? Shortcut;
    public Func<bool>? Selected;
    public Func<bool>? Enabled;
    public string? TooltipKey;

    public MenuItemEntry(string labelKey, Action onClick)
    {
        _labelKey = labelKey;
        _onClick = onClick;
        Key = labelKey;
    }

    public override void Draw()
    {
        bool selected = Selected?.Invoke() ?? false;
        bool enabled = Enabled?.Invoke() ?? true;

        if (ImGui.MenuItem(Localization.T(_labelKey), Shortcut, selected, enabled)) _onClick();
        MenuTooltip.Draw(TooltipKey);
    }
}

sealed class MenuTextEntry(Func<string> text) : MenuEntry
{
    public override void Draw() => ImGui.TextDisabled(text());
}

sealed class MenuSeparatorEntry : MenuEntry
{
    public override void Draw() => ImGui.Separator();
}

sealed class MenuCustomEntry(Action draw) : MenuEntry
{
    public override void Draw() => draw();
}

sealed class MenuNode : MenuEntry
{
    public readonly string LabelKey;

    readonly List<MenuEntry> _entries = [];
    List<MenuEntry> _draw = [];
    bool _dirty = true;

    public Action? OnClick;
    public Func<bool>? Enabled;
    public string? TooltipKey;

    public MenuNode(string labelKey)
    {
        LabelKey = labelKey;
        Key = labelKey;
    }

    public void Add(MenuEntry entry)
    {
        _entries.Add(entry);
        _dirty = true;
    }

    public void Invalidate() => _dirty = true;

    public bool RemoveByKey(string key)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Key == key)
            {
                _entries.RemoveAt(i);
                _dirty = true;
                return true;
            }
            if (_entries[i] is MenuNode child && child.RemoveByKey(key)) return true;
        }
        return false;
    }

    public MenuNode Submenu(string key)
    {
        foreach (var entry in _entries)
            if (entry is MenuNode node && node.LabelKey == key) return node;

        var created = new MenuNode(key);
        Add(created);
        return created;
    }

    public override void Draw()
    {
        var label = Localization.T(LabelKey);
        bool enabled = Enabled?.Invoke() ?? true;

        if (_entries.Count == 0)
        {
            if (ImGui.MenuItem(label, null, false, enabled)) OnClick?.Invoke();
            MenuTooltip.Draw(TooltipKey);
            return;
        }

        if (!ImGui.BeginMenu(label, enabled))
        {
            MenuTooltip.Draw(TooltipKey);
            return;
        }

        MenuTooltip.Draw(TooltipKey);
        if (_dirty)
        {
            _draw = MenuOrder.Arrange(_entries);
            _dirty = false;
        }
        foreach (var entry in _draw) entry.Draw();
        ImGui.EndMenu();
    }
}

static class MenuTooltip
{
    public static void Draw(string? tooltipKey)
    {
        if (tooltipKey == null || !ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) return;
        ImGui.SetTooltip(Localization.T(tooltipKey));
    }
}
