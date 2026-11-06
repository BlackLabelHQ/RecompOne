
namespace RecompOne.UI;

public static class LanguagePage
{
    public static void Register()
    {
        SettingsPages.Page(SettingsPages.Language, "settings.language", -5)
            .Add("interface", DrawInterface, 0, "settings.language.interface");
    }

    private static void DrawInterface()
    {
        var current = Localization.CurrentCode;
        var label = current;
        foreach (var language in Localization.Languages)
            if (language.Code == current)
                label = language.Name;

        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("##interface-language", label))
        {
            foreach (var language in Localization.Languages)
                if (ImGui.Selectable(language.Name, language.Code == current))
                {
                    Localization.SetLanguage(language.Code);
                    InterfaceOptions.Language.Value = language.Code;
                }

            ImGui.EndCombo();
        }

        SettingsWidgets.Note("settings.interface.language_hint");
    }
}
