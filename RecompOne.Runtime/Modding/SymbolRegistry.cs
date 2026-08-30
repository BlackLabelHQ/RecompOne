using System.Reflection;
using RecompOne.Runtime.Dispatch;

namespace RecompOne.Runtime.Modding;

public static class SymbolRegistry
{
    private static readonly Dictionary<(string Overlay, string Name), MethodInfo> _byName = [];
    private static readonly Dictionary<(string Overlay, uint Addr), MethodInfo> _byAddress = [];
    private static bool _built;

    public static void Build()
    {
        _byName.Clear();
        _byAddress.Clear();
        foreach (var (name, overlay) in Dispatcher.Overlays)
        {
            var key = name.ToLowerInvariant();
            foreach (var (addr, fn) in overlay.Functions)
            {
                _byAddress[(key, addr)] = fn.Method;
                _byName[(key, fn.Method.Name)] = fn.Method;
            }
        }

        _built = true;
    }

    public static MethodInfo? Resolve(string overlay, string? function, uint address)
    {
        if (!_built) Build();
        var key = overlay.ToLowerInvariant();
        if (!string.IsNullOrEmpty(function))
            return _byName.TryGetValue((key, function), out var byName) ? byName : null;
        return _byAddress.TryGetValue((key, address), out var byAddr) ? byAddr : null;
    }
}