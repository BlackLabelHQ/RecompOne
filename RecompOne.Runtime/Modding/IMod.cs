namespace RecompOne.Runtime.Modding;

public interface IMod
{
    /// <summary>called once after the mod assembly is loaded</summary>
    void OnLoad();

    /// <summary>called when the mod is unloaded.</summary>
    void OnUnload()
    {
    }

    /// <summary>optional settings ui shown in the mods panel, override to expose options (ImmGui calls are fine here)</summary>
    void DrawSettings()
    {
    }
}