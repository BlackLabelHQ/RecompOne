using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Assets;
using RecompOne.Runtime.Assets.Sfx;
using RecompOne.Runtime.Assets.Textures;
using RecompOne.Runtime.Assets.Xa;

namespace RecompOne.Runtime.Host.Window;

internal sealed partial class AssetPackEditor : IPanel
{
    public string Name => "Asset Pack Editor";
    public string TitleKey => "panel.asset_pack_editor";

    private bool _open;

    public bool IsOpen
    {
        get => _open;
        set
        {
            _open = value;
            Collecting(value);
        }
    }

    private static AssetReplacerManager Manager => AssetReplacerManager.Instance;

    private readonly ImageCache _images = new();
    private int _imagesGeneration = -1;
    private string? _packId;
    private bool _focus;
    private PackContents? _contents;
    private string? _contentsId;
    private int _contentsGeneration = -1;

    private volatile string _status = "";
    private volatile bool _statusError;
    private volatile bool _statusFresh;
    private double _statusUntil;

    private string _editName = "";
    private string _editAuthor = "";
    private string _editVersion = "";
    private string _editDescription = "";
    private string? _editFor;

    public static void Edit(string packId)
    {
        var editor = PanelManager.Get<AssetPackEditor>();
        if (editor == null) return;
        editor._packId = packId;
        editor._focus = true;
        editor.IsOpen = true;
    }

    private void Collecting(bool on)
    {
        TextureRegistry.Collect(this, on);
        SampleRegistry.Enabled = on;
        XaRegistry.Enabled = on;
    }

    public void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2(980f, 640f) * Theme.Scale, ImGuiCond.FirstUseEver);
        if (_focus)
        {
            ImGui.SetNextWindowFocus();
            _focus = false;
        }

        var open = _open;
        if (!ImGui.Begin(this.Title(), ref open))
        {
            IsOpen = open;
            Collecting(false);
            ImGui.End();
            return;
        }

        Collecting(open);
        ImageCache.ReleaseOrphans();
        if (_imagesGeneration != Manager.Generation)
        {
            _imagesGeneration = Manager.Generation;
            _images.Clear();
        }

        var packs = Manager.Packs;
        var pack = packs.FirstOrDefault(p => p.Id == _packId) ?? packs.FirstOrDefault();
        _packId = pack?.Id;

        if (pack == null)
        {
            CardUi.Empty("There are no asset packs to edit.", "Create one from Mods > Asset Packs."); //first of all how would someone even open this if the editor needs to be open from the asset packs window? but better safe than sorry ig ig
            if (ImGui.Button("New pack...")) PopupManager.Open<NewPackPopup>();
        }
        else
        {
            DrawEditor(pack, packs);
        }

        IsOpen = open;
        ImGui.End();
    }

    private void Say(string message, bool error = false)
    {
        _statusError = error;
        _status = message;
        _statusFresh = true;
        if (error) Console.Error.WriteLine($"[assets] {message}");
    }

    private void Try(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Say($"{what} failed: {e.Message}", true);
        }
    }

    private PackContents Contents(AssetPack pack)
    {
        if (_contents == null || _contentsId != pack.Id || _contentsGeneration != Manager.Generation)
        {
            _contents = PackContents.Read(pack);
            _contentsId = pack.Id;
            _contentsGeneration = Manager.Generation;
        }

        return _contents;
    }

    private void DrawPackIcon(AssetPack pack, float size)
    {
        if (pack.Icon != null)
        {
            var (tex, w, h) = _images.Get($"icon:{pack.Id}", () => pack.Icon);
            if (tex != 0)
            {
                ImGuiUtils.ImageBox(tex, w, h, new Vector2(size, size));
                return;
            }
        }

        ImGuiUtils.LetterIcon(pack.Id, pack.DisplayName, size);
    }

    private void DrawEditor(AssetPack pack, IReadOnlyList<AssetPack> packs)
    {
        var scale = Theme.Scale;
        DrawPackIcon(pack, 44f * scale);
        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.SetNextItemWidth(260f * scale);
        if (ImGui.BeginCombo("##pack", pack.DisplayName))
        {
            foreach (var p in packs)
                if (ImGui.Selectable(p.DisplayName, p.Id == pack.Id))
                    _packId = p.Id;
            ImGui.EndCombo();
        }

        var contents = Contents(pack);
        ImGui.TextDisabled($"{Count(contents.Textures.Count, "texture")}  -  {Count(contents.Sounds.Count, "sound")}  -  " +
                           $"{Count(contents.Music.Count, "track")}");
        ImGui.EndGroup();

        if (pack.Editable)
        {
            ImGui.SameLine(ImGui.GetWindowWidth() - CardUi.IconSize - ImGui.GetStyle().WindowPadding.X);
            if (CardUi.IconButton("##folder", FontSet.Or(FontSet.FolderOpen, "..."), Localization.T("common.open_folder")))
                PackAuthoring.OpenExternal(pack.Path);
        }

        if (!pack.Enabled)
        {
            ImGuiUtils.Banner("This pack is off, changes won't show in game.", ImGuiUtils.Amber);
            if (ImGui.SmallButton("Turn on")) Manager.SetPackEnabled(pack.Id, true);
        }

        if (pack.IsZip)
        {
            ImGuiUtils.Banner("Zipped packs are read only.", ImGuiUtils.Amber);
            if (ImGui.SmallButton("Unpack to edit"))
            {
                if (PackAuthoring.Unpack(pack, out var error) == null) Say($"Couldn't unpack: {error}", true);
                else Say("Unpacked.");
            }
        }

        DrawStatus();
        ImGui.Spacing();

        if (!ImGui.BeginTabBar("##tabs")) return;

        if (ImGui.BeginTabItem("Textures"))
        {
            DrawTexturesTab(pack);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Sounds"))
        {
            DrawSoundsTab(pack);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Music & voice"))
        {
            DrawMusicTab(pack);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Info"))
        {
            DrawInfoTab(pack);
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private static string Count(int n, string noun)
    {
        return n == 1 ? $"1 {noun}" : $"{n} {noun}s";
    }

    private void DrawStatus()
    {
        if (_statusFresh)
        {
            _statusFresh = false;
            _statusUntil = ImGui.GetTime() + 6.0;
        }

        if (_status.Length == 0 || ImGui.GetTime() > _statusUntil) return;
        ImGuiUtils.TextWrappedColored(_statusError ? ImGuiUtils.Red : ImGuiUtils.Green, _status);
    }

    private void DrawInfoTab(AssetPack pack)
    {
        if (_editFor != pack.Id)
        {
            _editFor = pack.Id;
            _editName = pack.Manifest.Name ?? "";
            _editAuthor = pack.Manifest.Author ?? "";
            _editVersion = pack.Manifest.Version ?? "";
            _editDescription = pack.Manifest.Description ?? "";
        }

        var editable = pack.Editable;
        using (ImGuiUtils.Disabled(!editable))
        {

            var scale = Theme.Scale;
            ImGui.BeginGroup();
            DrawPackIcon(pack, 96f * scale);
            if (ImGui.Button("Change icon", new Vector2(96f * scale, 0f)))
                if (ToolUi.PickFile("Images", "png,jpg,jpeg,bmp,tga") is { } icon)
                    Try("Changing the icon", () => PackAuthoring.SetIcon(pack, icon));
            ImGui.EndGroup();

            ImGui.SameLine();
            ImGui.BeginGroup();
            var field = ImGui.GetContentRegionAvail().X;
            ImGui.SetNextItemWidth(field);
            ImGui.InputTextWithHint("##name", "Name", ref _editName, 128);
            if (ImGui.IsItemDeactivatedAfterEdit()) SaveInfo(pack);
            ImGui.SetNextItemWidth(field * 0.6f);
            ImGui.InputTextWithHint("##author", "Author", ref _editAuthor, 128);
            if (ImGui.IsItemDeactivatedAfterEdit()) SaveInfo(pack);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("##version", "Version", ref _editVersion, 32);
            if (ImGui.IsItemDeactivatedAfterEdit()) SaveInfo(pack);
            ImGui.InputTextMultiline("##description", ref _editDescription, 1024, new Vector2(-1f, 70f * scale));
            if (ImGui.IsItemDeactivatedAfterEdit()) SaveInfo(pack);
            if (_editDescription.Length == 0 && !ImGui.IsItemActive())
            {
                var min = ImGui.GetItemRectMin() + ImGui.GetStyle().FramePadding;
                ImGui.GetWindowDrawList().AddText(min, ImGui.GetColorU32(ImGuiCol.TextDisabled), "Description");
            }

            ImGui.EndGroup();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("Export as .zip"))
            if (ToolUi.SaveFile("Zip archive", "zip", $"{pack.Id}.zip") is { } zip)
                Try("Exporting", () =>
                {
                    PackAuthoring.ExportZip(pack, zip);
                    Say($"Saved {Path.GetFileName(zip)}");
                });

        ImGui.SameLine();
        if (ImGuiUtils.Danger("Delete pack"))
            ConfirmPopup.Ask($"Delete \"{pack.DisplayName}\"?", "Its files are removed. This can't be undone.", "Delete",
                () => PackAuthoring.Delete(pack));

        ImGui.Spacing();
        ImGui.TextDisabled(pack.Path);
    }

    private void SaveInfo(AssetPack pack)
    {
        pack.Manifest.Name = string.IsNullOrWhiteSpace(_editName) ? null : _editName.Trim();
        pack.Manifest.Author = string.IsNullOrWhiteSpace(_editAuthor) ? null : _editAuthor.Trim();
        pack.Manifest.Version = string.IsNullOrWhiteSpace(_editVersion) ? null : _editVersion.Trim();
        pack.Manifest.Description = string.IsNullOrWhiteSpace(_editDescription) ? null : _editDescription.Trim();
        Try("Saving", () => PackAuthoring.Save(pack));
    }
}
