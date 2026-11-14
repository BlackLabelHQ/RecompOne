namespace RecompOne.UI.BigPicture.Animations;

public sealed class ShellFade
{
    private const float Speed = 12f;
    
    private readonly AnimatedFloat _alpha = new(0f, Speed);
    
    public void Restart() => _alpha.Snap(0f);
    public float Update() => _alpha.Update(1f);
}
