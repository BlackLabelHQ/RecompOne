using System.Globalization;

namespace RecompOne.Runtime.Config;

public class PanelState
{
    public bool Open { get; set; }
}

public class ViewConfig
{
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, PanelState> Panels { get; set; } = [];

    public bool GetBool(string key, bool fallback = false)
    {
        return Values.TryGetValue(key, out var v) && bool.TryParse(v, out var b) ? b : fallback;
    }

    public void SetBool(string key, bool value)
    {
        Values[key] = value.ToString();
    }

    public int GetInt(string key, int fallback = 0)
    {
        return Values.TryGetValue(key, out var v) &&
               int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
            ? i
            : fallback;
    }

    public void SetInt(string key, int value)
    {
        Values[key] = value.ToString(CultureInfo.InvariantCulture);
    }

    public float GetFloat(string key, float fallback = 0f)
    {
        return Values.TryGetValue(key, out var v) &&
               float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
            ? f
            : fallback;
    }

    public void SetFloat(string key, float value)
    {
        Values[key] = value.ToString(CultureInfo.InvariantCulture);
    }

    public string GetString(string key, string fallback = "")
    {
        return Values.TryGetValue(key, out var v) ? v : fallback;
    }

    public void SetString(string key, string value)
    {
        Values[key] = value;
    }

    public bool HideTopBar
    {
        get => GetBool("HideTopBar");
        set => SetBool("HideTopBar", value);
    }

    //todo: put top right
    public bool ShowFps
    {
        get => GetBool("ShowFps");
        set => SetBool("ShowFps", value);
    }

    public bool AutoHideMenuBar
    {
        get => GetBool("AutoHideMenuBar");
        set => SetBool("AutoHideMenuBar", value);
    }

    public bool Fullscreen
    {
        get => GetBool("Fullscreen");
        set => SetBool("Fullscreen", value);
    }

    public int RenderScale
    {
        get
        {
            var v = (int)GetFloat("RenderScale", 4f);
            return v < 1 ? 1 : v > 8 ? 8 : v;
        }
        set => SetFloat("RenderScale", value < 1 ? 1 : value > 8 ? 8 : value);
    }

    public bool VSync
    {
        get => GetBool("VSync");
        set => SetBool("VSync", value);
    }

    public string GpuBackend
    {
        get => GetString("GpuBackend", "auto");
        set => SetString("GpuBackend", value);
    }

    public string Language
    {
        get => GetString("Language");
        set => SetString("Language", value);
    }

    public string Accent
    {
        get => GetString("Accent");
        set => SetString("Accent", value);
    }

    public string Background
    {
        get => GetString("Background");
        set => SetString("Background", value);
    }

    public float UiScale
    {
        get => Math.Clamp(GetFloat("UiScale", 1f), 0.5f, 3f);
        set => SetFloat("UiScale", Math.Clamp(value, 0.5f, 3f));
    }
}