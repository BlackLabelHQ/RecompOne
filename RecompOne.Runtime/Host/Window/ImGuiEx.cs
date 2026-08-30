using System.Numerics;
using ImGuiNET;

namespace RecompOne.Runtime.Host.Window;

//this stupid piece of shit crashing on my face now shutup and work
internal static class ImGuiEx
{
    public static void TextDisabled(string s)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextUnformatted(s);
        ImGui.PopStyleColor();
    }

    public static void TextColored(Vector4 col, string s)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, col);
        ImGui.TextUnformatted(s);
        ImGui.PopStyleColor();
    }

    public static void TextWrappedColored(Vector4 col, string s)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, col);
        ImGui.TextWrapped(s);
        ImGui.PopStyleColor();
    }

    public static string Title(string titleKey, string id)
    {
        return $"{Localization.T(titleKey)}###{id}";
    }
}