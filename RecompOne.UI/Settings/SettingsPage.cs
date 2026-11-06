
namespace RecompOne.UI;

public sealed class SettingsPage
{
    private sealed record Group(string Id, Action Draw, int Order, string? TitleKey, Func<bool>? Visible);

    private readonly List<Group> _groups = [];
    private bool _sorted = true;

    internal SettingsPage(string id, string titleKey, int order)
    {
        Id = id;
        TitleKey = titleKey;
        Order = order;
    }

    public string Id { get; }
    public string TitleKey { get; internal set; }
    public int Order { get; internal set; }
    public Func<bool>? Visible { get; set; }
    public string Icon { get; set; } = FontSet.Gear;

    public bool IsVisible => (Visible?.Invoke() ?? true) && _groups.Exists(g => g.Visible?.Invoke() ?? true);

    public SettingsPage WithIcon(string icon) //so it can informe an icon to be used in gui that supports it (bigpicture addition)
    {
        Icon = icon;
        return this;
    }

    public SettingsPage Add(string id, Action draw, int order = 0, string? titleKey = null,
        Func<bool>? visible = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(draw);
        _groups.RemoveAll(g => g.Id == id);
        _groups.Add(new Group(id, draw, order, titleKey, visible));
        _sorted = false;
        return this;
    }

    public bool Remove(string id)
    {
        return _groups.RemoveAll(g => g.Id == id) > 0;
    }

    public void Draw()
    {
        if (!_sorted)
        {
            _groups.Sort(static (a, b) => a.Order != b.Order
                ? a.Order.CompareTo(b.Order)
                : string.CompareOrdinal(a.Id, b.Id));
            _sorted = true;
        }

        var first = true;
        foreach (var group in _groups.ToArray())
        {
            if (!(group.Visible?.Invoke() ?? true)) continue;

            if (!first) ImGui.Spacing();
            if (group.TitleKey != null) Ui.Section(Localization.T(group.TitleKey));
            else if (!first) Ui.Separator();
            first = false;

            ImGui.PushID(group.Id);
            group.Draw();
            ImGui.PopID();
        }
    }
}
