namespace RecompOne.UI;

public interface IPanel
{
    string Name { get; }

    string TitleKey => Name;

    bool IsOpen { get; set; }

    void Draw();
}

/// <summary>A panel that can also show its content inside another screen, without its own window.</summary>
public interface IEmbeddedPanel : IPanel
{
    void DrawContent();
}

//generic panel, doesnt count for layout count
public interface IFloatingPanel : IPanel;

public static class PanelExtensions
{
    public static string Title(this IPanel panel)
    {
        return ImGuiUtils.Title(panel.TitleKey, panel.Name);
    }
}