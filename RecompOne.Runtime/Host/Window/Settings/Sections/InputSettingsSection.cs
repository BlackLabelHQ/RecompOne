using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;
using RecompOne.Runtime.Config;

namespace RecompOne.Runtime.Host.Window;

internal sealed class InputSettingsSection : ISettingsSection
{
    public string Id => "input";
    public string TitleKey => "settings.input";
    public int Order => 0;

    static readonly (string Label, Func<KeyBindings, string> GetKey, Action<KeyBindings, string> SetKey, Func<GamepadBindings, int[]> GetPad, Action<GamepadBindings, int[]> SetPad)[] _rows =
    [
        ("Cross", b => b.Cross, (b,v) => b.Cross = v, p => p.Cross, (p,v) => p.Cross = v),
        ("Circle", b => b.Circle, (b,v) => b.Circle = v, p => p.Circle, (p,v) => p.Circle = v),
        ("Square", b => b.Square, (b,v) => b.Square = v, p => p.Square, (p,v) => p.Square = v),
        ("Triangle", b => b.Triangle, (b,v) => b.Triangle = v, p => p.Triangle, (p,v) => p.Triangle = v),
        ("L1", b => b.L1, (b,v) => b.L1 = v, p => p.L1, (p,v) => p.L1 = v),
        ("R1", b => b.R1, (b,v) => b.R1 = v, p => p.R1, (p,v) => p.R1 = v),
        ("L2", b => b.L2, (b,v) => b.L2 = v, p => p.L2, (p,v) => p.L2 = v),
        ("R2", b => b.R2, (b,v) => b.R2 = v, p => p.R2, (p,v) => p.R2 = v),
        ("L3", b => b.L3, (b,v) => b.L3 = v, p => p.L3, (p,v) => p.L3 = v),
        ("R3", b => b.R3, (b,v) => b.R3 = v, p => p.R3, (p,v) => p.R3 = v),
        ("Start", b => b.Start, (b,v) => b.Start = v, p => p.Start, (p,v) => p.Start = v),
        ("Select", b => b.Select, (b,v) => b.Select = v, p => p.Select, (p,v) => p.Select = v),
        ("Up", b => b.Up, (b,v) => b.Up = v, p => p.Up, (p,v) => p.Up = v),
        ("Down", b => b.Down, (b,v) => b.Down = v, p => p.Down, (p,v) => p.Down = v),
        ("Left", b => b.Left, (b,v) => b.Left = v, p => p.Left, (p,v) => p.Left = v),
        ("Right", b => b.Right, (b,v) => b.Right = v, p => p.Right, (p,v) => p.Right = v),
    ];

    bool _gamepadMode;
    int _padIndex;
    int _remapRow = -1;
    bool _remapAdd;

    public void Draw()
    {
        DrawDeviceSelector();
        ImGui.Spacing();
        DrawPadSelector();
        ImGui.Spacing();

        if (_gamepadMode)
        {
            DrawDeviceCombo();
            ImGui.Spacing();
        }

        if (_gamepadMode && !InputManager.IsPadConnected(_padIndex))
        {
            ImGuiEx.TextColored(new Vector4(1f, 0.75f, 0.3f, 1f), Localization.T("settings.input.no_gamepad"));
            ImGui.Spacing();
        }

        DrawBindings();

        ImGui.Spacing();
        if (ImGui.Button(Localization.T("settings.input.reset_defaults")))
        {
            if (!_gamepadMode)
            {
                if (_padIndex == 0) ConfigManager.Game.Keys = new KeyBindings();
                else ConfigManager.Game.Keys2 = KeyBindings.Empty();
            }
            else
            {
                if (_padIndex == 0) ConfigManager.Game.Pad = new GamepadBindings();
                else ConfigManager.Game.Pad2 = GamepadBindings.Empty();
            }
            _remapRow = -1;
            ConfigManager.SaveGame();
        }
    }

    void DrawDeviceSelector()
    {
        if (ImGui.BeginTabBar("##input-device"))
        {
            if (ImGui.BeginTabItem(Localization.T("settings.input.keyboard")))
            {
                if (_gamepadMode) _remapRow = -1;
                _gamepadMode = false;
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem(Localization.T("settings.input.gamepad")))
            {
                if (!_gamepadMode) _remapRow = -1;
                _gamepadMode = true;
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    void DrawPadSelector()
    {
        if (ImGui.BeginTabBar("##input-pad"))
        {
            if (ImGui.BeginTabItem(Localization.T("settings.input.pad", 1)))
            {
                if (_padIndex != 0) _remapRow = -1;
                _padIndex = 0;
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem(Localization.T("settings.input.pad", 2)))
            {
                if (_padIndex != 1) _remapRow = -1;
                _padIndex = 1;
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    void DrawDeviceCombo()
    {
        var devices = InputManager.Devices;
        string current = _padIndex == 0 ? ConfigManager.Game.PadDevice : ConfigManager.Game.PadDevice2;

        string preview = Localization.T("settings.input.device.auto");
        foreach (var d in devices)
            if (d.Id == current) { preview = d.Name; break; }
        if (!string.IsNullOrEmpty(current) && preview == Localization.T("settings.input.device.auto"))
            preview = Localization.T("settings.input.device.missing");

        ImGui.TextUnformatted(Localization.T("settings.input.device"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-70);

        if (ImGui.BeginCombo("##input-device-pick", preview))
        {
            if (ImGui.Selectable(Localization.T("settings.input.device.auto"), string.IsNullOrEmpty(current)))
                SetDevice("");

            foreach (var d in devices)
                if (ImGui.Selectable(d.Name, d.Id == current))
                    SetDevice(d.Id);

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.Button(Localization.T("settings.input.device.refresh")))
            InputManager.RefreshDevices();
    }

    void SetDevice(string id)
    {
        if (_padIndex == 0) ConfigManager.Game.PadDevice = id;
        else ConfigManager.Game.PadDevice2 = id;
        ConfigManager.SaveGame();
        InputManager.RefreshDevices();
    }

    void DrawBindings()
    {
        if (!ImGui.BeginTable("##bindings", 2,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn(Localization.T("settings.input.button"), ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn(Localization.T(_gamepadMode ? "settings.input.gamepad" : "settings.input.keyboard"),
            ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        var keys = _padIndex == 0 ? ConfigManager.Game.Keys : ConfigManager.Game.Keys2;
        var pad = _padIndex == 0 ? ConfigManager.Game.Pad : ConfigManager.Game.Pad2;

        for (int i = 0; i < _rows.Length; i++)
        {
            var (label, getKey, setKey, getPad, setPad) = _rows[i];
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted(label);

            ImGui.TableSetColumnIndex(1);
            bool awaiting = _remapRow == i;

            if (_gamepadMode)
            {
                var bindings = getPad(pad);
                string text = awaiting
                    ? Localization.T(_remapAdd ? "settings.input.press_button_add" : "settings.input.press_button")
                    : bindings.Length == 0 ? Localization.T("settings.input.unbound") : string.Join(" | ", bindings.Select(PadLabel));

                float plusW = ImGui.GetFrameHeight();
                float spacing = ImGui.GetStyle().ItemSpacing.X;
                if (ImGui.Button($"{text}##p{i}", new Vector2(-plusW - spacing, 0)))
                {
                    _remapRow = i;
                    _remapAdd = false;
                }
                ImGui.SameLine();
                if (ImGui.Button($"+##add{i}", new Vector2(plusW, 0)))
                {
                    _remapRow = i;
                    _remapAdd = true;
                }

                if (awaiting)
                {
                    var p = InputManager.GetFirstPressedPadButton(_padIndex);
                    if (p.HasValue)
                    {
                        if (_remapAdd)
                        {
                            if (!bindings.Contains(p.Value)) setPad(pad, [.. bindings, p.Value]);
                        }
                        else setPad(pad, [p.Value]);
                        _remapRow = -1;
                        ConfigManager.SaveGame();
                    }
                }
            }
            else
            {
                var key = getKey(keys);
                var text = awaiting
                    ? Localization.T("settings.input.press_key")
                    : $"{(key.Length == 0 ? Localization.T("settings.input.unbound") : key)}##k{i}";
                if (ImGui.Button(text, new Vector2(-1, 0))) _remapRow = i;
                if (awaiting)
                {
                    var p = GetPressedKey();
                    if (p != null) { setKey(keys, p); _remapRow = -1; ConfigManager.SaveGame(); }
                }
            }
        }

        ImGui.EndTable();
    }

    static string? GetPressedKey()
    {
        foreach (Key k in Enum.GetValues<Key>())
        {
            if (k is Key.Unknown or Key.Menu) continue;
            if (InputManager.IsKeyDown(k)) return k.ToString();
        }
        return null;
    }

    static string PadLabel(int b) => b switch
    {
        0 => "Cross (A)",
        1 => "Circle (B)",
        2 => "Square (X)",
        3 => "Triangle (Y)",
        4 => "Select (Back)",
        5 => "Guide",
        6 => "Start",
        7 => "L3 (LStick)",
        8 => "R3 (RStick)",
        9 => "L1 (LBumper)",
        10 => "R1 (RBumper)",
        11 => "D-Up",
        12 => "D-Down",
        13 => "D-Left",
        14 => "D-Right",
        100 => "L2 (LTrigger)",
        101 => "R2 (RTrigger)",
        102 => "LStick Left",
        103 => "LStick Right",
        104 => "LStick Up",
        105 => "LStick Down",
        106 => "RStick Left",
        107 => "RStick Right",
        108 => "RStick Up",
        109 => "RStick Down",
        _ => $"Btn {b}",
    };
}
