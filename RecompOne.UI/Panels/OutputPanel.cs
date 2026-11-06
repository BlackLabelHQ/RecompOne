using System.Numerics;

namespace RecompOne.UI;

public sealed class OutputPanel : IPanel
{
    public string Name => "Output";
    public string TitleKey => "panel.output";

    public bool IsOpen
    {
        get => true;
        set { }
    }

    public static bool IsDocked { get; private set; }

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

        var output = HostOutput.Display;
        if (output.IsValid)
        {
            var avail = ImGui.GetContentRegionAvail();
            var imageSize = FitAspect(new Vector2(output.Aspect, 1f), avail);
            var scale = ImGui.GetIO().DisplayFramebufferScale;
            HostOutput.ViewWidth = (int)MathF.Round(imageSize.X * scale.X);
            HostOutput.ViewHeight = (int)MathF.Round(imageSize.Y * scale.Y);
            var offset = (avail - imageSize) * 0.5f;
            ImGui.SetCursorPos(ImGui.GetCursorPos() + offset);
            ImGui.Image((nint)output.Id, imageSize);
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