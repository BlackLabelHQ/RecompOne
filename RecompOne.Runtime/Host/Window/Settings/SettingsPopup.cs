using System.Numerics;
using ImGuiNET;

namespace RecompOne.Runtime.Host.Window;

internal sealed class SettingsPopup : IPanel
{
    public string Name => "Settings";
    public bool IsOpen { get; set; }

    const float SidebarWidth = 170f;
    string _selectedId = "";

    public void Draw()
    {
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowSize(new Vector2(760, 470) * HostWindow.DpiScale, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(vp.GetCenter(), ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));

        bool open = IsOpen;
        if (!ImGui.Begin(Name, ref open,
                ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoSavedSettings))
        {
            IsOpen = open;
            ImGui.End();
            return;
        }

        var sections = SettingsRegistry.Sections;
        var current = ResolveSelection(sections);

        DrawSidebar(sections, current);
        ImGui.SameLine();
        DrawContent(current);

        IsOpen = open;
        ImGui.End();
    }

    ISettingsSection? ResolveSelection(IReadOnlyList<ISettingsSection> sections)
    {
        if (sections.Count == 0) return null;
        foreach (var s in sections)
            if (s.Id == _selectedId) return s;
        _selectedId = sections[0].Id;
        return sections[0];
    }

    void DrawSidebar(IReadOnlyList<ISettingsSection> sections, ISettingsSection? current)
    {
        ImGui.BeginChild("##settings-sidebar", new Vector2(SidebarWidth * HostWindow.DpiScale, 0), ImGuiChildFlags.Border);

        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextUnformatted("SETTINGS");
        ImGui.PopStyleColor();
        ImGui.Separator();
        ImGui.Spacing();

        foreach (var s in sections)
        {
            if (ImGui.Selectable($"{s.Title}##sec-{s.Id}", current == s))
                _selectedId = s.Id;
        }

        ImGui.EndChild();
    }

    void DrawContent(ISettingsSection? current)
    {
        ImGui.BeginChild("##settings-content", Vector2.Zero, ImGuiChildFlags.Border);

        if (current == null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            ImGui.TextUnformatted("No settings available.");
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.TextUnformatted(current.Title);
            ImGui.Separator();
            ImGui.Spacing();
            current.Draw();
            foreach (var ext in SettingsRegistry.GetExtensions(current.Id))
                ext();
        }

        ImGui.EndChild();
    }
}
