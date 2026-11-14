namespace RecompOne.UI.BigPicture.Session;

public class BackRouter(ShellSession session, ShellNavigation navigation, MenuInput input)
{
    private bool _widgetWasBusy;
    private bool _itemWasActive;
    
    public virtual void Update()
    {
        var busy = input.WidgetBusy || _widgetWasBusy;
        _widgetWasBusy = input.WidgetBusy;
        var active = ImGui.IsAnyItemActive() || _itemWasActive;
        _itemWasActive = ImGui.IsAnyItemActive();
        if (active || PopupManager.TopChildWasOpen || (busy && !PopupManager.AnyOpen)) return;
        
        if (input.PadBack)
        {
            if (!PopupManager.CloseTop() && session.IsOpen && !PopupManager.AnyOpen) navigation.Back();
            return;
        }
        
        if (PopupManager.AnyOpen || !input.KeyBack) return;
        
        if (session.IsOpen) navigation.Back();
        else session.Open();
    }
}
