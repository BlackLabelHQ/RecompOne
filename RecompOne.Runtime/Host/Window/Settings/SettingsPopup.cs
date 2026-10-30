using System.Numerics;
using ImGuiNET;

namespace RecompOne.Runtime.Host.Window;

public sealed class SettingsPopup : Popup
{
    private const float SidebarWidth = 180f;

    private static string _selectedId = "";

    protected override string TitleKey => "settings.title";
    protected override Vector2 Size => new(780f, 500f);

    internal static void Select(string? id)
    {
        if (!string.IsNullOrEmpty(id)) _selectedId = id;
    }

    protected override void DrawContent()
    {
        var pages = SettingsPages.All.Where(p => p.IsVisible).ToArray();
        var current = Array.Find(pages, p => p.Id == _selectedId) ?? pages.FirstOrDefault();
        if (current != null) _selectedId = current.Id;

        DrawSidebar(pages, current);
        ImGui.SameLine();
        DrawPage(current);
    }

    private static void DrawSidebar(SettingsPage[] pages, SettingsPage? current)
    {
        ImGui.BeginChild("##settings-sidebar", new Vector2(SidebarWidth, 0f), ImGuiChildFlags.Borders);

        foreach (var page in pages)
            if (ImGui.Selectable($"{Localization.T(page.TitleKey)}##page-{page.Id}", current == page))
                _selectedId = page.Id;

        ImGui.EndChild();
    }

    private static void DrawPage(SettingsPage? current)
    {
        ImGui.BeginChild("##settings-content", Vector2.Zero, ImGuiChildFlags.Borders);

        if (current == null)
        {
            ImGui.TextDisabled(Localization.T("settings.empty"));
        }
        else
        {
            ImGui.SeparatorText(Localization.T(current.TitleKey));
            ImGui.Spacing();
            ImGui.PushID(current.Id);
            current.Draw();
            ImGui.PopID();
        }

        ImGui.EndChild();
    }
}
