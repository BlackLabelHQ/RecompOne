namespace RecompOne.UI.BigPicture.Style;

public static class Metrics
{
    public static float Rounding => Px(10f);
    
    public static float Inset => Px(14f);
    
    public static float PanelPadding => Px(16f);
    
    public static float ContentPadding => Px(28f);
    
    public static float Footer => Px(48f);
    
    public static float IconSize => Px(22f);
    
    public static float FrameRounding => Px(8f);
    
    public static float GrabRounding => Px(6f);
    
    public static float PopupRounding => Px(16f);
    
    public static float RailWidth(float available) => MathF.Round(MathF.Min(available * 0.27f, Px(250f)));
    
    public static float PageListWidth(float available) => MathF.Round(MathF.Min(available * 0.32f, Px(230f)));
}
