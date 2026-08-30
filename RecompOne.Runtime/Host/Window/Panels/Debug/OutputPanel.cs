using System.Numerics;
using ImGuiNET;

namespace RecompOne.Runtime.Host.Window;

internal sealed class OutputPanel : IPanel
{
    public string Name => "Output";
    public string TitleKey => "panel.output";

    public bool IsOpen
    {
        get => true;
        set { }
    }

    private static uint _texId;
    private static int _texW, _texH;
    private static float _aspect = 4f / 3f;

    public static bool IsDocked { get; private set; }

    public static void SetTexture(uint id, int w, int h, float aspect = 0f)
    {
        (_texId, _texW, _texH, _aspect) = (id, w, h, aspect > 0f ? aspect : 4f / 3f);
    }

    //idea: in the future make this be able to draw images so you can have ornamented backgrounds
    public void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2(640, 480), ImGuiCond.FirstUseEver);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0f, 0f, 0f, 1f));

        var visible = ImGui.Begin(this.Title());
        IsDocked = ImGui.IsWindowDocked();

        if (!visible)
        {
            ImGui.End();
            ImGui.PopStyleColor();
            return;
        }

        if (_texId != 0 && _texW > 0 && _texH > 0)
        {
            var avail = ImGui.GetContentRegionAvail();
            var imageSize = FitAspect(new Vector2(_aspect, 1f), avail);
            var offset = (avail - imageSize) * 0.5f;
            ImGui.SetCursorPos(ImGui.GetCursorPos() + offset);
            ImGui.Image((nint)_texId, imageSize);
        }

        ToastNotifications.Draw();

        ImGui.End();
        ImGui.PopStyleColor();
    }

    private static Vector2 FitAspect(Vector2 src, Vector2 dst)
    {
        var scale = MathF.Min(dst.X / src.X, dst.Y / src.Y);
        return src * scale;
    }
}