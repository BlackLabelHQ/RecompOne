namespace RecompOne.UI.BigPicture.Components;

public class HintChannel
{
    public string? Current { get; private set; }
    
    public void Clear()
    {
        Current = null;
    }
    
    public void Show(string? text)
    {
        if (text != null) Current = text;
    }
}
