
namespace RecompOne.UI;

public static class LanguagePage
{
    public static void Register()
    {
        SettingsPages.Page(SettingsPages.Language, "settings.language", -5).WithIcon(FontSet.Language)
            .Add("interface", DrawInterface, 0, "settings.language.interface");
    }

    private static void DrawInterface()
    {
        var languages = Localization.Languages;
        var names = languages.Select(static language => language.Name).ToArray();
        var current = Localization.CurrentCode;
        var index = languages.ToList().FindIndex(language => language.Code == current);
        var label = index >= 0 ? names[index] : current;

        ImGui.SetNextItemWidth(-1f);
        if (Ui.Combo("##interface-language", label, names, ref index))
        {
            Localization.SetLanguage(languages[index].Code);
            InterfaceOptions.Language.Value = languages[index].Code;
        }

        SettingsWidgets.Note("settings.interface.language_hint");
    }
}
