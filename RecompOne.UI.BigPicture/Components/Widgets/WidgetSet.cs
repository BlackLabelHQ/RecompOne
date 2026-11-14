namespace RecompOne.UI.BigPicture.Components.Widgets;

public class WidgetSet
{
    public WidgetSet(MenuInput input, HintChannel hints)
    {
        Input = input;
        Hints = hints;
        Row = new RowWidget(this);
        Switch = new SwitchWidget(this);
        Pill = new PillWidget(this);
        Picker = new PickerWidget(this);
        Card = new CardWidget(this);
        Swatch = new SwatchWidget(this);
        Color = new ColorWidget(this);
    }
    
    public MenuInput Input { get; }
    
    public HintChannel Hints { get; }
    
    public RowWidget Row { get; set; }
    
    public SwitchWidget Switch { get; set; }
    
    public PillWidget Pill { get; set; }
    
    public PickerWidget Picker { get; set; }
    
    public CardWidget Card { get; set; }
    
    public SwatchWidget Swatch { get; set; }
    
    public ColorWidget Color { get; set; }
}
