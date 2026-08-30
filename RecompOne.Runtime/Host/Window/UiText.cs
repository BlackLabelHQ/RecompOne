using System.Text;
using ImGuiNET;

namespace RecompOne.Runtime.Host.Window;

internal static class UiText
{
    public static void CenteredWrapped(string text)
    {
        var avail = ImGui.GetContentRegionAvail().X;
        foreach (var para in text.Split('\n'))
        {
            if (para.Length == 0)
            {
                ImGui.Spacing();
                continue;
            }

            var line = new StringBuilder();
            foreach (var word in para.Split(' '))
            {
                var test = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && ImGui.CalcTextSize(test).X > avail)
                {
                    DrawLine(line.ToString(), avail);
                    line.Clear();
                    line.Append(word);
                }
                else
                {
                    if (line.Length > 0) line.Append(' ');
                    line.Append(word);
                }
            }

            if (line.Length > 0) DrawLine(line.ToString(), avail);
        }
    }

    private static void DrawLine(string line, float avail)
    {
        var w = ImGui.CalcTextSize(line).X;
        var off = (avail - w) * 0.5f;
        if (off > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + off);
        ImGui.TextUnformatted(line);
    }
}