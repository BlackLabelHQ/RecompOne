namespace RecompOne.UI.BigPicture.Animations;

public static class SwitchSlide
{
    private const float Speed = 18f;
    public static float Update(uint id, bool on)
    {
        return AnimationStore.Update(id, on ? 1f : 0f, Speed);
    }
}
