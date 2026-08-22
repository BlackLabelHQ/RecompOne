namespace RecompOne.Runtime.Host.Window;

public sealed class MenuBuilder
{
    readonly MenuNode _node;
    readonly MenuBuilder? _parent;

    MenuEntry? _last;
    MenuItemEntry? _lastItem;
    MenuNode? _lastNode;

    internal MenuBuilder(MenuNode node, MenuBuilder? parent)
    {
        _node = node;
        _parent = parent;
        if (parent != null)
        {
            _last = node;
            _lastNode = node;
        }
    }

    public MenuBuilder Item(string labelKey, Action onClick, string? shortcut = null)
    {
        _lastItem = new MenuItemEntry(labelKey, onClick) { Shortcut = shortcut };
        _lastNode = null;
        _last = _lastItem;
        _node.Add(_lastItem);
        return this;
    }

    public MenuBuilder Check(string labelKey, Func<bool> get, Action<bool> set, string? shortcut = null)
    {
        Item(labelKey, () => set(!get()), shortcut);
        _lastItem!.Selected = get;
        return this;
    }

    public MenuBuilder Panel<T>(string labelKey) where T : class, IPanel
    {
        return Check(labelKey,
            () => PanelManager.Get<T>()?.IsOpen == true,
            open => { if (PanelManager.Get<T>() is { } panel) panel.IsOpen = open; });
    }

    public MenuBuilder Popup<T>(string labelKey) where T : Popup
    {
        return Item(labelKey, PopupManager.Open<T>);
    }

    public MenuBuilder Label(string labelKey) => Text(() => Localization.T(labelKey));

    public MenuBuilder Text(Func<string> text)
    {
        Track(new MenuTextEntry(text));
        return this;
    }

    public MenuBuilder Separator(string? key = null)
    {
        Track(new MenuSeparatorEntry { Key = key });
        return this;
    }

    public MenuBuilder Custom(Action draw)
    {
        Track(new MenuCustomEntry(draw));
        return this;
    }

    public MenuBuilder Submenu(string labelKey) => new(_node.Submenu(labelKey), this);

    //the idea is, you can render this before or after a menu label, its better than weird indexing by id
    public MenuBuilder After(string labelKey) => Anchor(labelKey, false);

    public MenuBuilder Before(string labelKey) => Anchor(labelKey, true);

    MenuBuilder Anchor(string labelKey, bool before) //places the ancho on it
    {
        var target = _last ?? _node;
        target.AnchorKey = labelKey;
        target.AnchorBefore = before;

        if (target != _node) _node.Invalidate();
        else if (_parent != null) _parent.Invalidate();
        else MenuRegistry.Invalidate();

        return this;
    }

    public MenuBuilder End() => _parent ?? this;

    public MenuBuilder Enabled(Func<bool> predicate)
    {
        if (_lastItem != null) _lastItem.Enabled = predicate;
        else if (_lastNode != null) _lastNode.Enabled = predicate;
        return this;
    }

    public MenuBuilder Disabled() => Enabled(static () => false);

    public MenuBuilder Tooltip(string tooltipKey)
    {
        if (_lastItem != null) _lastItem.TooltipKey = tooltipKey;
        else if (_lastNode != null) _lastNode.TooltipKey = tooltipKey;
        return this;
    }

    internal void Invalidate() => _node.Invalidate();

    void Track(MenuEntry entry)
    {
        _lastItem = null;
        _lastNode = null;
        _last = entry;
        _node.Add(entry);
    }
}
