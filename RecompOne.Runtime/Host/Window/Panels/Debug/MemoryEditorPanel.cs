using System.Numerics;
using System.Text;
using ImGuiNET;
using RecompOne.Runtime.Memory;
using System.Globalization;

namespace RecompOne.Runtime.Host.Window;

internal sealed class MemoryEditorPanel : IPanel
{
    public string Name => "Memory Editor";
    public bool IsOpen { get; set; }
    const int BytesPerRow = 16;

    uint _baseAddr;
    string _addrInput = "80000000";
    bool _scrollPending;

    int _editAddr = -1;
    string _editBuf = "";
    bool _editFocusPending;
    
    int _selStart = -1, _selEnd = -1;
    private bool _selecting;

    (int lo, int hi) Sel() => _selStart < 0 ? (-1, -1) : (Math.Min(_selStart, _selEnd), Math.Max(_selStart, _selEnd));
    bool InSelection(int idx) { var (lo, hi) = Sel(); return lo >= 0 && idx >= lo && idx <= hi; }
    static uint Rgba(float r, float g, float b, float a) => ((uint)(a * 255) << 24) | ((uint)(b * 255) << 16) | ((uint)(g * 255) << 8) | (uint)(r * 255);
    static readonly uint FrozenBg = Rgba(1f, 0.35f, 0.7f, 0.55f);
    static readonly uint SelBg = Rgba(0.35f, 0.55f, 1f, 0.35f);


    public void JumpTo(uint physAddr)
    {
        _baseAddr = physAddr & ~(uint)(BytesPerRow - 1);
        _addrInput = $"{0x80000000u + physAddr:X8}";
        _scrollPending = true;
    }

    public void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2(640, 480) * HostWindow.DpiScale, ImGuiCond.FirstUseEver);
        bool open = IsOpen;
        if (!ImGui.Begin(Name, ref open)) { IsOpen = open; ImGui.End(); return; }

        var mem = Runtime.Mem as PSMemory;
        if (mem == null) { ImGui.TextDisabled("No memory"); ImGui.End(); IsOpen = open; return; }

        DrawToolbar();
        ImGui.Separator();
        DrawHexContent(mem);

        IsOpen = open;
        ImGui.End();
    }

    void DrawToolbar()
    {
        ImGui.SetNextItemWidth(160 * HostWindow.DpiScale);
        if (ImGui.InputText("##addr", ref _addrInput, 10,
            ImGuiInputTextFlags.CharsHexadecimal | ImGuiInputTextFlags.EnterReturnsTrue))
        {
            if (uint.TryParse(_addrInput, NumberStyles.HexNumber, null, out uint parsed))
            {
                uint phys = parsed & 0x1FFFFFFFu;
                if (phys < 0x200000u) JumpTo(phys);
            }
        }
        ImGui.SameLine();
        ImGui.TextDisabled("Go to address (hex)");
        ImGui.SameLine();
        ImGui.Spacing(); //space is enug <- good english right here
        ImGui.SameLine();
        ImGui.TextDisabled("Click a byte to edit");
    }

    void DrawHexContent(PSMemory mem)
    {
        var ram = mem.Ram;
        int totalRows = (ram.Length + BytesPerRow - 1) / BytesPerRow;

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 1));

        if (!ImGui.BeginChild("##hexscroll", Vector2.Zero, ImGuiChildFlags.None))
        {
            ImGui.PopStyleVar();
            ImGui.EndChild();
            return;
        }

        float rowH = ImGui.GetTextLineHeightWithSpacing();

        if (_scrollPending)
        {
            int targetRow = (int)(_baseAddr / BytesPerRow);
            ImGui.SetScrollY(targetRow * rowH - ImGui.GetWindowHeight() * 0.4f);
            _scrollPending = false;
        }

        float scrollY = ImGui.GetScrollY();
        int firstRow = Math.Max(0, (int)(scrollY / rowH) - 1);
        int visRows = (int)(ImGui.GetWindowHeight() / rowH) + 2;
        int lastRow = Math.Min(totalRows, firstRow + visRows);

        if (firstRow > 0)
            ImGui.Dummy(new Vector2(1f, firstRow * rowH));

        for (int row = firstRow; row < lastRow; row++)
            DrawRow(mem, row);

        float remaining = (totalRows - lastRow) * rowH;
        if (remaining > 0f)
            ImGui.Dummy(new Vector2(1f, remaining));

        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left)) _selecting = false;

        DrawContextMenu(mem);

        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    void DrawContextMenu(PSMemory mem)
    {
        if (!ImGui.BeginPopup("memctx")) return;
        var (lo, hi) = Sel();
        int len = lo < 0 ? 0 : hi - lo + 1;
        if (len > 0)
        {
            if (ImGui.MenuItem($"Freeze {len} byte(s)")) mem.Freeze((uint)lo, len);
            if (ImGui.MenuItem("Unfreeze")) mem.Unfreeze((uint)lo, len);
            ImGui.Separator();
        }
        if (ImGui.MenuItem("Clear all freezes")) mem.ClearFreezes();
        ImGui.EndPopup();
    }

    static readonly StringBuilder _asciiSb = new(BytesPerRow);

    void DrawRow(PSMemory mem, int row)
    {
        var ram = mem.Ram;
        int baseOff = row * BytesPerRow;
        uint virtAddr = 0x80000000u + (uint)baseOff;
        var log = Runtime.RamLog;

        ImGuiEx.TextDisabled($"{virtAddr:X8}  ");
        ImGui.SameLine();

        _asciiSb.Clear();

        for (int col = 0; col < BytesPerRow; col++)
        {
            int idx = baseOff + col;
            byte b = idx < ram.Length ? ram[idx] : (byte)0;

            if (idx == _editAddr)
                DrawEditCell(mem, idx);
            else
                DrawByteCell(mem, log, idx, b);

            if (col < BytesPerRow - 1)
            {
                ImGui.SameLine();
                if (col == 7) ImGui.TextDisabled("  ");
                else ImGui.TextDisabled(" ");
                ImGui.SameLine();
            }

            _asciiSb.Append(b >= 32 && b < 127 ? (char)b : '.');
        }

        ImGui.SameLine();
        ImGuiEx.TextDisabled($"  {_asciiSb}");
    }

    void DrawByteCell(PSMemory mem, RamLogger log, int idx, byte b)
    {
        bool frozen = mem.IsFrozen((uint)idx);
        if (frozen || InSelection(idx))
        {
            var pos = ImGui.GetCursorScreenPos();
            var sz = ImGui.CalcTextSize("FF");
            ImGui.GetWindowDrawList().AddRectFilled(pos, new Vector2(pos.X + sz.X, pos.Y + sz.Y), frozen ? FrozenBg : SelBg);
        }

        float wHeat = log.HeatAt(idx);
        float rHeat = log.ReadHeatAt(idx);

        if (wHeat > 0.01f)
        {
            var wc = log.WriteColor;
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(wc.X, wc.Y, wc.Z, 0.4f + wHeat * 0.6f));
            ImGui.Text($"{b:X2}");
            ImGui.PopStyleColor();
        }
        else if (rHeat > 0.01f)
        {
            var rc = log.ReadColor;
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(rc.X, rc.Y, rc.Z, 0.4f + rHeat * 0.6f));
            ImGui.Text($"{b:X2}");
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.Text($"{b:X2}");
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
        {
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) { _selStart = _selEnd = idx; _selecting = true; }
            else if (_selecting && ImGui.IsMouseDown(ImGuiMouseButton.Left) && idx != _selEnd) { _selEnd = idx; _editAddr = -1; }
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                if (!InSelection(idx)) _selStart = _selEnd = idx;
                ImGui.OpenPopup("memctx");
            }
        }

        if (ImGui.IsItemClicked())
        {
            _editAddr = idx;
            _editBuf = $"{b:X2}";
            _editFocusPending = true;
        }
    }

    void DrawEditCell(PSMemory mem, int idx)
    {
        ImGui.PushID(idx);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        ImGui.SetNextItemWidth(ImGui.CalcTextSize("FF").X + 2f);

        if (_editFocusPending)
        {
            ImGui.SetKeyboardFocusHere();
            _editFocusPending = false;
        }

        bool commit = ImGui.InputText("##edit", ref _editBuf, 2,
            ImGuiInputTextFlags.CharsHexadecimal | ImGuiInputTextFlags.CharsUppercase |
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll |
            ImGuiInputTextFlags.NoHorizontalScroll);

        if (commit)
        {
            CommitEdit(mem, idx);
            int next = idx + 1;
            if (next < mem.Ram.Length)
            {
                _editAddr = next;
                _editBuf = $"{mem.Ram[next]:X2}";
                _editFocusPending = true;
            }
            else
            {
                _editAddr = -1;
            }
        }
        else if (ImGui.IsItemDeactivated())
        {
            CommitEdit(mem, idx);
            _editAddr = -1;
        }

        ImGui.PopStyleVar();
        ImGui.PopID();
    }

    //the edit needs to be writeen to ram after bf
    void CommitEdit(PSMemory mem, int idx)
    {
        if (byte.TryParse(_editBuf, NumberStyles.HexNumber, null, out byte val))
        {
            if (idx < mem.Ram.Length && mem.Ram[idx] != val)
                mem.Poke((uint)idx, val);
        }
    }
}
