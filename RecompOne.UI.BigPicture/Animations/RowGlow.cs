namespace RecompOne.UI.BigPicture.Animations;

public static class RowGlow
{
    private const float Speed = 22f;
    private const float Hovered = 0.7f;
    
    public static float Update(uint id, bool focused, bool hovered)
    {
        return AnimationStore.Update(id, focused ? 1f : hovered ? Hovered : 0f, Speed);
    }
}
