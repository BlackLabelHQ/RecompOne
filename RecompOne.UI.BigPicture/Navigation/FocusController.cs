namespace RecompOne.UI.BigPicture.Navigation;

public class FocusController
{
    private bool _claim = true;
    private bool _prime;
    private bool _held;
    private bool _blocked;
    
    public ShellFocus Area { get; private set; } = ShellFocus.Rail;
    
    public void Move(ShellFocus area)
    {
        Area = area;
        _claim = true;
        _prime = area == ShellFocus.Content;
    }
    
    public void Reclaim()
    {
        _claim = true;
    }
    
    public void BeginFrame(bool blocked)
    {
        _held = false;
        _blocked = blocked;
    }
    
    //true if should take focous
    public bool Claims(ShellFocus area) => _claim && Area == area && !_blocked;
    
    public void Track(ShellFocus area)
    {
        if (Area == area && ImGui.IsWindowFocused(ImGuiFocusedFlags.ChildWindows)) _held = true;
    }
    
    public bool TakePrime(ShellFocus area)
    {
        if (Area != area || !_prime) return false;
        _prime = false;
        return true;
    }
    
    public void EndFrame(bool busy)
    {
        _claim = !_held && !busy;
    }
}
