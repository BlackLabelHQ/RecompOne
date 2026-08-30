using System.Numerics;
using ImGuiNET;

namespace RecompOne.Runtime.Host.Window;

internal sealed class TextureView
{
    public float Zoom = 1f;
    private Vector2 _pan;
    private bool _first = true;

    public void Reset()
    {
        Zoom = 1f;
        _pan = Vector2.Zero;
    }

    public readonly struct Result(bool hovered, Vector2 imgPos, float scale)
    {
        public bool Hovered { get; } = hovered;
        public Vector2 ImgPos { get; } = imgPos;
        public float Scale { get; } = scale;
    }

    public Result Draw(uint texId, int texW, int texH, float minZoom = 0.1f, float maxZoom = 64f)
    {
        var avail = ImGui.GetContentRegionAvail();
        var origin = ImGui.GetCursorScreenPos();
        if (texId == 0 || texW <= 0 || texH <= 0 || avail.X <= 0 || avail.Y <= 0)
            return new Result(false, origin, 1f);

        if (_first)
        {
            Reset();
            _first = false;
        }

        ImGui.InvisibleButton("##texview", avail);
        var hovered = ImGui.IsItemHovered();

        var baseScale = MathF.Min(avail.X / texW, avail.Y / texH);
        var scale = baseScale * Zoom;
        var drawn = new Vector2(texW * scale, texH * scale);
        var imgPos = origin + (avail - drawn) * 0.5f + _pan;

        var io = ImGui.GetIO();
        if (hovered && io.MouseWheel != 0)
        {
            var texel = (io.MousePos - imgPos) / scale;
            Zoom = Math.Clamp(Zoom * (io.MouseWheel > 0 ? 1.15f : 1f / 1.15f), minZoom, maxZoom);
            scale = baseScale * Zoom;
            drawn = new Vector2(texW * scale, texH * scale);
            imgPos = io.MousePos - texel * scale;
            _pan = imgPos - origin - (avail - drawn) * 0.5f;
        }

        if (hovered && ImGui.IsMouseDragging(ImGuiMouseButton.Middle))
        {
            _pan += ImGui.GetMouseDragDelta(ImGuiMouseButton.Middle);
            ImGui.ResetMouseDragDelta(ImGuiMouseButton.Middle);
            imgPos = origin + (avail - drawn) * 0.5f + _pan;
        }

        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(origin, origin + avail, true);
        dl.AddImage((nint)texId, imgPos, imgPos + drawn);
        dl.PopClipRect();

        return new Result(hovered, imgPos, scale);
    }
}