using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Assets;
using RecompOne.Runtime.Assets.Textures;

namespace RecompOne.Runtime.Host.Window;

internal sealed partial class AssetPackEditor
{
    private int _texView;
    private string _texSearch = "";
    private bool _texHideDone;
    private float _texSize = 72f;
    private bool _texPicked;
    private ulong _texIndex, _texClut;
    private string? _texFile;
    private bool _texPalette;
    private string? _texGroup;
    private readonly SortedSet<string> _texGroups = new(StringComparer.OrdinalIgnoreCase);
    private List<SeenTexture> _texArtworks = [];
    private Dictionary<ulong, List<SeenTexture>> _texVariants = [];
    private int _texSeenVersion = -1;

    private void DrawTexturesTab(AssetPack pack)
    {
        var scale = Theme.Scale;
        var contents = Contents(pack);

        ImGuiUtils.Segmented("texview", ref _texView, "Seen in game", $"In this pack ({contents.Textures.Count})");
        ImGui.SameLine();
        ImGuiUtils.Search("##texsearch", ref _texSearch, "Search by hash", max: 32);
        if (_texView == 0)
        {
            ImGui.SameLine();
            ImGui.Checkbox("Hide replaced", ref _texHideDone);
            ImGui.SameLine();
            if (ImGui.Button("Clear list")) TextureRegistry.Clear();
            ImGuiUtils.Tooltip("Start the list over.");
            DrawGroupFilter();
        }

        ImGui.SameLine();
        ImGuiUtils.Width(110f);
        ImGui.SliderFloat("##texsize", ref _texSize, 40f, 160f, "Size %.0f");

        var details = 300f * scale;
        ImGui.BeginChild("##texgrid", new Vector2(-details - ImGui.GetStyle().ItemSpacing.X, 0f), ImGuiChildFlags.Borders);
        if (_texView == 0) DrawSeenGrid(pack);
        else DrawPackTextureGrid(pack, contents);
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("##texdetails", Vector2.Zero, ImGuiChildFlags.Borders);
        if (_texView == 0) DrawSeenDetails(pack, contents);
        else DrawPackTextureDetails(pack, contents);
        ImGui.EndChild();
    }

    private List<SeenTexture> SeenArtworks(out Dictionary<ulong, List<SeenTexture>> variants)
    {
        var version = TextureRegistry.Version;
        if (version == _texSeenVersion)
        {
            variants = _texVariants;
            return _texArtworks;
        }

        _texSeenVersion = version;
        variants = [];
        _texGroups.Clear();
        foreach (var t in TextureRegistry.Snapshot())
        {
            if (t.IsPage || t.Dynamic) continue;
            if (t.Group != null) _texGroups.Add(t.Group);
            if (!variants.TryGetValue(t.IndexHash, out var list)) variants[t.IndexHash] = list = [];
            list.Add(t);
        }

        var artworks = new List<SeenTexture>(variants.Count);
        foreach (var list in variants.Values)
        {
            list.Sort((a, b) => a.FirstSeen.CompareTo(b.FirstSeen));
            artworks.Add(list[0]);
        }

        artworks.Sort((a, b) => b.FirstSeen.CompareTo(a.FirstSeen));
        _texArtworks = artworks;
        _texVariants = variants;
        return artworks;
    }

    private void DrawGroupFilter()
    {
        if (_texGroups.Count == 0)
        {
            _texGroup = null;
            return;
        }

        ImGui.SameLine();
        ImGuiUtils.Filter("##texgroup", ref _texGroup, _texGroups, none: "Uncategorized");
    }

    private bool InGroup(SeenTexture t)
    {
        if (_texGroup == null) return true;
        if (_texGroup.Length == 0) return t.Group == null;
        return string.Equals(t.Group, _texGroup, StringComparison.OrdinalIgnoreCase);
    }

    private bool Matches(ulong index, ulong clut)
    {
        if (_texSearch.Length == 0) return true;
        return $"{index:x16}".Contains(_texSearch, StringComparison.OrdinalIgnoreCase) ||
               $"{clut:x16}".Contains(_texSearch, StringComparison.OrdinalIgnoreCase);
    }

    private void DrawSeenGrid(AssetPack pack)
    {
        var artworks = SeenArtworks(out var variants);
        var shown = new List<(SeenTexture Texture, TextureAsset? Owner)>(artworks.Count);
        foreach (var t in artworks)
        {
            var owner = OwnerOf(variants[t.IndexHash]);
            if (_texHideDone && owner != null) continue;
            if (!InGroup(t)) continue;
            if (!Matches(t.IndexHash, t.ClutHash)) continue;
            shown.Add((t, owner));
        }

        if (shown.Count == 0)
        {
            ImGuiUtils.Hint(artworks.Count == 0
                ? "Play the game and textures will show up here."
                : "No matches.");
            return;
        }

        DrawGrid(shown.Count, i =>
        {
            var (t, owner) = shown[i];
            var selected = _texPicked && _texIndex == t.IndexHash;
            Vector4? border = owner == null ? null : owner.PackId == pack.Id ? ImGuiUtils.Green : ImGuiUtils.Amber;
            if (Tile($"seen{i}", ImageCache.Seen(t), t.W, t.H, selected, border))
            {
                _texPicked = true;
                _texIndex = t.IndexHash;
                _texClut = t.ClutHash;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGuiUtils.ImageBox(ImageCache.Seen(t), t.W, t.H, new Vector2(192f, 192f) * Theme.Scale);
                ImGui.TextUnformatted($"{t.W} x {t.H}  -  {t.Bpp}-bit");
                if (t.Group != null) ImGui.TextDisabled(t.Group);
                if (owner != null) ImGui.TextColored(border!.Value, $"Replaced by {PackName(owner.PackId)}");
                ImGui.EndTooltip();
            }
        });
    }

    private static TextureAsset? OwnerOf(List<SeenTexture> variants)
    {
        foreach (var v in variants)
            if (Manager.ResolveTexture(v.IndexHash, v.ClutHash) is { } owner)
                return owner;
        return null;
    }

    private static string PackName(string id)
    {
        return Manager.Packs.FirstOrDefault(p => p.Id == id)?.DisplayName ?? id;
    }

    private void DrawGrid(int count, Action<int> item)
    {
        var size = _texSize * Theme.Scale;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var columns = Math.Max(1, (int)((ImGui.GetContentRegionAvail().X + spacing) / (size + spacing)));
        var rows = (count + columns - 1) / columns;

        unsafe
        {
            var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
            clipper.Begin(rows, size + ImGui.GetStyle().ItemSpacing.Y);
            while (clipper.Step())
                for (var row = clipper.DisplayStart; row < clipper.DisplayEnd; row++)
                for (var col = 0; col < columns; col++)
                {
                    var i = row * columns + col;
                    if (i >= count) break;
                    if (col > 0) ImGui.SameLine();
                    item(i);
                }

            clipper.End();
            clipper.Destroy();
        }
    }

    private bool Tile(string id, uint tex, int w, int h, bool selected, Vector4? border)
    {
        var size = _texSize * Theme.Scale;
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();

        ImGuiUtils.Checker(dl, pos, pos + new Vector2(size, size));
        if (tex != 0 && w > 0 && h > 0)
        {
            var fit = MathF.Min(size / w, size / h);
            var drawn = new Vector2(w * fit, h * fit);
            var at = pos + (new Vector2(size, size) - drawn) * 0.5f;
            dl.AddImage((nint)tex, at, at + drawn);
        }

        if (border is { } color)
        {
            dl.AddRect(pos, pos + new Vector2(size, size), ImGui.ColorConvertFloat4ToU32(color), 0f, ImDrawFlags.None,
                2f * Theme.Scale);
            dl.AddCircleFilled(pos + new Vector2(size - 7f, 7f) * 1f, 4f * Theme.Scale, ImGui.ColorConvertFloat4ToU32(color));
        }

        if (selected || hovered)
            dl.AddRect(pos - Vector2.One, pos + new Vector2(size, size) + Vector2.One,
                ImGui.ColorConvertFloat4ToU32(selected ? Theme.Accent : new Vector4(1f, 1f, 1f, 0.5f)), 0f,
                ImDrawFlags.None, (selected ? 3f : 1f) * Theme.Scale);

        return clicked;
    }

    private SeenTexture? SelectedSeen(out List<SeenTexture> variants)
    {
        variants = [];
        if (!_texPicked) return null;
        foreach (var t in TextureRegistry.Snapshot())
            if (t.IndexHash == _texIndex && !t.IsPage)
                variants.Add(t);
        variants.Sort((a, b) => a.FirstSeen.CompareTo(b.FirstSeen));
        return variants.FirstOrDefault(t => t.ClutHash == _texClut) ?? variants.FirstOrDefault();
    }

    private PackContents.Texture? PackFileFor(PackContents contents, ulong index, ulong clut)
    {
        return contents.Textures.FirstOrDefault(t => t.Index == index && t.Clut == clut && t.Mode == TextureMode.Rgba) ??
               contents.Textures.FirstOrDefault(t => t.Index == index && t.Clut == 0 && t.Mode == TextureMode.Rgba);
    }

    private (uint Tex, int W, int H) AssetImage(TextureAsset asset)
    {
        return _images.Get($"asset:{asset.PackId}:{asset.FileName}", asset.Open);
    }

    private (uint Tex, int W, int H) PackImage(AssetPack pack, string file)
    {
        return _images.Get($"file:{pack.Id}:{file}", () => pack.ReadAsset(file));
    }

    private void DrawSeenDetails(AssetPack pack, PackContents contents)
    {
        var t = SelectedSeen(out var variants);
        if (t == null)
        {
            ImGuiUtils.Hint("Select a texture.\n\nGreen: replaced by this pack.\nOrange: replaced by another pack.");
            return;
        }

        var scale = Theme.Scale;
        var box = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f;
        var owner = Manager.ResolveTexture(t.IndexHash, t.ClutHash);
        var mine = PackFileFor(contents, t.IndexHash, t.ClutHash);

        ImGui.BeginGroup();
        ImGui.TextDisabled("Original");
        ImGuiUtils.ImageBox(ImageCache.Seen(t), t.W, t.H, new Vector2(box, box));
        ImGui.EndGroup();
        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.TextDisabled("Your version");
        if (mine != null)
        {
            var (tex, w, h) = PackImage(pack, mine.File);
            ImGuiUtils.ImageBox(tex, w, h, new Vector2(box, box));
        }
        else if (owner != null)
        {
            var (tex, w, h) = AssetImage(owner);
            ImGuiUtils.ImageBox(tex, w, h, new Vector2(box, box));
        }
        else
        {
            ImGuiUtils.ImageBox(0, 0, 0, new Vector2(box, box), "none yet");
        }

        ImGui.EndGroup();

        ImGui.TextUnformatted($"{t.W} x {t.H} pixels  -  {t.Bpp}-bit color");
        if (t.Group != null) ImGuiUtils.Tag(t.Group, ImGuiUtils.Gray, false);
        if (mine == null && owner != null) ImGuiUtils.Tag($"Replaced by {PackName(owner.PackId)}", ImGuiUtils.Amber);

        if (variants.Count > 1)
        {
            ImGui.Spacing();
            ImGui.TextDisabled($"Drawn in {variants.Count} color versions:");
            var small = 28f * scale;
            for (var i = 0; i < variants.Count; i++)
            {
                if (i > 0 && ImGui.GetContentRegionAvail().X > small) ImGui.SameLine();
                var v = variants[i];
                var pos = ImGui.GetCursorScreenPos();
                if (ImGui.InvisibleButton($"variant{i}", new Vector2(small, small))) _texClut = v.ClutHash;
                var dl = ImGui.GetWindowDrawList();
                ImGuiUtils.Checker(dl, pos, pos + new Vector2(small, small));
                var tex = ImageCache.Seen(v);
                if (tex != 0) dl.AddImage((nint)tex, pos, pos + new Vector2(small, small));
                if (v.ClutHash == t.ClutHash)
                    dl.AddRect(pos, pos + new Vector2(small, small), ImGui.ColorConvertFloat4ToU32(Theme.Accent), 0f,
                        ImDrawFlags.None, 2f * scale);
            }

            ImGui.Checkbox("Only replace this color version", ref _texPalette);
            ImGuiUtils.Help("Off: replaces every color version at once.");
        }
        else
        {
            _texPalette = false;
        }

        ImGui.Spacing();
        var editable = pack.Editable;

        if (ImGuiUtils.Wide($"{FontSet.Or(FontSet.Pen, "")}  Edit image", editable))
            Try("Opening the image", () =>
            {
                var path = mine != null && (!_texPalette || mine.Clut != 0)
                    ? PackAuthoring.FullPath(pack, mine.File)
                    : PackAuthoring.WriteOriginal(pack, t, _texPalette);
                PackAuthoring.OpenExternal(path);
                Say("Opened. Save in your editor and the game updates.");
            });
        ImGuiUtils.Tooltip(editable
            ? "Opens it in your image editor. It can be bigger than the original, keep the same shape."
            : "Unpack the pack first.");

        if (ImGuiUtils.Wide("Choose image...", editable))
            if (ToolUi.PickFile("Images", "png,jpg,jpeg,bmp,tga") is { } file)
                Try("Replacing", () =>
                {
                    PackAuthoring.SetTexture(pack, t.IndexHash, t.ClutHash, _texPalette, file);
                    Say("Replaced.");
                });
        ImGuiUtils.Tooltip("PNG, JPG, BMP or TGA.");

        if (ImGuiUtils.Wide("Save original as..."))
            if (ToolUi.SaveFile("PNG image", "png", $"{t.IndexHash:x16}.png") is { } target)
                Try("Saving", () =>
                {
                    PackAuthoring.ExportOriginal(t, target);
                    Say($"Saved to {target}");
                });

        if (mine != null)
        {
            if (ImGuiUtils.Danger("Remove", editable, wide: true))
                Try("Removing", () =>
                {
                    PackAuthoring.RemoveFile(pack, mine.File);
                    Say("Removed.");
                });
        }

        ImGui.Spacing();
        ImGui.TextDisabled($"{t.IndexHash:x16}");
        if (ImGui.IsItemClicked()) ImGui.SetClipboardText($"{t.IndexHash:x16}");
        ImGuiUtils.Tooltip("Click to copy.");
    }

    private void DrawPackTextureGrid(AssetPack pack, PackContents contents)
    {
        var files = contents.Textures.Where(t => Matches(t.Index, t.Clut)).ToList();
        if (files.Count == 0)
        {
            ImGuiUtils.Hint(contents.Textures.Count == 0
                ? "No textures in this pack yet."
                : "No matches.");
            return;
        }

        DrawGrid(files.Count, i =>
        {
            var f = files[i];
            var (tex, w, h) = PackImage(pack, f.File);
            if (Tile($"file{i}", tex, w, h, _texFile == f.File, null)) _texFile = f.File;
            ImGuiUtils.Tooltip($"{f.File}\n{w} x {h}");
        });
    }

    private void DrawPackTextureDetails(AssetPack pack, PackContents contents)
    {
        var f = contents.Textures.FirstOrDefault(t => t.File == _texFile);
        if (f == null)
        {
            ImGuiUtils.Hint("Select a texture.");
            return;
        }

        var box = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f;
        var original = TextureRegistry.Snapshot().FirstOrDefault(t =>
            t.IndexHash == f.Index && !t.IsPage && (f.Clut == 0 || t.ClutHash == f.Clut));

        ImGui.BeginGroup();
        ImGui.TextDisabled("Original");
        if (original != null) ImGuiUtils.ImageBox(ImageCache.Seen(original), original.W, original.H, new Vector2(box, box));
        else ImGuiUtils.ImageBox(0, 0, 0, new Vector2(box, box), "not seen yet");
        ImGui.EndGroup();
        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.TextDisabled("Your version");
        var (tex, w, h) = PackImage(pack, f.File);
        ImGuiUtils.ImageBox(tex, w, h, new Vector2(box, box));
        ImGui.EndGroup();

        ImGui.TextWrapped(f.File);
        ImGui.TextDisabled($"{w} x {h}" + (original != null ? $"  -  original {original.W} x {original.H}" : ""));
        if (f.Clut != 0) ImGuiUtils.Tag("One color version only", ImGuiUtils.Gray);
        if (f.Mode == TextureMode.Indexed) ImGuiUtils.Tag("Palette index image", ImGuiUtils.Gray);

        ImGui.Spacing();
        var editable = pack.Editable;
        if (ImGuiUtils.Wide("Open in image editor", editable))
            PackAuthoring.OpenExternal(PackAuthoring.FullPath(pack, f.File));

        if (ImGuiUtils.Wide("Replace with another image...", editable))
            if (ToolUi.PickFile("Images", "png,jpg,jpeg,bmp,tga") is { } file) 
                Try("Replacing", () =>
                {
                    PackAuthoring.SetTexture(pack, f.Index, f.Clut, f.Clut != 0, file);
                    Say("Replaced.");
                });

        if (ImGuiUtils.Danger("Remove", editable, wide: true))
            Try("Removing", () =>
            {
                PackAuthoring.RemoveFile(pack, f.File);
                _texFile = null;
                Say("Removed.");
            });
    }
}
