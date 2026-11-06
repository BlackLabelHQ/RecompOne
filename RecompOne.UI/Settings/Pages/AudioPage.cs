
namespace RecompOne.UI;

using Runtime = RecompOne.Runtime.Runtime;

public static class AudioPage
{
    public static void Register()
    {
        SettingsPages.Page(SettingsPages.Audio, "settings.audio", 10).WithIcon(FontSet.Volume)
            .Add("volume", DrawVolume, 0)
            .Add("mute", DrawMute, 100);
    }

    public static void Apply()
    {
        var audio = Settings.System.Audio;
        Audio.SetMasterVolume(audio.EffectiveMaster);
        var spu = Runtime.Spu;
        if (spu == null) return;
        spu.VoiceGain = audio.Spu;
        spu.XaGain = audio.Xa;
    }

    private static void DrawVolume()
    {
        var audio = Settings.System.Audio;
        Volume("settings.audio.master", null, audio.Master, v => audio.Master = v);
        Volume("settings.audio.spu", "settings.audio.spu_hint", audio.Spu, v => audio.Spu = v);
        Volume("settings.audio.xa", "settings.audio.xa_hint", audio.Xa, v => audio.Xa = v);
    }

    private static void DrawMute()
    {
        var audio = Settings.System.Audio;
        var muted = audio.Muted;
        if (!Ui.Toggle(Localization.T("settings.audio.mute"), ref muted)) return;
        audio.Muted = muted;
        Changed();
    }

    private static void Volume(string labelKey, string? hintKey, float current, Action<float> set)
    {
        var value = current;
        if (Ui.Slider(Localization.T(labelKey), ref value, 0f, 1f, "%.2f", hintKey == null ? null : Localization.T(hintKey)))
        {
            set(Math.Clamp(value, 0f, 1f));
            Changed();
        }
    }

    private static void Changed()
    {
        Apply();
        Settings.MarkDirty();
    }
}
