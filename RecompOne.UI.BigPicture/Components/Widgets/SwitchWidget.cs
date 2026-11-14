namespace RecompOne.UI.BigPicture.Components.Widgets;

public class SwitchWidget(WidgetSet widgets) : Widget(widgets)
{
    private const int KnobSegments = 24;
    
    public virtual Vector2 Size => Px(46f, 24f);
    
    public virtual float KnobInset => Px(3f);
    
    public virtual void Draw(ImDrawListPtr draw, Vector2 topLeft, uint id, bool on)
    {
        var track = Size;
        var slide = SwitchSlide.Update(id, on);
        draw.AddRectFilled(topLeft, topLeft + track, Rgba(Vector4.Lerp(Palette.Raised, Theme.Accent, slide)), track.Y * 0.5f);
        var knob = topLeft.X + track.Y * 0.5f + (track.X - track.Y) * slide;
        draw.AddCircleFilled(new Vector2(knob, topLeft.Y + track.Y * 0.5f), track.Y * 0.5f - KnobInset, Palette.White, KnobSegments);
    }
}
