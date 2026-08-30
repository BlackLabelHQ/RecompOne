using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Context;

namespace RecompOne.Runtime.Host.Window;

internal sealed class CpuStatePanel : IPanel
{
    public string Name => "CPU State";
    public string TitleKey => "panel.cpu_state";
    public bool IsOpen { get; set; }

    private static readonly string[] GprNames =
    [
        "zero", "at", "v0", "v1", "a0", "a1", "a2", "a3",
        "t0", "t1", "t2", "t3", "t4", "t5", "t6", "t7",
        "s0", "s1", "s2", "s3", "s4", "s5", "s6", "s7",
        "t8", "t9", "k0", "k1", "gp", "sp", "fp", "ra"
    ];

    private readonly uint[] _prev = new uint[32];

    public void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2(320, 540), ImGuiCond.FirstUseEver);
        var open = IsOpen;
        if (!ImGui.Begin(this.Title(), ref open))
        {
            IsOpen = open;
            ImGui.End();
            return;
        }

        var cpu = Runtime.Cpu;
        if (cpu == null)
        {
            ImGui.TextDisabled("No CPU context");
            ImGui.End();
            IsOpen = open;
            return;
        }

        if (ImGui.BeginTabBar("##CpuTabs"))
        {
            if (ImGui.BeginTabItem("GPR"))
            {
                DrawGpr(cpu);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("COP0"))
            {
                DrawCop0(cpu);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        for (var i = 0; i < 32; i++) _prev[i] = cpu[i];

        IsOpen = open;
        ImGui.End();
    }

    private void DrawGpr(CpuContext cpu)
    {
        if (!ImGui.BeginTable("gpr", 2,
                ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH)) return;

        ImGui.TableSetupColumn("Reg", ImGuiTableColumnFlags.WidthFixed, 46);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        for (var i = 0; i < 32; i++)
        {
            var val = cpu[i];
            var changed = val != _prev[i];

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextDisabled(GprNames[i]);
            ImGui.TableSetColumnIndex(1);
            if (changed) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.55f, 0.2f, 1f));
            ImGui.Text($"{val:X8}");
            if (changed) ImGui.PopStyleColor();
        }

        ImGui.Separator();
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextDisabled("hi");
        ImGui.TableSetColumnIndex(1);
        ImGui.Text($"{cpu.HI:X8}");
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextDisabled("lo");
        ImGui.TableSetColumnIndex(1);
        ImGui.Text($"{cpu.LO:X8}");

        ImGui.EndTable();
    }

    private static void DrawCop0(CpuContext cpu)
    {
        if (!ImGui.BeginTable("cop0", 2,
                ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH)) return;

        ImGui.TableSetupColumn("Reg", ImGuiTableColumnFlags.WidthFixed, 72);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();


        void Row(string name, uint val)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextDisabled(name);
            ImGui.TableSetColumnIndex(1);
            ImGui.Text($"{val:X8}");
        }

        Row("SR", cpu.SR);
        Row("Cause", cpu.Cause);
        Row("EPC", cpu.EPC);
        Row("BadVAddr", cpu.BadVAddr);
        Row("PRId", cpu.PRId);

        ImGui.EndTable();
    }
}