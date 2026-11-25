using System.Numerics;

namespace RecompOne.UI;

public sealed class InputPage
{
    private const float ColumnShare = 0.29f;
    private const float ColumnMin = 8.5f;
    private const float ColumnMax = 15f;
    private const float DiagramMin = 11f;
    private const float StackedDiagram = 12f;
    private const float HeaderCombo = 13f;
    private const float AxisCombo = 10f;
    
    private static readonly PadInput[] _left =
    [
        PadInput.L2, PadInput.L1, PadInput.Up, PadInput.Left, PadInput.Down, PadInput.Right, PadInput.Select, PadInput.L3
    ];
    
    private static readonly PadInput[] _right =
    [
        PadInput.R2, PadInput.R1, PadInput.Triangle, PadInput.Square, PadInput.Circle, PadInput.Cross, PadInput.Start, PadInput.R3
    ];
    
    private static readonly string[] _kindKeys = ["settings.input.kind.digital", "settings.input.kind.analog"];
    
    private static readonly string[] _axisNames = ["Left X", "Left Y", "Right X", "Right Y", "Trigger L", "Trigger R"];
    
    private static readonly (string Label, Func<GamepadBindings, int> Get, Action<GamepadBindings, int> Set)[] _axes =
    [
        ("L Stick X", p => p.LeftStickX, (p, v) => p.LeftStickX = v),
        ("L Stick Y", p => p.LeftStickY, (p, v) => p.LeftStickY = v),
        ("R Stick X", p => p.RightStickX, (p, v) => p.RightStickX = v),
        ("R Stick Y", p => p.RightStickY, (p, v) => p.RightStickY = v)
    ];
    
    private int _port;
    private PadInput? _hot;
    private PadInput? _hotNext;
    private PadInput? _pointed;
    
    public static void Register()
    {
        var page = new InputPage();
        SettingsPages.Page(SettingsPages.Input, "settings.input", 0).WithIcon(FontSet.Gamepad)
            .Add("bindings", page.Draw, 0);
    }
    
    private PortSettings Port => Settings.System.Input[_port];
    
    private void Draw()
    {
        _hot = _hotNext;
        _hotNext = null;
        
        if (Ui.UsesRows) DrawHeaderRows();
        else DrawHeader();
        
        if (!InputManager.IsPadConnected(_port))
            ImGuiUtils.TextColored(ImGuiUtils.Amber, Localization.T("settings.input.no_gamepad"));
        
        ImGui.Spacing();
        DrawLayout();
        ImGui.Spacing();
        
        if (!Ui.UsesRows) Ui.Note(Localization.T("settings.input.help"));
        if (Port.Kind == PadKind.Analog) DrawStickAxes();
        
        ImGui.Spacing();
        if (Ui.Button(Localization.T("settings.input.reset_defaults")))
        {
            Port.ResetKeys(_port);
            Port.ResetGamepad(_port);
            Settings.MarkDirty();
        }
    }
    
    private static string[] PortNames()
    {
        var names = new string[InputSettings.PortCount];
        for (var i = 0; i < names.Length; i++) names[i] = Localization.T("settings.input.pad", i + 1);
        return names;
    }
    
    private void DrawHeader()
    {
        var em = ImGui.GetFontSize();
        ImGuiUtils.Segmented("##input-port", ref _port, PortNames());
        
        ImGui.SameLine();
        var kinds = _kindKeys.Select(Localization.T).ToArray();
        var kind = (int)Port.Kind;
        ImGui.SetNextItemWidth(em * HeaderCombo);
        if (Ui.CompactCombo("##input-kind", kinds[kind], kinds, ref kind)) SetKind(kind);
        ImGuiUtils.Tooltip(Localization.T(kind == 1 ? "settings.input.kind.analog.hint" : "settings.input.kind.digital.hint"));
        
        var refresh = Localization.T("settings.input.device.refresh");
        var style = ImGui.GetStyle();
        var refreshWidth = ImGui.CalcTextSize(refresh).X + style.FramePadding.X * 2f;
        
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(Localization.T("settings.input.device"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-refreshWidth - style.ItemSpacing.X);
        DeviceChoices(out var names, out var index, out var preview);
        if (Ui.CompactCombo("##input-device", preview, names, ref index)) SetDevice(index);
        ImGui.SameLine();
        if (ImGui.Button(refresh)) InputManager.RefreshDevices();
    }
    
    private void DrawHeaderRows()
    {
        var ports = PortNames();
        Ui.Combo(Localization.T("settings.input.port"), ports[_port], ports, ref _port);
        
        var kinds = _kindKeys.Select(Localization.T).ToArray();
        var kind = (int)Port.Kind;
        var hint = Localization.T(kind == 1 ? "settings.input.kind.analog.hint" : "settings.input.kind.digital.hint");
        if (Ui.Combo(Localization.T("settings.input.kind"), kinds[kind], kinds, ref kind, hint)) SetKind(kind);
        
        DeviceChoices(out var names, out var index, out var preview);
        if (Ui.Combo(Localization.T("settings.input.device"), preview, names, ref index)) SetDevice(index);
    }
    
    private void SetKind(int kind)
    {
        Port.Kind = (PadKind)kind;
        Settings.MarkDirty();
    }
    
    private void DeviceChoices(out string[] names, out int index, out string preview)
    {
        var devices = InputManager.Devices;
        var current = Port.Device;
        var auto = Localization.T("settings.input.device.auto");
        
        names = devices.Select(static d => d.Name).Prepend(auto).ToArray();
        index = 0;
        preview = auto;
        if (string.IsNullOrEmpty(current)) return;
        
        index = -1;
        preview = Localization.T("settings.input.device.missing");
        for (var i = 0; i < devices.Count; i++)
        {
            if (devices[i].Id != current) continue;
            index = i + 1;
            preview = devices[i].Name;
            return;
        }
    }
    
    private void SetDevice(int index)
    {
        Port.Device = index <= 0 ? "" : InputManager.Devices[index - 1].Id;
        Settings.MarkDirty();
        InputManager.RefreshDevices();
    }
    
    private void DrawLayout()
    {
        var em = ImGui.GetFontSize();
        var style = ImGui.GetStyle();
        var gap = style.ItemSpacing.X;
        var available = ImGui.GetContentRegionAvail().X;
        var column = Math.Clamp(available * ColumnShare, em * ColumnMin, em * ColumnMax);
        var diagram = available - column * 2f - gap * 2f;
        var rows = _left.Length * ImGui.GetFrameHeight() + (_left.Length - 1) * style.ItemSpacing.Y;
        
        if (diagram < em * DiagramMin)
        {
            DrawDiagram(new Vector2(available, em * StackedDiagram));
            column = MathF.Floor((available - gap) * 0.5f);
            DrawColumn(_left, column);
            ImGui.SameLine(0f, gap);
            DrawColumn(_right, column);
            return;
        }
        
        DrawColumn(_left, column);
        ImGui.SameLine(0f, gap);
        DrawDiagram(new Vector2(diagram, rows));
        ImGui.SameLine(0f, gap);
        DrawColumn(_right, column);
    }
    
    private void DrawDiagram(Vector2 size)
    {
        var state = _port == 0 ? Controller.State : Controller.State2;
        var result = ControllerDiagram.Draw("##input-diagram", size, Port.Kind == PadKind.Analog, _hot, (ushort)~state);
        _pointed = result.Hovered;
        if (result.Clicked is { } input) BindingCapturePopup.Begin(_port, input, ImGui.GetIO().KeyShift);
    }
    
    private void DrawColumn(PadInput[] inputs, float width)
    {
        ImGui.BeginGroup();
        foreach (var input in inputs) DrawCell(input, width);
        ImGui.EndGroup();
    }
    
    private void DrawCell(PadInput input, float width)
    {
        var size = new Vector2(width, ImGui.GetFrameHeight());
        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        
        ImGui.PushStyleColor(ImGuiCol.NavCursor, Vector4.Zero);
        var clicked = ImGui.InvisibleButton($"##bind-{input}", size, ImGuiButtonFlags.EnableNav);
        ImGui.PopStyleColor();
        
        var hovered = ImGui.IsItemHovered();
        var focused = ImGuiUtils.NavFocused && ImGui.GetIO().NavVisible;
        if (hovered || focused) _hotNext = input;
        
        if (clicked) BindingCapturePopup.Begin(_port, input, ImGui.GetIO().KeyShift);
        else if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right)) Clear(input);
        else if (focused && ImGui.IsKeyPressed(ImGuiKey.GamepadFaceLeft, false)) BindingCapturePopup.Begin(_port, input, true);
        else if (focused && (ImGui.IsKeyPressed(ImGuiKey.GamepadFaceUp, false) || ImGui.IsKeyPressed(ImGuiKey.Delete, false))) Clear(input);
        
        if (Ui.UsesRows)
            Ui.Hint(Localization.T("settings.input.help_pad", PadSourceLabels.Name(PadSourceLabels.South),
                PadSourceLabels.Name(PadSourceLabels.West), PadSourceLabels.Name(PadSourceLabels.North)));
        
        DrawCellContent(input, min, max, hovered || focused, _pointed == input);
    }
    
    private void DrawCellContent(PadInput input, Vector2 min, Vector2 max, bool hot, bool pointed)
    {
        var draw = ImGui.GetWindowDrawList();
        var style = ImGui.GetStyle();
        var rounding = style.FrameRounding;
        var padding = style.FramePadding.X;
        var textY = min.Y + (max.Y - min.Y - ImGui.GetFontSize()) * 0.5f;
        
        draw.AddRectFilled(min, max, ImGui.GetColorU32(hot ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg), rounding);
        if (hot || pointed) draw.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(Theme.Accent), rounding, ImDrawFlags.None, MathF.Max(1f, Theme.Scale));
        
        var label = PadInputs.Label(input);
        draw.AddText(new Vector2(min.X + padding, textY), ImGui.GetColorU32(ImGuiCol.TextDisabled), label);
        
        var key = PadInputs.GetKey(Port.Keys, input);
        var pads = string.Join(", ", PadInputs.GetPad(Port.Gamepad, input).Select(PadSourceLabels.Name));
        var left = min.X + padding * 2f + ImGui.CalcTextSize(label).X;
        var x = max.X - padding;
        
        draw.PushClipRect(new Vector2(left, min.Y), max, true);
        if (key.Length == 0 && pads.Length == 0)
        {
            var none = Localization.T("settings.input.unbound");
            draw.AddText(new Vector2(x - ImGui.CalcTextSize(none).X, textY), ImGui.GetColorU32(ImGuiCol.TextDisabled), none);
        }
        else
        {
            if (pads.Length > 0)
            {
                x -= ImGui.CalcTextSize(pads).X;
                draw.AddText(new Vector2(x, textY), ImGui.ColorConvertFloat4ToU32(Theme.AccentText), pads);
                x -= padding * 2f;
            }
            
            if (key.Length > 0) draw.AddText(new Vector2(x - ImGui.CalcTextSize(key).X, textY), ImGui.GetColorU32(ImGuiCol.Text), key);
        }
        draw.PopClipRect();
    }
    
    private void Clear(PadInput input)
    {
        PadInputs.SetKey(Port.Keys, input, "");
        PadInputs.SetPad(Port.Gamepad, input, []);
        Settings.MarkDirty();
    }
    
    private void DrawStickAxes()
    {
        var pad = Port.Gamepad;
        Ui.Section(Localization.T("settings.input.sticks"));
        
        foreach (var (label, get, set) in _axes)
        {
            var current = get(pad);
            var name = current >= 0 && current < _axisNames.Length ? _axisNames[current] : $"axis {current}";
            
            if (!Ui.UsesRows) ImGui.SetNextItemWidth(ImGui.GetFontSize() * AxisCombo);
            if (!Ui.Combo(label, name, _axisNames, ref current)) continue;
            set(pad, current);
            Settings.MarkDirty();
        }
    }
}
