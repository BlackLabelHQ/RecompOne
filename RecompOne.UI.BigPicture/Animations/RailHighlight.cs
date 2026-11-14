namespace RecompOne.UI.BigPicture.Animations;

public sealed class RailHighlight
{
    private const float Speed = 20f;
    private const float Unset = -1f;
    
    private readonly AnimatedFloat _top = new(Unset, Speed);
    private readonly AnimatedColor _fill = new();
    
    public float Top(float target)
    {
        if (_top.Value < 0f) _top.Snap(target);
        return MathF.Round(_top.Update(target));
    }
    
    public Vector4 Fill(Vector4 target) => _fill.Update(target);
}
