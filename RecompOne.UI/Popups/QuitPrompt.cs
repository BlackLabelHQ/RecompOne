namespace RecompOne.UI;

public static class QuitPrompt
{
    public static void Ask()
    {
        ConfirmPopup.Ask(Localization.T("quit.title"), Localization.T("quit.message"), Localization.T("quit.confirm"), static () => Environment.Exit(0));
    }
}
