namespace RecompOne.UI.BigPicture.Shell;

//full screen menu shell
public class BigPictureShell(ShellNavigation navigation, MenuInput input, HintChannel hints)
{
    private const ImGuiWindowFlags RootFlags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoDocking |
                                               ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNav |
                                               ImGuiWindowFlags.NoScrollWithMouse;
    
    private const ImGuiWindowFlags RailFlags = ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoScrollbar;
    
    private readonly RailList _rail = new("rail", input);
    private readonly List<RailItem> _items = [];
    private readonly ShellFade _fade = new();
    
    protected ShellBody Body { get; set; } = new(navigation, input);
    
    protected KeyLegend Keys { get; set; } = new(input);
    
    public virtual void Reset()
    {
        _fade.Restart();
    }
    
    public virtual void Draw()
    {
        navigation.Refresh();
        navigation.Update();
        hints.Clear();
        navigation.Focus.BeginFrame(PopupManager.AnyOpen);
        
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        ImGui.SetNextWindowViewport(viewport.ID);
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, _fade.Update());
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 0f);
        
        var showsGame = navigation.Current.ShowsGame;
        ImGui.PushStyleColor(ImGuiCol.WindowBg, showsGame ? Vector4.Zero : Palette.Backdrop);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        ImGui.Begin("##bp-shell", RootFlags);
        
        var footer = Metrics.Footer;
        var railWidth = Metrics.RailWidth(viewport.Size.X);
        
        DrawRail(new Vector2(railWidth, viewport.Size.Y), footer);
        if (!showsGame)
        {
            ImGui.SameLine(0f, 0f);
            Body.Draw(new Vector2(viewport.Size.X - railWidth, viewport.Size.Y - footer));
            DrawHint(railWidth, viewport.Size.Y, footer);
        }
        
        ToastNotifications.Draw();
        
        ImGui.End();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(5);
        navigation.Focus.EndFrame(PopupManager.AnyOpen || input.WidgetBusy || input.UsesMouse);
        navigation.Flush();
    }
    
    protected virtual void DrawRail(Vector2 size, float footer)
    {
        var focus = navigation.Focus;
        var pad = Metrics.PanelPadding;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(pad, pad));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Palette.Panel);
        if (focus.Claims(ShellFocus.Rail)) ImGui.SetNextWindowFocus();
        ImGui.BeginChild("##bp-rail", size, ImGuiChildFlags.AlwaysUseWindowPadding, RailFlags);
        focus.Track(ShellFocus.Rail);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        
        ShellHeader.Draw();
        ImGui.Dummy(new Vector2(0f, pad));
        
        ImGui.BeginChild("##bp-rail-list", new Vector2(0f, size.Y - ImGui.GetCursorPosY() - footer), ImGuiChildFlags.None, RailFlags);
        DrawTabs();
        ImGui.EndChild();
        
        DrawKeys(size.Y, footer, pad);
        ImGui.EndChild();
    }
    
    protected virtual void DrawTabs()
    {
        _items.Clear();
        foreach (var tab in navigation.Tabs) _items.Add(new RailItem(tab.Icon, tab.Label, tab.Danger));
        
        var clicked = _rail.Draw(_items, navigation.Index, navigation.Focus.Area == ShellFocus.Rail);
        if (clicked >= 0) navigation.Defer(() => Open(clicked));
    }
    
    protected virtual void Open(int tab)
    {
        navigation.Select(tab);
        navigation.Focus.Move(ShellFocus.Rail);
        navigation.Activate();
    }
    
    protected virtual void DrawKeys(float height, float footer, float inset)
    {
        var pos = ImGui.GetWindowPos();
        var inRail = navigation.Focus.Area == ShellFocus.Rail;
        var accept = Localization.T(inRail ? "big_picture.keys.open" : "big_picture.keys.select");
        var back = Localization.T(inRail ? "big_picture.keys.resume" : "big_picture.keys.back");
        Keys.Draw(new Vector2(pos.X + inset, pos.Y + height - footer + (footer - ImGui.GetFontSize()) * 0.5f), accept, back);
    }
    
    protected virtual void DrawHint(float left, float height, float footer)
    {
        if (hints.Current is not { } hint) return;
        
        var pos = ImGui.GetWindowPos();
        var y = pos.Y + height - footer + (footer - ImGui.GetFontSize()) * 0.5f;
        ImGui.GetWindowDrawList().AddText(Snap(new Vector2(pos.X + left + Metrics.ContentPadding, y)), Palette.Muted, hint);
    }
}
