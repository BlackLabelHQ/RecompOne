using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Hub;

namespace RecompOne.Runtime.Host.Window;

public sealed class ModHubPopup : Popup
{
    private static readonly Dictionary<string, uint> _icons = new();

    private string _search = "";
    private string? _type;
    private int _sort;

    protected override string TitleKey => "modhub.title";
    protected override Vector2 Size => new(680f, 620f);

    protected internal override void Update()
    {
        ModHub.Update();
    }

    protected override void OnOpened()
    {
        ModHub.Scan();
        ModHub.Refresh();
    }

    protected override void DrawContent()
    {
        var entries = ModHub.Entries;
        var installed = entries.Count(e => ModHub.StateOf(e) is HubInstallState.Installed or HubInstallState.Outdated);
        var count = Localization.T("modhub.count", entries.Count);
        if (installed > 0) count += $"  -  {Localization.T("modhub.installed_count", installed)}";

        CardUi.Header(count,
            (FontSet.Or(FontSet.Rotate, "R"), Localization.T("modhub.refresh"), static () => ModHub.Refresh(true)),
            (FontSet.Or(FontSet.ExternalLink, "->"), Localization.T("modhub.open_hub"),
                static () => Assets.PackAuthoring.OpenExternal(ModHub.RepositoryUrl)));

        DrawToolbar(entries);

        if (ModHub.Error != null && entries.Count == 0)
        {
            CardUi.Empty(Localization.T("modhub.failed"), ModHub.Error);
            return;
        }

        if (entries.Count == 0)
        {
            CardUi.Empty(Localization.T(ModHub.Loading ? "modhub.loading" : "modhub.empty"),
                Localization.T("modhub.empty_hint"));
            return;
        }

        var shown = Filtered(entries);
        if (shown.Count == 0)
        {
            CardUi.Empty(Localization.T("modhub.no_results"), "");
            return;
        }

        foreach (var entry in shown) DrawEntry(entry);
    }

    private void DrawToolbar(IReadOnlyList<HubEntry> entries)
    {
        ImGuiUtils.Search("##hubsearch", ref _search, Localization.T("modhub.search"), 220f);

        var types = entries.Select(e => e.Type).Distinct().Select(TypeLabel).OrderBy(t => t).ToList();
        if (types.Count > 1)
        {
            ImGui.SameLine();
            ImGuiUtils.Filter("##hubtype", ref _type, types, Localization.T("modhub.all_types"));
        }

        ImGui.SameLine();
        ImGuiUtils.Combo("##hubsort", ref _sort,
            [Localization.T("modhub.sort_stars"), Localization.T("modhub.sort_name"), Localization.T("modhub.sort_installed")]);
        ImGui.Spacing();
    }

    private List<HubEntry> Filtered(IReadOnlyList<HubEntry> entries)
    {
        var shown = entries.Where(e => _type == null || TypeLabel(e.Type) == _type).Where(Matches);
        shown = _sort switch
        {
            1 => shown.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
            2 => shown.OrderBy(e => ModHub.StateOf(e) is HubInstallState.Missing ? 1 : 0)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
            _ => shown.OrderByDescending(e => e.Stars ?? -1).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
        };
        return shown.ToList();
    }

    private bool Matches(HubEntry entry)
    {
        if (_search.Length == 0) return true;
        return entry.Name.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
               entry.Authors.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
               entry.Description.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
               entry.Tags.Any(t => t.Contains(_search, StringComparison.OrdinalIgnoreCase));
    }

    private static string TypeLabel(string type)
    {
        return ModHub.FindType(type) is { } known ? Localization.T(known.LabelKey) : type;
    }

    private static void DrawEntry(HubEntry entry)
    {
        using var _ = ImGuiUtils.Id(entry.Id);

        var state = ModHub.StateOf(entry);
        var job = ModHub.JobOf(entry);
        var busy = job is { Error: null };

        var actions = new List<(string, string, bool)>();
        var commands = new List<Action>();
        if (!busy)
        {
            if (state is HubInstallState.Missing)
            {
                actions.Add((FontSet.Download, Localization.T("modhub.install"), false));
                commands.Add(() => AskInstall(entry));
            }

            if (state is HubInstallState.Outdated)
            {
                actions.Add((FontSet.Rotate, Localization.T("modhub.update"), false));
                commands.Add(() => AskInstall(entry));
            }

            if (state is HubInstallState.Installed or HubInstallState.Outdated)
            {
                actions.Add((FontSet.Trash, Localization.T("modhub.remove"), false));
                commands.Add(() => ConfirmPopup.Ask(Localization.T("modhub.remove_title", entry.Name),
                    Localization.T("modhub.remove_message"), Localization.T("modhub.remove"), () => ModHub.Uninstall(entry)));
            }
        }

        actions.Add((FontSet.ExternalLink, Localization.T("modhub.open_repository"), false));
        commands.Add(() => Assets.PackAuthoring.OpenExternal(entry.RepositoryUrl));

        var meta = new List<string> { TypeLabel(entry.Type) };
        if (entry.Authors.Length > 0) meta.Add(entry.Authors);
        if (entry.Stars is { } stars) meta.Add($"{FontSet.Or(FontSet.Star, "*")} {stars}");
        else if (!entry.HasStars) meta.Add($"{FontSet.Or(FontSet.Star, "*")} {Localization.T("modhub.no_stars")}");

        var card = new Card
        {
            Title = entry.Name,
            Meta = string.Join("  -  ", meta),
            Description = entry.Description,
            Status = state switch
            {
                HubInstallState.Installed => ImGuiUtils.Green,
                HubInstallState.Outdated => ImGuiUtils.Amber,
                HubInstallState.Conflict => ImGuiUtils.Red,
                _ => ImGuiUtils.Gray
            },
            StatusTip = Localization.T(state switch
            {
                HubInstallState.Installed => "modhub.state_installed",
                HubInstallState.Outdated => "modhub.state_outdated",
                HubInstallState.Conflict => "modhub.state_conflict",
                _ => "modhub.state_missing"
            }),
            Enabled = true,
            CanToggle = false,
            Icon = size => DrawIcon(entry, size),
            Actions = actions.ToArray(),
            Footer = entry.Warning.Length > 0 || entry.Tags.Length > 0 || job != null ? () => DrawFooter(entry, job) : null
        };

        var result = CardUi.Draw("card", card);
        if (result.Action >= 0) commands[result.Action]();
    }

    private static void DrawFooter(HubEntry entry, HubJob? job)
    {
        if (entry.Tags.Length > 0) ImGuiUtils.Tags(entry.Tags.Select(t => (t, ImGuiUtils.Gray)).ToArray());
        if (entry.Warning.Length > 0)
            ImGuiUtils.TextWrappedColored(ImGuiUtils.Amber, $"{FontSet.Or(FontSet.Warning, "!")}  {entry.Warning}");

        if (job == null) return;
        if (job.Error != null)
        {
            ImGuiUtils.TextWrappedColored(ImGuiUtils.Red, job.Error);
            if (ImGui.SmallButton(Localization.T("modhub.dismiss"))) ModHub.DismissError(entry);
            return;
        }

        ImGui.ProgressBar(job.Removing ? 1f : job.Progress, new Vector2(-1f, 0f),
            Localization.T(job.Removing ? "modhub.removing" : "modhub.installing"));
    }

    private static void AskInstall(HubEntry entry)
    {
        if (entry.Warning.Length == 0)
        {
            ModHub.Install(entry);
            return;
        }

        ConfirmPopup.Ask(Localization.T("modhub.warning_title", entry.Name), entry.Warning,
            Localization.T("modhub.install"), () => ModHub.Install(entry));
    }

    private static void DrawIcon(HubEntry entry, float size)
    {
        var tex = Icon(entry);
        if (tex != 0) ImGuiUtils.ImageBox(tex, 1, 1, new Vector2(size, size));
        else ImGuiUtils.LetterIcon(entry.Id, entry.Name, size);
    }

    private static uint Icon(HubEntry entry)
    {
        var key = $"{entry.Id}@{entry.Commit}";
        if (_icons.TryGetValue(key, out var tex)) return tex;
        if (!entry.IconLoaded) return 0;
        tex = entry.IconData != null ? HostWindow.UploadPng(entry.IconData) : 0;
        _icons[key] = tex;
        return tex;
    }
}
