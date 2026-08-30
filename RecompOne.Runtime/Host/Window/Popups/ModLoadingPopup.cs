using System.Numerics;
using ImGuiNET;

namespace RecompOne.Runtime.Host.Window;

public sealed class ModLoadingPopup : Popup
{
    private static volatile bool _active;
    private static volatile int _current;
    private static volatile int _total;
    private static volatile string _name = "";

    protected override string TitleKey => "mods.loading";
    protected override Vector2 Size => new(400f, 0f);
    protected override bool Closable => false;

    public static void Begin(int total)
    {
        _total = total;
        _current = 0;
        _name = "";
        _active = true;
    }

    public static void Update(int current, string name)
    {
        _current = current;
        _name = name;
    }

    public static void End()
    {
        _active = false;
    }

    protected internal override void Update()
    {
        if (_active && !IsOpen) Open();
        else if (!_active && IsOpen) Close();
    }

    protected override void DrawContent()
    {
        var name = _name;
        if (name.Length > 0) ImGui.TextUnformatted(name);
        else ImGui.TextDisabled("...");

        var total = _total;
        var current = _current;
        ImGui.ProgressBar(total > 0 ? current / (float)total : 0f, new Vector2(-1f, 0f), $"{current}/{total}");
    }
}