namespace RecompOne.Runtime.Assets;

public static class AssetApi //so mods can access and swap assets
{
    public static AssetReplacerManager Manager => AssetReplacerManager.Instance;

    public static bool Enabled
    {
        get => Manager.Enabled;
        set => Manager.Enabled = value;
    }

    public static string GameId => Manager.GameId;

    public static string PacksRoot => Manager.Root;

    public static IReadOnlyList<AssetPack> Packs => Manager.Packs;

    public static void ReloadPacks()
    {
        Manager.Reload();
    }

    public static XaEntry ReplaceXa(int fileNumber, int channel, int startLba, string audioPath,
        Action<XaOptions>? configure = null, string owner = "mod")
    {
        var options = new XaOptions();
        configure?.Invoke(options);
        return Manager.RegisterXa((byte)fileNumber, (byte)channel, startLba, audioPath, options, owner);
    }

    public static XaEntry ReplaceXa(int fileNumber, int channel, int startLba, string name, Func<byte[]?> open,
        Action<XaOptions>? configure = null, string owner = "mod")
    {
        var options = new XaOptions();
        configure?.Invoke(options);
        return Manager.RegisterXa((byte)fileNumber, (byte)channel, startLba, name, open, options, owner);
    }

    public static XaEntry ReplaceXaChannel(int fileNumber, int channel, string audioPath,
        Action<XaOptions>? configure = null, string owner = "mod")
    {
        return ReplaceXa(fileNumber, channel, 0, audioPath, configure, owner);
    }

    public static void Remove(XaEntry entry)
    {
        Manager.UnregisterXa(entry);
    }

    public static void RemoveAll(string owner)
    {
        Manager.ClearRuntimeRegistrations(owner);
    }

    public static IReadOnlyList<XaEntry> XaEntries => Manager.XaEntries;

    public static bool XaReplacementActive => Xa.XaRouter.Active;

    public static string? XaReplacementName => Xa.XaRouter.ActiveName;

    public static AssetStats Stats => Manager.Stats;
}