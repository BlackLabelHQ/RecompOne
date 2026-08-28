using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Hardware;

namespace RecompOne.Runtime.Host.Window;

internal sealed class InputRollPanel : IPanel
{
    public string Name => "Input Roll";
    public string TitleKey => "panel.input_roll";
    public bool IsOpen { get; set; }

    static readonly (string Label, ushort Mask, Vector4 Color)[] Rows =
    [
        ("Triangle", Controller.Triangle,  new Vector4(0.36f, 0.85f, 0.55f, 1f)),
        ("Circle", Controller.Circle, new Vector4(0.95f, 0.38f, 0.40f, 1f)),
        ("Cross", Controller.Cross, new Vector4(0.40f, 0.62f, 1.00f, 1f)),
        ("Square", Controller.Square, new Vector4(0.94f, 0.48f, 0.80f, 1f)),
        ("Up", Controller.Up, new Vector4(0.88f, 0.88f, 0.88f, 1f)),
        ("Down", Controller.Down, new Vector4(0.76f, 0.76f, 0.76f, 1f)),
        ("Left", Controller.Left, new Vector4(0.64f, 0.64f, 0.64f, 1f)),
        ("Right", Controller.Right, new Vector4(0.52f, 0.52f, 0.52f, 1f)),
        ("L1", Controller.L1, new Vector4(0.36f, 0.82f, 0.90f, 1f)),
        ("R1", Controller.R1, new Vector4(0.36f, 0.82f, 0.90f, 1f)),
        ("L2", Controller.L2, new Vector4(1.00f, 0.68f, 0.28f, 1f)),
        ("R2", Controller.R2, new Vector4(1.00f, 0.68f, 0.28f, 1f)),
        ("Select", Controller.Select, new Vector4(0.58f, 0.58f, 0.62f, 1f)),
        ("Start",  Controller.Start, new Vector4(0.58f, 0.58f, 0.62f, 1f)),
        ("L3", Controller.L3, new Vector4(0.68f, 0.56f, 0.95f, 1f)),
        ("R3", Controller.R3, new Vector4(0.68f, 0.56f, 0.95f, 1f)),
    ];

    static readonly (string Label, int Index, Vector4 Color)[] AxisRows =
    [
        ("L Stick X", 0, new Vector4(0.30f, 0.58f, 1.00f, 1f)),
        ("L Stick Y", 1, new Vector4(0.30f, 0.58f, 1.00f, 1f)),
        ("R Stick X", 2, new Vector4(1.00f, 0.58f, 0.22f, 1f)),
        ("R Stick Y", 3, new Vector4(1.00f, 0.58f, 0.22f, 1f)),
    ];

    const float RowHeight = 12f;
    const float BarHeight = 6f;
    const float LabelWidth = 58f;

    int _framesPerScreen = 300;
    bool _pad2;
    bool _follow = true;
    int _scroll;

    public void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2(760, 260), ImGuiCond.FirstUseEver);
        bool open = IsOpen;
        if (!ImGui.Begin(this.Title(), ref open)) { IsOpen = open; ImGui.End(); return; }

        DrawToolbar();
        ImGui.Separator();
        DrawRoll();

        IsOpen = open;
        ImGui.End();
    }

    void DrawToolbar()
    {
        switch (InputRecorder.State)
        {
            case InputRecorder.Mode.Recording:
                if (ImGui.Button(Localization.T("menu.debug.input.stop_record"))) InputRecorder.Stop();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f),
                    Localization.T("menu.debug.input.recording", InputRecorder.Name, InputRecorder.Frame));
                break;

            case InputRecorder.Mode.Playing:
                if (ImGui.Button(Localization.T("panel.input_roll.stop"))) InputRecorder.Stop();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.4f, 0.85f, 1f, 1f),
                    Localization.T("menu.debug.input.playing", InputRecorder.Name, InputRecorder.Frame, InputRecorder.Length));
                break;

            default:
                if (ImGui.Button(Localization.T("menu.debug.input.record"))) InputRecorder.StartRecording();
                ImGui.SameLine();
                ImGui.TextDisabled(Localization.T("menu.debug.input.idle"));
                break;
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(120f);
        ImGui.SliderInt("##zoom", ref _framesPerScreen, 60, 1800, Localization.T("panel.input_roll.window"));
        ImGui.SameLine();
        ImGui.Checkbox(Localization.T("panel.input_roll.follow"), ref _follow);
        ImGui.SameLine();
        ImGui.Checkbox(Localization.T("panel.input_roll.pad2"), ref _pad2);
    }

    void DrawRoll()
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();

        float rollX = origin.X + LabelWidth;
        float rollW = MathF.Max(32f, avail.X - LabelWidth);
        int totalRows = Rows.Length + AxisRows.Length;
        float rollH = totalRows * RowHeight;

        uint bg = ImGui.GetColorU32(ImGuiCol.FrameBg);
        uint grid = ImGui.GetColorU32(ImGuiCol.Border);
        uint head = ImGui.GetColorU32(new Vector4(1f, 0.75f, 0.2f, 1f));

        draw.AddRectFilled(new Vector2(rollX, origin.Y), new Vector2(rollX + rollW, origin.Y + rollH), bg);

        int total = InputRecorder.Length;
        int cursor = InputRecorder.Frame;
        int span = _framesPerScreen;

        if (_follow) _scroll = Math.Max(0, cursor - span * 3 / 4);
        int first = Math.Clamp(_scroll, 0, Math.Max(0, total - 1));
        float px = rollW / span;

        for (int r = 0; r < totalRows; r++)
        {
            float y = origin.Y + r * RowHeight;
            if ((r & 1) == 0)
                draw.AddRectFilled(new Vector2(rollX, y), new Vector2(rollX + rollW, y + RowHeight),
                    ImGui.GetColorU32(ImGuiCol.TableRowBgAlt));
            var (label, colour) = r < Rows.Length
                ? (Rows[r].Label, Rows[r].Color)
                : (AxisRows[r - Rows.Length].Label, AxisRows[r - Rows.Length].Color);
            draw.AddText(new Vector2(origin.X, y), ImGui.GetColorU32(colour), label);
            draw.AddLine(new Vector2(rollX, y), new Vector2(rollX + rollW, y), grid);
        }
        draw.AddLine(new Vector2(rollX, origin.Y + rollH), new Vector2(rollX + rollW, origin.Y + rollH), grid);

        for (int r = 0; r < Rows.Length; r++)
        {
            ushort mask = Rows[r].Mask;
            uint on = ImGui.GetColorU32(Rows[r].Color);
            float y = origin.Y + r * RowHeight + (RowHeight - BarHeight) * 0.5f;
            int runStart = -1;

            for (int i = 0; i <= span; i++)
            {
                int frame = first + i;
                bool held = i < span && frame < total && Held(frame, mask);

                if (held && runStart < 0) runStart = i;
                else if (!held && runStart >= 0)
                {
                    draw.AddRectFilled(
                        new Vector2(rollX + runStart * px, y),
                        new Vector2(rollX + MathF.Max(i * px, runStart * px + 1f), y + BarHeight), on);
                    runStart = -1;
                }
            }
        }

        for (int a = 0; a < AxisRows.Length; a++)
        {
            var (_, axis, colour) = AxisRows[a];
            float y = origin.Y + (Rows.Length + a) * RowHeight + (RowHeight - BarHeight) * 0.5f;

            for (int i = 0; i < span; i++)
            {
                int frame = first + i;
                if (frame >= total) break;
                if (!InputRecorder.TryGetAxes(frame, _pad2, out byte lx, out byte ly, out byte rx, out byte ry)) continue;

                byte v = axis switch { 0 => lx, 1 => ly, 2 => rx, _ => ry };
                float strength = Math.Abs(v - 128) / 127f;
                if (strength < 0.02f) continue;

                var c = Desaturate(colour, strength);
                draw.AddRectFilled(
                    new Vector2(rollX + i * px, y),
                    new Vector2(rollX + MathF.Max((i + 1) * px, i * px + 1f), y + BarHeight),
                    ImGui.GetColorU32(c));
            }
        }

        float headX = rollX + (cursor - first) * px;
        if (headX >= rollX && headX <= rollX + rollW)
            draw.AddLine(new Vector2(headX, origin.Y), new Vector2(headX, origin.Y + rollH), head, 2f);

        ImGui.Dummy(new Vector2(avail.X, rollH));

        if (!_follow && total > span)
        {
            ImGui.SetNextItemWidth(avail.X);
            ImGui.SliderInt("##scroll", ref _scroll, 0, total - span, "%d");
        }
    }

    //stupid effect that i liked
    static Vector4 Desaturate(Vector4 c, float strength)
    {
        float grey = (c.X + c.Y + c.Z) / 3f;
        float t = Math.Clamp(strength, 0f, 1f);
        return new Vector4(
            grey + (c.X - grey) * t,
            grey + (c.Y - grey) * t,
            grey + (c.Z - grey) * t,
            1f);
    }

    bool Held(int frame, ushort mask)
    {
        if (!InputRecorder.TryGetFrame(frame, out ushort b1, out ushort b2)) return false;
        return ((_pad2 ? b2 : b1) & mask) == 0;
    }
}
