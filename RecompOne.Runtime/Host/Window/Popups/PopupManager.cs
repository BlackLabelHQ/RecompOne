namespace RecompOne.Runtime.Host.Window;

public static class PopupManager
{
    private static readonly List<Popup> _registered = [];
    private static readonly List<Popup> _stack = [];

    public static void Register(Popup popup)
    {
        if (!_registered.Contains(popup)) _registered.Add(popup);
    }

    public static T? Get<T>() where T : Popup
    {
        foreach (var popup in _registered)
            if (popup is T match)
                return match;
        return null;
    }

    public static void Open<T>() where T : Popup
    {
        Get<T>()?.Open();
    }

    public static void Close<T>() where T : Popup
    {
        Get<T>()?.Close();
    }

    public static bool AnyOpen => _stack.Count > 0;

    internal static int TopIndex => _stack.Count - 1;

    internal static void Push(Popup popup)
    {
        if (!_stack.Contains(popup)) _stack.Add(popup);
    }

    internal static void CloseAbove(Popup popup)
    {
        var index = _stack.IndexOf(popup);
        if (index < 0) return;

        for (var i = _stack.Count - 1; i > index; i--)
            _stack[i].Close();
    }

    internal static void Draw()
    {
        foreach (var popup in _registered) popup.Update();

        DrawNested(0);

        for (var i = _stack.Count - 1; i >= 0; i--)
            if (_stack[i].Finished)
                _stack.RemoveAt(i);
    }

    internal static void DrawNested(int index)
    {
        if (index < 0 || index >= _stack.Count) return;
        _stack[index].Render(index);
    }

    internal static void Shutdown()
    {
        _stack.Clear();
        _registered.Clear();
    }
}