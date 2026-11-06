namespace RecompOne.UI.Animation;

//animation providers
public static class AnimationStore
{
    private static readonly Dictionary<uint, float> _values = [];
    
    public static float Update(uint id, float target, float speed = 16f)
    {
        _values.TryGetValue(id, out var value);
        value = Easing.Approach(value, target, speed, ImGui.GetIO().DeltaTime);
        _values[id] = value;
        return value;
    }
}
