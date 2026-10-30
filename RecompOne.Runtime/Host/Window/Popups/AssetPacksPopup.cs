using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Assets;

namespace RecompOne.Runtime.Host.Window;

public sealed class AssetPacksPopup : Popup
{
    private static AssetReplacerManager Manager => AssetReplacerManager.Instance;

    private readonly ImageCache _icons = new();
    private int _iconsGeneration = -1;
    private string? _dragged;

    protected override string TitleKey => "assetpacks.title";
    protected override Vector2 Size => new(600f, 560f);

    protected override void DrawContent()
    {
        if (_iconsGeneration != Manager.Generation)
        {
            _iconsGeneration = Manager.Generation;
            _icons.Clear();
        }

        var packs = Manager.Packs;
        var on = packs.Count(p => p.Enabled);
        var count = packs.Count == 1 ? "1 pack" : $"{packs.Count} packs";
        if (on > 0) count += $"  -  {on} on";

        CardUi.Header(count,
            (FontSet.Or(FontSet.Plus, "+"), "New pack", static () => PopupManager.Open<NewPackPopup>()),
            (FontSet.Or(FontSet.FileImport, "Import"), "Import a .zip pack", Import),
            (FontSet.Or(FontSet.Rotate, "R"), "Reload packs", static () => Manager.Reload()),
            (FontSet.Or(FontSet.FolderOpen, "..."), Localization.T("common.open_folder"),
                static () => PackAuthoring.OpenExternal(PackAuthoring.Root)));

        if (packs.Count == 0)
        {
            CardUi.Empty("No asset packs yet.", "Press + to make one, or import a .zip.");
            return;
        }

        ImGuiUtils.Hint("Packs higher on the list win when two change the same thing.");
        ImGui.Spacing();

        for (var i = 0; i < packs.Count; i++) DrawPack(packs[i], i, packs.Count);
    }

    private void DrawPack(AssetPack pack, int index, int total)
    {
        ImGui.PushID(pack.Id);

        var meta = pack.Manifest.Version is { } version ? $"v{version}" : "";
        if (!string.IsNullOrWhiteSpace(pack.Manifest.Author))
            meta += (meta.Length > 0 ? "  -  " : "") + pack.Manifest.Author;
        if (pack.IsZip) meta += (meta.Length > 0 ? "  -  " : "") + ".zip";

        var card = new Card
        {
            Title = pack.DisplayName,
            Meta = meta,
            Description = pack.Manifest.Description,
            Status = pack.Enabled ? ImGuiUtils.Green : ImGuiUtils.Gray,
            Enabled = pack.Enabled,
            ToggleTip = pack.Enabled ? "Turn off" : "Turn on",
            Draggable = total > 1,
            Icon = size => DrawIcon(pack, size),
            Actions =
            [
                (FontSet.Or(FontSet.Pen, "Edit"), "Edit", false),
                (FontSet.Ellipsis, "", false)
            ]
        };

        var result = CardUi.Draw("card", card);
        if (result.Toggled) Manager.SetPackEnabled(pack.Id, !pack.Enabled);
        if (result.DragStarted) _dragged = pack.Id;
        if (result.Dropped && _dragged != null && _dragged != pack.Id)
        {
            Manager.MovePack(_dragged, index);
            _dragged = null;
        }

        if (result.Action == 0)
        {
            Close();
            AssetPackEditor.Edit(pack.Id);
        }
        else if (result.Action == 1)
        {
            ImGui.OpenPopup("opts");
        }

        if (ImGui.BeginPopup("opts"))
        {
            if (pack.Editable && ImGui.MenuItem(Localization.T("common.open_folder"))) PackAuthoring.OpenExternal(pack.Path);
            if (ImGui.MenuItem("Export as .zip...")) Export(pack);
            if (pack.IsZip && ImGui.MenuItem("Unpack to edit"))
            {
                if (PackAuthoring.Unpack(pack, out var error) == null)
                    ToastNotifications.ShowText("Asset Packs", $"Couldn't unpack: {error}");
            }

            ImGui.Separator();
            if (ImGui.MenuItem("Delete..."))
                ConfirmPopup.Ask($"Delete \"{pack.DisplayName}\"?", "Its files are removed. This can nt be undone.", "Delete",
                    () => PackAuthoring.Delete(pack));
            ImGui.Separator();
            ImGui.TextDisabled(pack.Id);
            ImGui.EndPopup();
        }

        ImGui.PopID();
    }

    private void DrawIcon(AssetPack pack, float size)
    {
        if (pack.Icon != null)
        {
            var (tex, w, h) = _icons.Get($"icon:{pack.Id}", () => pack.Icon);
            if (tex != 0)
            {
                ImGuiUtils.ImageBox(tex, w, h, new Vector2(size, size));
                return;
            }
        }

        ImGuiUtils.LetterIcon(pack.Id, pack.DisplayName, size);
    }

    private static void Import()
    {
        var source = ToolUi.PickFile("Asset pack", "zip");
        if (source == null) return;
        if (PackAuthoring.Import(source, out var error) == null)
            ToastNotifications.ShowText("Asset Packs", $"Couldn't import: {error}");
    }

    private static void Export(AssetPack pack)
    {
        if (ToolUi.SaveFile("Zip archive", "zip", $"{pack.Id}.zip") is not { } zip) return;
        try
        {
            PackAuthoring.ExportZip(pack, zip);
            ToastNotifications.ShowText("Asset Packs", $"Saved {Path.GetFileName(zip)}");
        }
        catch (Exception e)
        {
            ToastNotifications.ShowText("Asset Packs", $"Export failed: {e.Message}");
        }
    }
}

public sealed class NewPackPopup : Popup
{
    private string _name = "";
    private string _author = "";
    private string _description = "";
    private string _error = "";

    protected override string TitleKey => "assetpacks.new_title";
    protected override Vector2 Size => new(440f, 0f);

    protected override void OnOpened()
    {
        _name = "";
        _author = "";
        _description = "";
        _error = "";
    }

    protected override void DrawContent()
    {
        ImGui.TextUnformatted("Name");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        var enter = ImGui.InputTextWithHint("##name", "HD Textures", ref _name, 128, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.TextUnformatted("Author");
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##author", "Optional", ref _author, 128);
        ImGui.TextUnformatted("Description");
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##description", "Optional", ref _description, 512);

        if (_error.Length > 0) ImGuiUtils.TextWrappedColored(ImGuiUtils.Red, _error);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var button = 120f * Theme.Scale;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (ImGui.GetContentRegionAvail().X - button * 2f - spacing) * 0.5f);

        var valid = !string.IsNullOrWhiteSpace(_name);
        using (ImGuiUtils.Disabled(!valid))
        {
            if ((ImGui.Button("Create", new Vector2(button, 0f)) || enter) && valid) Create();
        }
        ImGui.SameLine();
        if (ImGui.Button(Localization.T("common.cancel"), new Vector2(button, 0f))) Close();
    }

    private void Create()
    {
        var id = PackAuthoring.Create(_name, _author, _description, out var error);
        if (id == null)
        {
            _error = $"Couldn't create the pack: {error}";
            return;
        }

        Close();
        PopupManager.Close<AssetPacksPopup>();
        AssetPackEditor.Edit(id);
    }
}

public sealed class ConfirmPopup : Popup
{
    private string _title = "";
    private string _message = "";
    private string _confirm = "";
    private Action? _action;

    protected override string TitleKey => "common.confirm";
    protected override Vector2 Size => new(420f, 0f);

    public static void Ask(string title, string message, string confirm, Action action)
    {
        var popup = PopupManager.Get<ConfirmPopup>();
        if (popup == null) return;
        popup._title = title;
        popup._message = message;
        popup._confirm = confirm;
        popup._action = action;
        popup.Open();
    }

    protected override void DrawContent()
    {
        ImGui.TextWrapped(_title);
        ImGui.TextDisabled(_message);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var button = 120f * Theme.Scale;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (ImGui.GetContentRegionAvail().X - button * 2f - spacing) * 0.5f);
        if (ImGuiUtils.Danger(_confirm, size: new Vector2(button, 0f)))
        {
            try
            {
                _action?.Invoke();
            }
            catch (Exception e)
            {
                ToastNotifications.ShowText(_confirm, e.Message);
            }

            Close();
        }

        ImGui.SameLine();
        if (ImGui.Button(Localization.T("common.cancel"), new Vector2(button, 0f))) Close();
    }
}
