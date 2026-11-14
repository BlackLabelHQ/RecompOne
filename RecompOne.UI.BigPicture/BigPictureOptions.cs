namespace RecompOne.UI.BigPicture;

public static class BigPictureOptions
{
    public static readonly Option<bool> Enabled = Options.Bool("interface.big_picture");
    public static readonly Option<bool> Asked = Options.Bool("interface.big_picture_asked", true);
    public static readonly Option<bool> HintSeen = Options.Bool("interface.big_picture_hint_seen");
}
