using System.Numerics;

namespace RecompOne.UI;

//todo: improve researching system
public sealed class ShadersPopup : Popup
{
    private const float ListWidth = 280f;
    private const float ActionWidth = 110f;
    private const float ComboWidth = 200f;
    private const string SearchIcon = "";

    private static readonly Vector4 ErrorColor = new(1f, 0.4f, 0.4f, 1f);

    private string installedFilter = "";
    private string browseFilter = "";
    private string category = "";
    private List<InstalledPreset> installed = [];
    private List<CatalogEntry> results = [];
    private (string, int) installedKey = ("", -1);
    private (string, string, int) resultsKey = ("", "", -1);
    private string? labelsFor;
    private Dictionary<string, string> labels = [];
    private float listTop;

    protected override string TitleKey => "shaders.title";
    protected override Vector2 Size => new(820f, 560f);

    protected override void OnOpened()
    {
        ShaderManager.Library.Refresh();
        ShaderManager.Repository.EnsureLoaded();
    }

    protected override void DrawContent()
    {
        if (!ImGui.BeginTabBar("##shader-tabs")) return;

        if (ImGui.BeginTabItem(Localization.T("shaders.installed")))
        {
            DrawInstalledList();
            ImGui.SameLine();
            DrawDetail();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(Localization.T("shaders.browse")))
        {
            DrawBrowse();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawInstalledList()
    {
        ImGui.BeginChild("##installed-list", new Vector2(ListWidth * Theme.Scale, 0f), ImGuiChildFlags.Borders);
        SearchBox("##installed-filter", ref installedFilter, -1f);
        ImGui.Spacing();

        if (installedKey != (installedFilter, ShaderManager.Library.Version))
        {
            installedKey = (installedFilter, ShaderManager.Library.Version);
            installed = ShaderManager.Library.Presets.Where(p => Matches(installedFilter, p.Name, p.Folder)).ToList();
        }

        if (ImGui.Selectable(Localization.T("shaders.none"), ShaderManager.Selected.Length == 0)) ShaderManager.Apply("");

        if (ShaderManager.Library.Presets.Count == 0)
        {
            ImGui.Spacing();
            WrappedDisabled(Localization.T("shaders.no_installed"));
        }

        string? remove = null;
        var rowHeight = ImGui.GetTextLineHeightWithSpacing();
        var (first, last) = BeginRows(installed.Count, rowHeight);
        for (var i = first; i < last; i++)
        {
            var preset = installed[i];
            if (ImGui.Selectable($"{preset.Name}##{preset.Relative}", preset.Relative == ShaderManager.Selected))
                ShaderManager.Apply(preset.Relative);
            Tooltip(preset.Relative);

            if (ImGui.BeginPopupContextItem($"##context-{preset.Relative}"))
            {
                if (ImGui.MenuItem(FontSet.With(FontSet.Trash, Localization.T("shaders.remove")))) remove = preset.Relative;
                ImGui.EndPopup();
            }
        }

        EndRows(installed.Count, rowHeight);
        if (remove != null) ShaderManager.Remove(remove);
        ImGui.EndChild();
    }

    private void DrawDetail()
    {
        ImGui.BeginChild("##installed-detail", Vector2.Zero, ImGuiChildFlags.Borders);

        if (ShaderManager.Selected.Length == 0)
        {
            Centered(Localization.T("shaders.inactive"));
        }
        else
        {
            DrawHeader();
            ImGui.Spacing();

            var native = PostFx.NativeInput;
            if (ImGui.Checkbox(Localization.T("shaders.native"), ref native)) ShaderManager.NativeInput = native;
            SettingsWidgets.Hint("shaders.native_hint");

            if (PostFx.Error == null && PostFx.Parameters.Count > 0)
            {
                var reset = Localization.T("shaders.reset");
                ImGui.SameLine((ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X) - ImGui.CalcTextSize(reset).X - ImGui.GetStyle().FramePadding.X * 2f);
                if (ImGui.Button(reset)) ShaderManager.ResetParams();
            }

            if (PostFx.Error is { } error)
            {
                ImGui.Spacing();
                ErrorText(Localization.T("shaders.failed"));
                ErrorText(error);
            }
            else
            {
                DrawParameters();
            }
        }

        ImGui.EndChild();
    }

    private void DrawHeader()
    {
        var style = ImGui.GetStyle();
        var button = ImGui.GetFrameHeight();
        var buttons = button * 2f + style.ItemSpacing.X;
        var start = ImGui.GetCursorPos();
        var right = start.X + ImGui.GetContentRegionAvail().X;

        ImGui.PushTextWrapPos(right - buttons - style.ItemSpacing.X);
        ImGui.TextUnformatted(PostFx.Name ?? System.IO.Path.GetFileNameWithoutExtension(ShaderManager.Selected));
        ImGui.TextDisabled(ShaderManager.Selected);
        ImGui.PopTextWrapPos();
        var end = ImGui.GetCursorPos();

        var height = end.Y - start.Y - style.ItemSpacing.Y;
        ImGui.SetCursorPos(new Vector2(right - buttons, start.Y + MathF.Max(0f, (height - button) * 0.5f)));
        if (IconButton(FontSet.Rotate, "shaders.reload", button)) ShaderManager.Reload();
        ImGui.SameLine();
        if (IconButton(FontSet.Trash, "shaders.remove", button)) ShaderManager.Remove(ShaderManager.Selected);

        ImGui.SetCursorPos(end);
    }

    private void DrawParameters()
    {
        var parameters = PostFx.Parameters;
        ImGui.Spacing();

        if (parameters.Count == 0)
        {
            ImGui.SeparatorText(Localization.T("shaders.parameters"));
            ImGui.TextDisabled(Localization.T("shaders.no_parameters"));
            return;
        }

        if (labelsFor != PostFx.Path)
        {
            labelsFor = PostFx.Path;
            labels = ParameterLabels.Build(parameters);
        }

        ImGui.SeparatorText(Localization.T("shaders.parameters"));

        ImGui.BeginChild("##parameters", Vector2.Zero);
        if (ImGui.BeginTable("##parameter-table", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("name", ImGuiTableColumnFlags.WidthStretch, 0.55f);
            ImGui.TableSetupColumn("value", ImGuiTableColumnFlags.WidthStretch, 0.45f);
            foreach (var parameter in parameters) DrawParameter(parameter);
            ImGui.EndTable();
        }

        ImGui.EndChild();
    }

    private void DrawParameter(SlangParameter parameter) //get better text
    {
        var label = labels.GetValueOrDefault(parameter.Name, parameter.Name);

        if (parameter.Max <= parameter.Min)
        {
            if (label.Length == 0) return;
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Spacing();
            ImGui.TextDisabled(label);
            return;
        }

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(label.Length == 0 ? parameter.Name : label);
        ImGui.PopTextWrapPos();
        Tooltip(parameter.Name);

        ImGui.TableNextColumn();
        ImGui.PushID(parameter.Name);
        ImGui.SetNextItemWidth(-1f);

        var value = parameter.Value;
        if (parameter.Min == 0f && parameter.Max == 1f && parameter.Step == 1f)
        {
            var on = value >= 0.5f;
            if (ImGui.Checkbox("##value", ref on)) ShaderManager.SetParam(parameter, on ? 1f : 0f);
        }
        else if (ImGui.SliderFloat("##value", ref value, parameter.Min, parameter.Max, Format(parameter.Step)))
        {
            ShaderManager.SetParam(parameter, Snap(parameter, value));
        }

        ImGui.PopID();
    }

    private void DrawBrowse()
    {
        var repository = ShaderManager.Repository;
        var style = ImGui.GetStyle();
        var combo = ComboWidth * Theme.Scale;
        var refresh = ImGui.GetFrameHeight();

        SearchBox("##browse-filter", ref browseFilter, ImGui.GetContentRegionAvail().X - combo - refresh - style.ItemSpacing.X * 2f);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(combo);
        var all = Localization.T("shaders.all_categories");
        if (ImGui.BeginCombo("##category", category.Length == 0 ? all : category))
        {
            if (ImGui.Selectable(all, category.Length == 0)) category = "";
            foreach (var option in repository.Categories)
                if (ImGui.Selectable(option, option == category))
                    category = option;
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(repository.Busy || !repository.Valid);
        if (IconButton(FontSet.Rotate, "shaders.refresh", refresh)) repository.Refresh();
        ImGui.EndDisabled();

        if (repository.Valid)
        {
            var info = Localization.T("shaders.catalog_info", repository.Entries.Count, repository.Label);
            if (repository.Fetched is { } fetched) info += $"  -  {fetched:g}";
            ImGui.TextDisabled(info);
        }

        if (repository.Truncated) ErrorText(Localization.T("shaders.truncated"));
        if (!repository.Valid) ErrorText(Localization.T("shaders.bad_repository"));
        if (repository.Error is { } error) ErrorText(error);

        var status = InstallStatus(repository);
        var footer = status != null ? ImGui.GetTextLineHeightWithSpacing() + style.ItemSpacing.Y : 0f;

        ImGui.BeginChild("##catalog", new Vector2(0f, -footer), ImGuiChildFlags.Borders);
        DrawCatalog(repository);
        ImGui.EndChild();

        if (status != null) ImGui.TextDisabled(status);
    }

    private void DrawCatalog(ShaderRepository repository)
    {
        if (repository.Entries.Count == 0)
        {
            Centered(Localization.T(repository.Busy ? "shaders.loading" : "shaders.no_results"));
            return;
        }

        if (resultsKey != (browseFilter, category, repository.Version))
        {
            resultsKey = (browseFilter, category, repository.Version);
            results = repository.Entries.Where(e =>
                (category.Length == 0 || e.Category == category) && Matches(browseFilter, e.Name, e.Folder)).ToList();
        }

        if (results.Count == 0)
        {
            Centered(Localization.T("shaders.no_results"));
            return;
        }

        var rowHeight = ImGui.GetFrameHeightWithSpacing();
        var (first, last) = BeginRows(results.Count, rowHeight);
        for (var i = first; i < last; i++) DrawEntry(repository, results[i]);
        EndRows(results.Count, rowHeight);
    }

    private void DrawEntry(ShaderRepository repository, CatalogEntry entry)
    {
        ImGui.PushID(entry.Path);
        var action = ActionWidth * Theme.Scale;

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(entry.Name);
        ImGui.SameLine();
        ImGui.TextDisabled(entry.Folder);
        Tooltip(entry.Path);

        ImGui.SameLine((ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X) - action);
        if (repository.Progress(entry) is { } progress)
        {
            ImGui.TextDisabled(Localization.T("shaders.installing", progress)); ///w
        }
        else if (!repository.IsInstalled(entry))
        {
            if (ImGui.Button(FontSet.With(FontSet.Download, Localization.T("shaders.install")), new Vector2(action, 0f)))
                repository.Install(entry);
        }
        else if (repository.LocalRelative(entry) == ShaderManager.Selected)
        {
            ImGui.TextColored(ImGui.GetStyle().Colors[(int)ImGuiCol.CheckMark],
                FontSet.With(FontSet.Check, Localization.T("shaders.active")));
        }
        else if (ImGui.Button(Localization.T("shaders.use"), new Vector2(action, 0f)))
        {
            ShaderManager.Apply(repository.LocalRelative(entry));
        }

        ImGui.PopID();
    }

    private static string? InstallStatus(ShaderRepository repository)
    {
        if (repository.LastInstall is not { } result) return null;
        return result.Error == null
            ? Localization.T("shaders.installed_ok", result.Name)
            : Localization.T("shaders.install_failed", result.Name, result.Error);
    }

    private (int First, int Last) BeginRows(int count, float rowHeight)
    {
        listTop = ImGui.GetCursorPosY();
        var scroll = ImGui.GetScrollY();
        var first = Math.Clamp((int)((scroll - listTop) / rowHeight), 0, count);
        var last = Math.Clamp((int)((scroll + ImGui.GetWindowHeight() - listTop) / rowHeight) + 1, first, count);
        ImGui.SetCursorPosY(listTop + first * rowHeight);
        return (first, last);
    }

    private void EndRows(int count, float rowHeight)
    {
        ImGui.SetCursorPosY(listTop + count * rowHeight);
        ImGui.Dummy(Vector2.Zero);
    }

    private static void SearchBox(string id, ref string filter, float width)
    {
        ImGui.SetNextItemWidth(width);
        ImGui.InputTextWithHint(id, $"{SearchIcon}  {Localization.T("shaders.search")}", ref filter, 128);
    }

    private static bool IconButton(string icon, string tooltipKey, float size)
    {
        var clicked = ImGui.Button(icon, new Vector2(size, size));
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(Localization.T(tooltipKey));
        return clicked;
    }

    private static void Tooltip(string text)
    {
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(text);
    }

    private static void ErrorText(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ErrorColor);
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();
    }

    private static void WrappedDisabled(string text)
    {
        ImGui.PushTextWrapPos(0f);
        ImGui.TextDisabled(text);
        ImGui.PopTextWrapPos();
    }

    private static void Centered(string text)
    {
        var offset = (ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(text).X) * 0.5f;
        ImGui.Spacing();
        if (offset > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
        ImGui.TextDisabled(text);
    }

    private static bool Matches(string filter, string name, string folder)
    {
        foreach (var word in filter.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (!name.Contains(word, StringComparison.OrdinalIgnoreCase) &&
                !folder.Contains(word, StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    private static float Snap(SlangParameter parameter, float value)
    {
        if (parameter.Step > 0f)
            value = parameter.Min + MathF.Round((value - parameter.Min) / parameter.Step) * parameter.Step;
        return Math.Clamp(value, parameter.Min, parameter.Max);
    }

    private static string Format(float step)
    {
        return step >= 1f ? "%.0f" : step >= 0.1f ? "%.1f" : step >= 0.01f ? "%.2f" : "%.3f";
    }
}
