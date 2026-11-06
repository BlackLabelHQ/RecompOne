namespace RecompOne.UI.Animation;

public static class Easing
{
    private const float SnapDistance = 0.001f;
    
    public static float Approach(float current, float target, float speed, float delta)
    {
        var next = current + (target - current) * (1f - MathF.Exp(-delta * speed));
        return MathF.Abs(next - target) < SnapDistance ? target : next;
    }
}
