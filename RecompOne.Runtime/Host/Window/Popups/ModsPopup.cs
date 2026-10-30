using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Modding;

namespace RecompOne.Runtime.Host.Window;

public sealed class ModsPopup : Popup
{
    private static readonly Dictionary<string, uint> _icons = new();
    private static readonly HashSet<string> _iconTried = new();

    private string? _settingsFor;

    protected override string TitleKey => "mods.title";
    protected override Vector2 Size => new(600f, 560f);

    protected override void DrawContent()
    {
        ModLoader.PollChanges();

        var mods = ModLoader.Mods;
        var active = mods.Count(m => m.Loaded);
        var count = mods.Count == 1 ? Localization.T("mods.count_one") : Localization.T("mods.count", mods.Count);
        if (active > 0) count += $"  -  {Localization.T("mods.active", active)}";

        CardUi.Header(count,
            (FontSet.Or(FontSet.Rotate, "R"), Localization.T("mods.reload"), static () => ModLoader.Rescan()),
            (FontSet.Or(FontSet.FolderOpen, "..."), Localization.T("common.open_folder"), OpenFolder));

        if (mods.Count == 0)
        {
            CardUi.Empty(Localization.T("mods.empty"), Localization.T("mods.empty_hint"));
            return;
        }

        foreach (var mod in mods) DrawMod(mod);
    }

    private void DrawMod(ModEntry mod)
    {
        ImGui.PushID(mod.Info.Id);

        var showGear = mod.Loaded && mod.HasSettings;
        var settingsOpen = showGear && _settingsFor == mod.Info.Id;
        var actions = new List<(string, string, bool)>();
        if (showGear) actions.Add((FontSet.Gear, Localization.T("mods.settings"), settingsOpen));
        actions.Add((FontSet.Ellipsis, "", false));

        var card = new Card
        {
            Title = string.IsNullOrWhiteSpace(mod.Info.Name) ? mod.Info.Id : mod.Info.Name,
            Meta = string.IsNullOrEmpty(mod.Info.Author) ? $"v{mod.Info.Version}" : $"v{mod.Info.Version}  -  {mod.Info.Author}",
            Description = mod.Info.Description,
            // enabled = green, failed = red, disabled = gray
            Status = !mod.Enabled ? ImGuiUtils.Gray : mod.Loaded ? ImGuiUtils.Green : ImGuiUtils.Red,
            StatusTip = mod.Enabled && !mod.Loaded ? mod.LoadError ?? Localization.T("mods.load_failed") : null,
            Enabled = mod.Enabled,
            ToggleTip = Localization.T(mod.Enabled ? "mods.disable" : "mods.enable"),
            Icon = size => DrawIcon(mod, size),
            Actions = actions.ToArray(),
            Footer = settingsOpen ? mod.DrawSettings : null
        };

        var result = CardUi.Draw("card", card);

        if (result.Toggled)
        {
            ModLoader.SetEnabled(mod.Info.Id, !mod.Enabled);
            if (!mod.Enabled && _settingsFor == mod.Info.Id) _settingsFor = null;
        }

        if (result.Action >= 0)
        {
            if (showGear && result.Action == 0) _settingsFor = settingsOpen ? null : mod.Info.Id;
            else ImGui.OpenPopup("opts");
        }

        if (ImGui.BeginPopup("opts"))
        {
            using (ImGuiUtils.Disabled(!mod.Enabled))
            {
                if (ImGui.MenuItem(Localization.T("mods.reload"))) ModLoader.Reload(mod.Info.Id);
            }
            ImGui.Separator();
            ImGui.TextDisabled(mod.Info.Id);
            ImGui.TextDisabled(Localization.T("mods.hooks", mod.HookCount));
            ImGui.EndPopup();
        }

        ImGui.PopID();
    }

    private static void DrawIcon(ModEntry mod,
        float size) //draw generic icon when no icon is proveded, its like the youtube one, first initial and colorful background based on modid hash
    {
        var tex = Icon(mod);
        if (tex != 0) ImGuiUtils.ImageBox(tex, 1, 1, new Vector2(size, size));
        else ImGuiUtils.LetterIcon(mod.Info.Id, string.IsNullOrWhiteSpace(mod.Info.Name) ? mod.Info.Id : mod.Info.Name, size);
    }

    private static void OpenFolder()
    {
        var root = ModLoader.Root;
        if (string.IsNullOrEmpty(root)) root = Path.GetFullPath("mods");
        Assets.PackAuthoring.OpenExternal(root);
    }

    private static uint Icon(ModEntry mod)
    {
        if (mod.IconData == null) return 0;
        if (_icons.TryGetValue(mod.Info.Id, out var t)) return t;
        if (!_iconTried.Add(mod.Info.Id)) return 0;
        var tex = HostWindow.UploadPng(mod.IconData);
        if (tex != 0) _icons[mod.Info.Id] = tex;
        return tex;
    }
}
