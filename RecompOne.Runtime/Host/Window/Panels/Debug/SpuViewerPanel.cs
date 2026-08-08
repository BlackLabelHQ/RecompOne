using System.Numerics;
using System.Text;
using ImGuiNET;

namespace RecompOne.Runtime.Host.Window;

//still needs a bit of working
internal sealed class SpuViewerPanel : IPanel
{
    public string Name => "SPU Viewer";
    public bool IsOpen { get; set; }

    readonly Spu.VoiceDebug[] _voices = new Spu.VoiceDebug[24];
    static readonly StringBuilder _flagsSb = new(8);

    public void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2(860, 520) * HostWindow.DpiScale, ImGuiCond.FirstUseEver);
        bool open = IsOpen;
        if (!ImGui.Begin(Name, ref open)) { IsOpen = open; ImGui.End(); return; }

        var spu = Runtime.Spu;
        spu.CaptureDebug(_voices, out var st);

        DrawGlobals(st);
        ImGui.Separator();
        DrawXa();
        ImGui.Separator();
        DrawVoices(st);
        IsOpen = open;
        ImGui.End();
    }

    static void DrawGlobals(Spu.SpuDebug st)
    {
        Pair("Main", $"{st.MainVolL:X4} {st.MainVolR:X4}");
        ImGui.SameLine(); ImGui.Spacing(); ImGui.SameLine();
        Pair("CD", $"{st.CdVolL:X4} {st.CdVolR:X4}");
        ImGui.SameLine(); ImGui.Spacing(); ImGui.SameLine();
        Pair("Ext", $"{st.ExtVolL:X4} {st.ExtVolR:X4}");
        ImGui.SameLine(); ImGui.Spacing(); ImGui.SameLine();
        Pair("Reverb", $"{st.ReverbVolL:X4} {st.ReverbVolR:X4} @ {st.ReverbStartAddr:X5}");

        Pair("SPUCNT", $"{st.Spucnt:X4}");
        ImGui.SameLine(); ImGui.Spacing(); ImGui.SameLine();
        Flag("Enable", (st.Spucnt & 0x8000) != 0);
        ImGui.SameLine();
        Flag("Unmute", (st.Spucnt & 0x4000) != 0);
        ImGui.SameLine();
        Flag("Reverb", (st.Spucnt & 0x0080) != 0);
        ImGui.SameLine();
        Flag("CD audio", (st.Spucnt & 0x0001) != 0);
        ImGui.SameLine(); ImGui.Spacing(); ImGui.SameLine();
        Pair("Transfer", $"{st.TransferAddr:X5}");
    }

    static void DrawXa()
    {
        bool playing = XaAudio.Playing;
        int buffered = XaAudio.BufferedSamples;
        int rate = XaAudio.SourceRate;
        float ms = rate > 0 ? buffered * 1000f / rate : 0f;

        Pair("XA", playing ? "playing" : "stopped");
        ImGui.SameLine(); ImGui.Spacing(); ImGui.SameLine();
        Pair("Rate", $"{rate} Hz");
        ImGui.SameLine(); ImGui.Spacing(); ImGui.SameLine();
        Pair("Buffered", $"{buffered} ({ms:F0} ms)");
    }

    void DrawVoices(Spu.SpuDebug st)
    {
        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersInnerV |
                         ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.ScrollY;

        if (!ImGui.BeginTable("voices", 10, tableFlags)) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("V", ImGuiTableColumnFlags.WidthFixed, 26);
        ImGui.TableSetupColumn("Phase", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("ENVX", ImGuiTableColumnFlags.WidthFixed, 76);
        ImGui.TableSetupColumn("VolL", ImGuiTableColumnFlags.WidthFixed, 44);
        ImGui.TableSetupColumn("VolR", ImGuiTableColumnFlags.WidthFixed, 44);
        ImGui.TableSetupColumn("Pitch", ImGuiTableColumnFlags.WidthFixed, 48);
        ImGui.TableSetupColumn("Start", ImGuiTableColumnFlags.WidthFixed, 56);
        ImGui.TableSetupColumn("Repeat", ImGuiTableColumnFlags.WidthFixed, 56);
        ImGui.TableSetupColumn("Cur", ImGuiTableColumnFlags.WidthFixed, 56);
        ImGui.TableSetupColumn("Flags", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        for (int i = 0; i < 24; i++)
        {
            var v = _voices[i];
            bool on = v.Phase != Spu.AdsrPhase.Off;

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            if (on) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.3f, 1f, 0.4f, 1f));
            ImGui.Text($"{i:D2}");
            if (on) ImGui.PopStyleColor();

            ImGui.TableSetColumnIndex(1);
            if (on) ImGui.TextUnformatted(v.Phase.ToString());
            else ImGui.TextDisabled("Off");

            ImGui.TableSetColumnIndex(2);
            ImGui.ProgressBar(v.AdsrVol / 32767f, new Vector2(70, ImGui.GetTextLineHeight()), $"{v.AdsrVol:X4}");

            ImGui.TableSetColumnIndex(3);
            ImGui.Text($"{v.VolL:X4}");
            ImGui.TableSetColumnIndex(4);
            ImGui.Text($"{v.VolR:X4}");
            ImGui.TableSetColumnIndex(5);
            ImGui.Text($"{v.Pitch:X4}");
            ImGui.TableSetColumnIndex(6);
            ImGui.Text($"{(uint)v.StartAddr << 3:X5}");
            ImGui.TableSetColumnIndex(7);
            ImGui.Text($"{(uint)v.RepeatAddr << 3:X5}");
            ImGui.TableSetColumnIndex(8);
            ImGui.Text($"{v.CurAddr:X5}");

            ImGui.TableSetColumnIndex(9);
            _flagsSb.Clear();
            if (v.Noise) _flagsSb.Append("N ");
            if (v.Pmod) _flagsSb.Append("P ");
            if (v.Reverb) _flagsSb.Append("R ");
            if (v.EndX) _flagsSb.Append("E ");
            ImGui.TextDisabled(_flagsSb.Length > 0 ? _flagsSb.ToString() : "-");
        }

        ImGui.EndTable();
    }

    static void Pair(string label, string value)
    {
        ImGui.TextDisabled(label);
        ImGui.SameLine();
        ImGui.TextUnformatted(value);
    }

    static void Flag(string label, bool set)
    {
        if (set) ImGui.TextUnformatted(label);
        else ImGui.TextDisabled(label);
    }
}
