namespace RecompOne.Runtime.Host.Window;

public static class PanelManager
{
    static readonly List<IPanel> _panels = [];

    public static void Register(IPanel panel) => _panels.Add(panel);
    public static bool Unregister(IPanel panel) => _panels.Remove(panel);
    public static IReadOnlyList<IPanel> Panels => _panels;

    public static T? Get<T>() where T : class, IPanel
    {
        foreach (var p in _panels)
            if (p is T t) return t;
        return null;
    }

    public static void DrawPanels()
    {
        foreach (var panel in _panels)
            if (panel.IsOpen) panel.Draw();
    }

    public static void Shutdown() => _panels.Clear();
}
