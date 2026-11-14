namespace RecompOne.UI.BigPicture.Components.Widgets;

public abstract class Widget(WidgetSet widgets)
{
    protected WidgetSet Widgets { get; } = widgets;
    
    protected MenuInput Input => Widgets.Input;
    
    protected bool Focused => Input.ItemFocused;
    
    protected bool Hovered => Input.ItemHovered;
    
    protected void Hint(string? text)
    {
        Widgets.Hints.Show(text);
    }
}
