using System.Text.Json.Serialization;

namespace RecompOne.Runtime.Modding;

public sealed class ModInfo
{
    /// <summary>Unique id of the mod</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>Display name of the mod</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Version string of the mod</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    /// <summary>Author of the mod</summary>
    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    [JsonPropertyName("description")] public string Description { get; set; } = "";

    /// <summary>Ids of mods that must load before this one</summary>
    [JsonPropertyName("dependencies")]
    public string[] Dependencies { get; set; } = [];

    /// <summary>Folder or zip this mod was loaded from</summary>
    [JsonIgnore]
    public string SourcePath { get; internal set; } = "";
}