namespace RecompOne.UI.BigPicture.Style;

public class BigPicturePopupStyle(MenuInput input) : IPopupStyle
{
    private const float CrossInset = 0.34f;
    
    protected virtual float TitleHeight => Px(44f);
    
    protected virtual float TitleInset => Px(24f);
    
    protected virtual float CloseSize => Px(36f);
    
    protected virtual float CrossStroke => Px(2f);
    
    protected virtual Vector2 WindowPadding => Px(24f, 14f);
    
    protected virtual Vector2 FramePadding => Px(14f, 9f);
    
    protected virtual Vector2 ItemSpacing => Px(10f, 10f);
    
    public virtual void Push()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Metrics.PopupRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, WindowPadding);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, FramePadding);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, ItemSpacing);
        ImGui.PushStyleColor(ImGuiCol.Button, Palette.Raised);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.Accent);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Accent);
    }
    
    public virtual void Pop()
    {
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(5);
    }
    
    public virtual bool DrawTitle(string title, bool closable)
    {
        var height = TitleHeight;
        var origin = ImGui.GetCursorScreenPos();
        var left = TitleInset;
        var draw = ImGui.GetWindowDrawList();
        var y = origin.Y + (height - ImGui.GetFontSize()) * 0.5f;
        draw.AddText(Snap(new Vector2(origin.X + left, y)), Palette.Accent, title);
        
        var close = false;
        if (closable && input.UsesMouse)
        {
            var size = CloseSize;
            var pos = Snap(new Vector2(origin.X + ImGui.GetWindowWidth() - left * 0.5f - size, origin.Y + (height - size) * 0.5f));
            close = DrawClose(draw, pos, size);
        }
        
        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + height));
        ImGui.Dummy(Vector2.Zero);
        return close;
    }
    
    protected virtual bool DrawClose(ImDrawListPtr draw, Vector2 pos, float size)
    {
        ImGui.SetCursorScreenPos(pos);
        var close = ImGui.InvisibleButton("##close", new Vector2(size, size));
        if (ImGui.IsItemHovered()) draw.AddRectFilled(pos, pos + new Vector2(size, size), Rgba(Palette.Danger), Metrics.Rounding);
        
        var inset = size * CrossInset;
        var stroke = CrossStroke;
        draw.AddLine(pos + new Vector2(inset, inset), pos + new Vector2(size - inset, size - inset), Palette.Text, stroke);
        draw.AddLine(pos + new Vector2(size - inset, inset), pos + new Vector2(inset, size - inset), Palette.Text, stroke);
        return close;
    }
}
