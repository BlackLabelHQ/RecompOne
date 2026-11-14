namespace RecompOne.UI.BigPicture.Shell;

public class ShellBody(ShellNavigation navigation, MenuInput input)
{
    private const ImGuiWindowFlags ListFlags = ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoScrollbar;
    
    private readonly RailList _pages = new("page", input); //content page
    private float _used;
    
    public ShellNavigation Navigation => navigation;
    
    public Vector2 Size { get; private set; }
    
    public virtual void Draw(Vector2 size)
    {
        Size = size;
        _used = 0f;
        ImGui.BeginChild("##bp-body", size, ImGuiChildFlags.None, ImGuiWindowFlags.NoNav);
        navigation.Current.Draw(this);
        ImGui.EndChild();
    }
    
    public virtual int PageList(IReadOnlyList<RailItem> items, int selected)
    {
        var focus = navigation.Focus;
        var pad = Metrics.PanelPadding;
        var width = Metrics.PageListWidth(Size.X);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(pad, pad));
        if (focus.Claims(ShellFocus.Pages)) ImGui.SetNextWindowFocus();
        ImGui.BeginChild("##bp-pages", new Vector2(width, Size.Y), ImGuiChildFlags.AlwaysUseWindowPadding, ListFlags);
        focus.Track(ShellFocus.Pages);
        ImGui.PopStyleVar();
        var clicked = _pages.Draw(items, selected, focus.Area == ShellFocus.Pages);
        ImGui.EndChild();
        
        _used = width;
        return clicked;
    }
    
    public virtual void Content(string id, Action draw)
    {
        var focus = navigation.Focus;
        var pad = Metrics.ContentPadding;
        if (_used > 0f) ImGui.SameLine(0f, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(pad, pad));
        if (focus.Claims(ShellFocus.Content)) ImGui.SetNextWindowFocus();
        ImGui.BeginChild(id, new Vector2(Size.X - _used, Size.Y), ImGuiChildFlags.AlwaysUseWindowPadding, ImGuiWindowFlags.NoScrollbar);
        focus.Track(ShellFocus.Content);
        ImGui.PopStyleVar();
        
        if (focus.Area == ShellFocus.Content) input.ShowCursor();
        else if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            focus.Move(ShellFocus.Content);
        
        var dragging = DragScroll.Begin();
        draw();
        DragScroll.End(dragging);
        
        if (focus.TakePrime(ShellFocus.Content)) input.PrimeSelection();
        ImGui.EndChild();
    }
}
