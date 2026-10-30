using System.Text.Json;

namespace RecompOne.Runtime.Assets;

public sealed class PackState
{
    private const string FileName = "packs.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public List<string> Order { get; set; } = [];
    public List<string> Disabled { get; set; } = [];

    public static PackState Load(string root)
    {
        try
        {
            var path = Path.Combine(root, FileName);
            if (!File.Exists(path)) return new PackState();
            return JsonSerializer.Deserialize<PackState>(File.ReadAllText(path), Json) ?? new PackState();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[assets] cannot read {FileName}: {ex.Message}");
            return new PackState();
        }
    }

    public void Save(string root)
    {
        try
        {
            File.WriteAllText(Path.Combine(root, FileName), JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[assets] cannot write {FileName}: {ex.Message}");
        }
    }

    public bool IsEnabled(string id)
    {
        return !Disabled.Contains(id, StringComparer.OrdinalIgnoreCase);
    }

    public int IndexOf(string id)
    {
        return Order.FindIndex(o => o.Equals(id, StringComparison.OrdinalIgnoreCase));
    }
}
