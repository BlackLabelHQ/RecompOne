namespace RecompOne.UI.BigPicture.Session;

public class HostProfile(IPopupStyle popups)
{
    private const float ReferenceHeight = 1080f;
    private const float ReferenceScale = 1.25f;
    private const float MinScale = 1f;
    private const float MaxScale = 4f;
    private const ImGuiConfigFlags NavFlags = ImGuiConfigFlags.NavEnableGamepad | ImGuiConfigFlags.NavEnableKeyboard;
    
    private int _windowingNext;
    private int _windowingPrev;
    
    public float Scale { get; private set; } = 1f;
    
    public virtual void Measure(HostContext host)
    {
        var monitor = host.Window.Monitor?.VideoMode.Resolution?.Y ?? (int)ReferenceHeight;
        Scale = Math.Clamp(monitor / ReferenceHeight * ReferenceScale, MinScale, MaxScale);
    }
    
    public virtual void ApplyStyle(bool active)
    {
        Theme.ScaleMultiplier = active ? Scale : 1f;
        OutputPanel.Fullbleed = active;
        OutputPanel.ShowToasts = true;
        ToastNotifications.Centered = active;
        PopupManager.Fullscreen = active;
        PopupManager.Style = active ? popups : null;
    }
    
    public virtual void ApplyFlags(bool active)
    {
        var io = ImGui.GetIO();
        var context = ImGui.GetCurrentContext();
        if (_windowingNext == 0 && _windowingPrev == 0)
        {
            _windowingNext = context.ConfigNavWindowingKeyNext;
            _windowingPrev = context.ConfigNavWindowingKeyPrev;
        }
        
        context.ConfigNavWindowingWithGamepad = !active;
        context.ConfigNavWindowingKeyNext = active ? 0 : _windowingNext;
        context.ConfigNavWindowingKeyPrev = active ? 0 : _windowingPrev;
        if (active)
        {
            io.ConfigFlags |= NavFlags;
            io.BackendFlags |= ImGuiBackendFlags.HasGamepad;
            return;
        }
        
        io.ConfigFlags &= ~NavFlags;
        io.BackendFlags &= ~ImGuiBackendFlags.HasGamepad;
    }
    
    public virtual void ApplyFontSize()
    {
        var style = ImGui.GetStyle();
        style.FontSizeBase = Px(FontSet.BaseSize);
        style.FontScaleMain = 1f;
    }
}
