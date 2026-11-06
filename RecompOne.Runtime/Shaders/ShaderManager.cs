using System.Diagnostics;
using RecompOne.Runtime.Config;
using RecompOne.Runtime.Hle;

namespace RecompOne.Runtime.Shaders;

//this is not sotn specific this works with any recomp powered by recompone in theory

public static class ShaderManager
{
    private const string Prefix = "mods.shaders.";
    
    public const string DefaultRepository = "https://github.com/libretro/slang-shaders";
    
    private static bool _loaded;
    
    public static ShaderLibrary Library { get; } = new("shaders");
    public static ShaderRepository Repository { get; } = new(Library);
    public static string Selected { get; private set; } = "";
    public static string RepositoryUrl => Settings.Get(Prefix + "repository", DefaultRepository);
    public static string RepositoryBranch => Settings.Get(Prefix + "branch", "");
    
    public static bool NativeInput
    {
        get => PostFx.NativeInput;
        set
        {
            PostFx.NativeInput = value;
            Settings.Set(Prefix + "native", value);
        }
    }
    
    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        
        Repository.Configure(RepositoryUrl, RepositoryBranch);
        PostFx.NativeInput = Settings.Get(Prefix + "native", true);
        Library.Refresh();
        Apply(Settings.Get(Prefix + "selected", ""));
    }
    
    public static void ConfigureRepository(string url, string branch)
    {
        url = url.Trim();
        branch = branch.Trim();
        Settings.Set(Prefix + "repository", url);
        Settings.Set(Prefix + "branch", branch);
        Repository.Configure(url, branch);
    }
    
    public static void Apply(string relative)
    {
        Selected = relative;
        Settings.Set(Prefix + "selected", relative);
        
        if (string.IsNullOrEmpty(relative) || !File.Exists(Library.FullPath(relative)))
        {
            PostFx.Clear();
            return;
        }
        
        if (PostFx.Load(Library.FullPath(relative))) PostFx.SetParams(SavedParams(relative));
        else Console.Error.WriteLine($"[Shaders] {relative}: {PostFx.Error}");
    }
    
    public static void Reload()
    {
        Library.Refresh();
        Apply(Selected);
    }
    
    public static void SetParam(SlangParameter parameter, float value)
    {
        PostFx.SetParam(parameter.Name, value);
        var saved = SavedParams(Selected);
        if (value == parameter.Default) saved.Remove(parameter.Name);
        else saved[parameter.Name] = value;
        SaveParams(saved);
    }
    
    public static void ResetParams()
    {
        PostFx.ResetParams();
        Settings.Remove(ParamsKey(Selected));
    }
    
    public static void Remove(string relative)
    {
        Settings.Remove(ParamsKey(relative));
        Library.Remove(relative);
        if (relative == Selected) Apply("");
    }
    
    public static void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(Library.Root);
            Process.Start(new ProcessStartInfo(Library.Root) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[Shaders] fail to open the shaders folder: {e.Message}");
        }
    }
    
    private static Dictionary<string, float> SavedParams(string relative)
    {
        return Settings.Get(ParamsKey(relative), new Dictionary<string, float>(StringComparer.Ordinal));
    }
    
    private static void SaveParams(Dictionary<string, float> values)
    {
        if (values.Count == 0) Settings.Remove(ParamsKey(Selected));
        else Settings.Set(ParamsKey(Selected), values);
    }
    
    private static string ParamsKey(string relative)
    {
        return Prefix + "params." + relative;
    }
}
