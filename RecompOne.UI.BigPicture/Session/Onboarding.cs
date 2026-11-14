namespace RecompOne.UI.BigPicture.Session;

public static class Onboarding
{
    public static float HintDuration { get; set; } = 8f;
    
    public static void FirstBoot()
    {
        if (!Settings.FirstBoot) return;
        BigPictureOptions.Enabled.Value = InputManager.IsConnected;
        BigPictureOptions.Asked.Value = false;
    }
    
    public static void AskOnce()
    {
        if (BigPictureOptions.Asked || PopupManager.AnyOpen) return;
        PopupManager.Open<BigPictureAskPopup>();
    }
    
    public static void ShowMenuHint(ShellSession session)
    {
        if (session.IsOpen || PopupManager.AnyOpen || BigPictureOptions.HintSeen) return;
        BigPictureOptions.HintSeen.Value = true;
        ToastNotifications.ShowText(Localization.T("big_picture.hint.title"), string.Format(Localization.T("big_picture.hint.message"), ButtonGlyphs.MenuCombo), duration: HintDuration);
    }
}
