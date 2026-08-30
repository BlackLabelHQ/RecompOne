using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Assets.Textures;
using RecompOne.Runtime.Hle;
using Silk.NET.OpenGL;

namespace RecompOne.Runtime.Host.Window;

internal sealed class TextureInspectorPanel : IPanel
{
    public string Name => "Texture Inspector";
    public string TitleKey => "panel.texture_inspector";
    public bool IsOpen { get; set; }

    private string _filter = "";
    private bool _onlyReplaced;
    private bool _onlyPages;
    private bool _group = true;
    private bool _recentFirst;
    private int _thumbSize = 64;

    public void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2(760, 520), ImGuiCond.FirstUseEver);

        var open = IsOpen;
        if (!ImGui.Begin(this.Title(), ref open))
        {
            IsOpen = open;
            TextureRegistry.Enabled = open;
            ImGui.End();
            return;
        }

        TextureRegistry.Enabled = true;
        ReleaseOrphans();
        DrawToolbar();
        ImGui.Separator();
        DrawTable();

        IsOpen = open;
        TextureRegistry.Enabled = open;
        ImGui.End();
    }

    private void DrawToolbar()
    {
        var dumpTiles = TextureDumper.Tiles;
        if (ImGui.Checkbox("dump tiles", ref dumpTiles)) TextureDumper.SetTiles(dumpTiles);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("write each sampled tile to dump/<game>/textures");

        ImGui.SameLine();
        var dumpPages = TextureDumper.Pages;
        if (ImGui.Checkbox("dump pages", ref dumpPages)) TextureDumper.SetPages(dumpPages);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("write whole texture pages to dump/<game>/pages");

        ImGui.SameLine();
        if (ImGui.Button("Clear")) TextureRegistry.Clear();

        ImGui.SameLine();
        ImGui.SetNextItemWidth(180);
        ImGui.InputTextWithHint("##filter", "filter by hash", ref _filter, 64);

        ImGui.Checkbox("replaced", ref _onlyReplaced);
        ImGui.SameLine();
        ImGui.Checkbox("pages", ref _onlyPages);

        ImGui.SameLine();
        ImGui.Checkbox("group", ref _group);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("one row per artwork, collapsing its palette variants");
        ImGui.SameLine();
        ImGui.Checkbox("recent", ref _recentFirst);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("sort by last seen (reorders every frame)");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(110);
        ImGui.SliderInt("##thumb", ref _thumbSize, 32, 160, "%dpx");

        ImGui.TextDisabled($"{TextureRegistry.UniqueKeys} unique  |  {TextureRegistry.UniqueArtworks} artworks  |  " +
                           $"{TextureRegistry.Count} listed  |  dump: written={TextureDumper.Written} " +
                           $"pending={TextureDumper.Pending} dropped={TextureDumper.Dropped}");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("unique = distinct index+clut hashes seen this session\n" +
                             "artworks = distinct index hashes (palette variants collapsed)\n" +
                             "listed = currently held in the table (oldest are evicted)");
    }

    private void DrawTable()
    {
        var snapshot = TextureRegistry.Snapshot();

        var variants = new Dictionary<ulong, int>();
        SeenTexture[] all;
        if (_group)
        {
            var best = new Dictionary<ulong, SeenTexture>();
            foreach (var t in snapshot)
            {
                var key = t.IndexHash ^ (t.IsPage ? 1UL : 0UL);
                variants[key] = variants.GetValueOrDefault(key) + 1;
                if (!best.TryGetValue(key, out var cur) || t.FirstSeen < cur.FirstSeen ||
                    (!cur.Replaced && t.Replaced))
                    best[key] = t;
            }

            all = best.Values.ToArray();
        }
        else
        {
            all = snapshot;
        }

        Array.Sort(all, (a, b) => _recentFirst
            ? b.LastSeen.CompareTo(a.LastSeen)
            : a.FirstSeen.CompareTo(b.FirstSeen));

        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY |
                    ImGuiTableFlags.SizingFixedFit;
        if (!ImGui.BeginTable("##textures", 5, flags)) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("preview");
        ImGui.TableSetupColumn("size");
        ImGui.TableSetupColumn("index hash");
        ImGui.TableSetupColumn("clut hash");
        ImGui.TableSetupColumn("where");
        ImGui.TableHeadersRow();

        foreach (var t in all)
        {
            if (_onlyReplaced && !t.Replaced) continue;
            if (_onlyPages && !t.IsPage) continue;
            if (_filter.Length > 0 &&
                !$"{t.IndexHash:x16}".Contains(_filter, StringComparison.OrdinalIgnoreCase) &&
                !$"{t.ClutHash:x16}".Contains(_filter, StringComparison.OrdinalIgnoreCase))
                continue;

            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            DrawThumb(t);

            ImGui.TableNextColumn();
            ImGui.Text($"{t.W}x{t.H}");
            ImGui.TextDisabled($"{t.Bpp}bpp{(t.IsPage ? " page" : "")}");
            if (t.Dynamic)
            {
                ImGui.TextColored(new Vector4(0.95f, 0.7f, 0.3f, 1f), "dynamic");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("the GPU rendered into this region, so it is dumped but never replaced");
            }

            ImGui.TableNextColumn();
            HashCell($"{t.IndexHash:x16}", t.Replaced);

            ImGui.TableNextColumn();
            HashCell($"{t.ClutHash:x16}", false);
            if (_group && variants.TryGetValue(t.IndexHash ^ (t.IsPage ? 1UL : 0UL), out var n) && n > 1)
                ImGui.TextDisabled($"+{n - 1} palette{(n > 2 ? "s" : "")}");

            ImGui.TableNextColumn();
            ImGui.TextDisabled($"tp {t.TPage}  clut {t.Clut}");
            ImGui.TextDisabled($"uv {t.U0},{t.V0}  x{t.Hits}");
        }

        ImGui.EndTable();
    }

    private static void ReleaseOrphans()
    {
        var gl = GpuGlAccess.Gl;
        var ids = TextureRegistry.TakeOrphanTextures();
        if (gl == null || ids.Length == 0) return;
        foreach (var id in ids) gl.DeleteTexture(id);
    }

    private void HashCell(string hash, bool replaced)
    {
        if (replaced) ImGui.TextColored(new Vector4(0.45f, 0.9f, 0.5f, 1f), hash);
        else ImGui.Text(hash);

        if (ImGui.IsItemClicked()) ImGui.SetClipboardText(hash);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("click to copy");
    }

    private unsafe void DrawThumb(SeenTexture t)
    {
        var gl = GpuGlAccess.Gl;
        if (gl == null || t.Thumb.Length == 0)
        {
            ImGui.Dummy(new Vector2(_thumbSize, _thumbSize));
            return;
        }

        if (t.GlTex == 0)
        {
            t.GlTex = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, t.GlTex);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
            gl.TexImage2D<byte>(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)t.W, (uint)t.H, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, t.Thumb);
            gl.BindTexture(TextureTarget.Texture2D, 0);
        }

        var scale = _thumbSize / (float)Math.Max(t.W, t.H);
        var size = new Vector2(Math.Max(1, t.W * scale), Math.Max(1, t.H * scale));
        ImGui.Image((nint)t.GlTex, size);

        if (!ImGui.IsItemHovered()) return;
        ImGui.BeginTooltip();
        var big = 256f / Math.Max(t.W, t.H);
        ImGui.Image((nint)t.GlTex, new Vector2(t.W * big, t.H * big));
        ImGui.EndTooltip();
    }
}