using System.Numerics;

namespace RecompOne.UI.Animation;

public sealed class AnimatedColor(float speed = 14f)
{
    private bool _started;
    
    public Vector4 Value { get; private set; }
    public float Speed { get; set; } = speed;
    
    public Vector4 Update(Vector4 target)
    {
        if (!_started)
        {
            _started = true;
            Value = target;
            return Value;
        }
        
        var delta = ImGui.GetIO().DeltaTime;
        Value = new Vector4(
            Easing.Approach(Value.X, target.X, Speed, delta),
            Easing.Approach(Value.Y, target.Y, Speed, delta),
            Easing.Approach(Value.Z, target.Z, Speed, delta),
            Easing.Approach(Value.W, target.W, Speed, delta));
        return Value;
    }
}
