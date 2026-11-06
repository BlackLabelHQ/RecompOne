namespace RecompOne.UI.Animation;

public sealed class AnimatedFloat(float value = 0f, float speed = 16f)
{
    public float Value { get; private set; } = value;
    public float Speed { get; set; } = speed;
    
    public float Update(float target)
    {
        Value = Easing.Approach(Value, target, Speed, ImGui.GetIO().DeltaTime);
        return Value;
    }
    
    public void Snap(float value)
    {
        Value = value;
    }
}
