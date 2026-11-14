namespace RecompOne.UI.BigPicture.Session;

public sealed class BigPictureAskPopup : Popup
{
    protected override string TitleKey => "big_picture.ask.title";
    protected override Vector2 Size => new(460f, 0f);
    protected override bool Closable => false;
    
    protected override void DrawContent()
    {
        UiText.CenteredWrapped(Localization.T("big_picture.ask.message"));
        ImGui.Spacing();
        
        var choice = Ui.Dialog(Localization.T("big_picture.ask.enable"), Localization.T("big_picture.ask.keep"));
        if (choice == DialogChoice.None) return;
        
        BigPictureOptions.Enabled.Value = choice == DialogChoice.Confirm;
        BigPictureOptions.Asked.Value = true;
        Close();
    }
}
