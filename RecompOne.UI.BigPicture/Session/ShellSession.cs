namespace RecompOne.UI.BigPicture.Session;

public class ShellSession(MenuInput input)
{
    private bool _waitRelease;
    
    public bool IsOpen { get; private set; }
    
    public event Action? Opened;
    
    public void Open()
    {
        IsOpen = true;
        InputManager.GameInputEnabled = false;
        Opened?.Invoke();
    }
    
    public void Resume()
    {
        IsOpen = false;
        _waitRelease = true;
    }
    
    public void Close()
    {
        IsOpen = false;
        _waitRelease = false;
        InputManager.GameInputEnabled = true;
    }
    
    public void Toggle()
    {
        if (IsOpen) Resume();
        else Open();
    }
    
    public void Update(in MenuPadState pad, bool interacting)
    {
        if (_waitRelease && input.Released(pad)) _waitRelease = false;
        InputManager.GameInputEnabled = !interacting && !_waitRelease;
    }
}
