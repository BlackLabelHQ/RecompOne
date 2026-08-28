using ImGuiNET;

namespace RecompOne.Runtime.Host.Window;

public static class MenuRegistry
{
    static readonly List<MenuNode> _roots = [];
    static List<MenuNode> _draw = [];
    static bool _dirty = true;

    public static MenuBuilder Menu(string labelKey)
    {
        var node = Find(labelKey);
        if (node == null)
        {
            node = new MenuNode(labelKey);
            _roots.Add(node);
            _dirty = true;
        }

        return new MenuBuilder(node, null);
    }

    public static MenuBuilder BarItem(string labelKey, Action onClick)
    {
        var node = Find(labelKey);
        if (node == null)
        {
            node = new MenuNode(labelKey);
            _roots.Add(node);
            _dirty = true;
        }
        node.OnClick = onClick;
        return new MenuBuilder(node, null);
    }

    public static bool Remove(string labelKey)
    {
        if (_roots.RemoveAll(m => m.LabelKey == labelKey) > 0)
        {
            _dirty = true;
            return true;
        }

        foreach (var root in _roots)
            if (root.RemoveByKey(labelKey)) return true;

        return false;
    }

    internal static void Invalidate() => _dirty = true;

    public static Action? RightAligned;

    internal static void Draw()
    {
        if (!ImGui.BeginMainMenuBar()) return;

        if (_dirty)
        {
            _draw = MenuOrder.Arrange(_roots);
            _dirty = false;
        }
        foreach (var menu in _draw) menu.Draw();

        RightAligned?.Invoke();

        ImGui.EndMainMenuBar();
    }

    static MenuNode? Find(string labelKey)
    {
        foreach (var menu in _roots)
            if (menu.LabelKey == labelKey) return menu;
        return null;
    }
}
