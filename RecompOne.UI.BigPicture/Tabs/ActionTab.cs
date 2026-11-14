namespace RecompOne.UI.BigPicture.Tabs;

public class ActionTab(string id, string icon, string labelKey, Action<ShellNavigation> action, bool danger = false) : ShellTab(id)
{
    public override string Icon => icon;
    
    public override string Label => Localization.T(labelKey);
    
    public override bool Danger => danger;
    
    public override bool ShowsGame => true;
    
    public override void Activate(ShellNavigation navigation)
    {
        action(navigation);
    }
    
    public override void Draw(ShellBody body)
    {
    }
}
