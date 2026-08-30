namespace RecompOne.Runtime.Host.Window;

public interface IPanel
{
    string Name { get; }

    string TitleKey => Name;

    bool IsOpen { get; set; }

    void Draw();
}

//generic panel, doesnt count for layout count
public interface IFloatingPanel : IPanel;

public static class PanelExtensions
{
    public static string Title(this IPanel panel)
    {
        return ImGuiEx.Title(panel.TitleKey, panel.Name);
    }
}