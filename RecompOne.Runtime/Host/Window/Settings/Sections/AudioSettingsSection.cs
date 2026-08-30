using ImGuiNET;
using RecompOne.Runtime.Config;

namespace RecompOne.Runtime.Host.Window;

internal sealed class AudioSettingsSection : ISettingsSection
{
    public string Id => "audio";
    public string TitleKey => "settings.audio";
    public int Order => 10;

    public static void Apply()
    {
        var game = ConfigManager.Game;
        Audio.SetMasterVolume(game.Muted ? 0f : game.MasterVolume);
        var spu = Runtime.Spu;
        if (spu == null) return;
        spu.VoiceGain = game.SpuVolume;
        spu.XaGain = game.XaVolume;
    }

    public void Draw()
    {
        var game = ConfigManager.Game;

        var volume = game.MasterVolume;
        if (ImGui.SliderFloat(Localization.T("settings.audio.master"), ref volume, 0f, 1f, "%.2f"))
        {
            game.MasterVolume = Math.Clamp(volume, 0f, 1f);
            Apply();
            ConfigManager.SaveGame();
        }

        var spu = game.SpuVolume;
        if (ImGui.SliderFloat(Localization.T("settings.audio.spu"), ref spu, 0f, 1f, "%.2f"))
        {
            game.SpuVolume = Math.Clamp(spu, 0f, 1f);
            Apply();
            ConfigManager.SaveGame();
        }

        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Localization.T("settings.audio.spu_hint"));

        var xa = game.XaVolume;
        if (ImGui.SliderFloat(Localization.T("settings.audio.xa"), ref xa, 0f, 1f, "%.2f"))
        {
            game.XaVolume = Math.Clamp(xa, 0f, 1f);
            Apply();
            ConfigManager.SaveGame();
        }

        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Localization.T("settings.audio.xa_hint"));

        ImGui.Separator();

        var muted = game.Muted;
        if (ImGui.Checkbox(Localization.T("settings.audio.mute"), ref muted))
        {
            game.Muted = muted;
            Apply();
            ConfigManager.SaveGame();
        }
    }
}