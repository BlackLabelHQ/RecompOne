namespace RecompOne.UI.BigPicture.Tabs;

public abstract class ShellTab(string id)
{
    public string Id { get; } = id;
    
    public abstract string Icon { get; }
    
    public abstract string Label { get; }
    
    public virtual bool Danger => false;
    
    public virtual bool ShowsGame => false;
    
    public virtual bool HasPages => false;
    
    public virtual void Refresh()
    {
    }
    
    public virtual void Activate(ShellNavigation navigation)
    {
        navigation.Focus.Move(ShellFocus.Content);
    }
    
    public virtual bool Back(ShellNavigation navigation) => false;
    
    public virtual void MovePage(int delta)
    {
    }
    
    public virtual void Leave()
    {
    }
    
    public abstract void Draw(ShellBody body);
}
