namespace RecompOne.UI.BigPicture.Session;

public class PopupPrimer(MenuInput input)
{
    private bool _primed;
    
    public void Update()
    {
        if (!PopupManager.AnyOpen)
        {
            _primed = false;
            return;
        }
        
        if (_primed) return;
        _primed = true;
        input.PrimeSelection();
    }
}
