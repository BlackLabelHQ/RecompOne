using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Host;

namespace RecompOne.Runtime.Modding;

internal static class ModLoadingPopup
{
    static volatile bool _active;
    static volatile int _current, _total;
    static volatile string _name = "";

    public static void Begin(int total)
    {
        _active = true;
        _total = total;
        _current = 0;
        _name = "";
    }

    public static void Update(int current, string name)
    {
        _current = current;
        _name = name;
    }

    public static void End() => _active = false;

    public static void Draw()
    {
        if (!_active) return;
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.GetCenter(), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(380 * HostWindow.DpiScale, 0), ImGuiCond.Always);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoSavedSettings;
        if (ImGui.Begin("Loading mods", flags))
        {
            if (_name.Length > 0) ImGui.TextUnformatted(_name);
            float frac = _total > 0 ? _current / (float)_total : 0f;
            ImGui.ProgressBar(frac, new Vector2(-1, 0), $"{_current}/{_total}");
        }
        ImGui.End();
    }
}
