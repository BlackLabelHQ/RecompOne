using System.Text;

namespace RecompOne.UI;

public static class InterfaceLayout
{
    private const string FilePath = "interface.ini";
    private const string Section = "[RecompOne]";
    private const string PanelPrefix = "Panels.";

    private static readonly Dictionary<string, bool> _panels = new(StringComparer.Ordinal);
    private static string? _pendingImGui;
    private static bool _loaded;

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        if (!File.Exists(FilePath)) return;

        var imgui = new StringBuilder();
        var inSection = false;
        foreach (var raw in File.ReadAllLines(FilePath))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith('[')) inSection = line == Section;

            if (!inSection)
            {
                imgui.AppendLine(line);
                continue;
            }

            if (!line.StartsWith(PanelPrefix, StringComparison.Ordinal)) continue;
            var eq = line.IndexOf('=');
            if (eq > PanelPrefix.Length)
                _panels[line[PanelPrefix.Length..eq]] = bool.TryParse(line[(eq + 1)..], out var open) && open;
        }

        _pendingImGui = imgui.ToString();
    }

    public static bool ApplyImGui()
    {
        if (_pendingImGui == null) return false;
        ImGui.LoadIniSettingsFromMemory(_pendingImGui);
        _pendingImGui = null;
        return true;
    }

    public static void ApplyPanels(IReadOnlyList<IPanel> panels)
    {
        foreach (var panel in panels)
            if (_panels.TryGetValue(panel.Name, out var open))
                panel.IsOpen = open;
    }

    public static void Save(IReadOnlyList<IPanel> panels)
    {
        foreach (var panel in panels)
            _panels[panel.Name] = panel.IsOpen;

        var sb = new StringBuilder();
        sb.AppendLine(Section);
        foreach (var (name, open) in _panels)
            sb.AppendLine($"{PanelPrefix}{name}={open}");
        sb.AppendLine();
        sb.Append(ImGui.SaveIniSettingsToMemory());

        try
        {
            File.WriteAllText(FilePath, sb.ToString());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[InterfaceLayout] cannot write {FilePath}: {e.Message}");
        }
    }

    public static void Reset(IReadOnlyList<IPanel> panels)
    {
        _panels.Clear();
        foreach (var panel in panels)
            panel.IsOpen = panel.Name == "Output";
        ImGui.LoadIniSettingsFromMemory("");
        Save(panels);
    }
}
