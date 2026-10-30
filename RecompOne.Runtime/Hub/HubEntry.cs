using System.Text.RegularExpressions;

namespace RecompOne.Runtime.Hub;

public enum HubInstallState
{
    Missing,
    Installed,
    Outdated,
    Conflict
}

public sealed partial class HubEntry
{
    public string Id { get; private init; } = "";
    public string Type { get; private init; } = ModHub.ModType;
    public string Name { get; private init; } = "";
    public string Description { get; private init; } = "";
    public string Repository { get; private init; } = "";
    public string Commit { get; private init; } = "";
    public string Path { get; private init; } = "";
    public string Icon { get; private init; } = "";
    public string Authors { get; private init; } = "";
    public string Warning { get; private init; } = "";
    public string[] Tags { get; private init; } = [];

    public int? Stars { get; internal set; }
    public byte[]? IconData { get; internal set; }
    public bool IconLoaded { get; internal set; }

    internal GitRemote Remote { get; private init; } = null!;

    public string RepositoryUrl => Remote.WebUrl;
    public bool HasStars => Remote.HasStars;

    public static HubEntry? Parse(string id, string text, out string? error)
    {
        error = null;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            values[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }

        string Get(string key)
        {
            return values.TryGetValue(key, out var v) ? v : "";
        }

        if (!GitRemote.TryParse(Get("repository"), out var remote))
        {
            error = "repository must be a https:// git repository url";
            return null;
        }

        var commit = Get("commit").ToLowerInvariant();
        if (!CommitHash().IsMatch(commit))
        {
            error = "commit must be a full 40 character commit hash";
            return null;
        }

        var path = Get("path").Replace('\\', '/').Trim('/');
        if (path.Split('/').Any(part => part is ".." or "."))
        {
            error = "path cannot contain . or .. segments";
            return null;
        }

        var type = Get("type").ToLowerInvariant();
        return new HubEntry
        {
            Id = id,
            Type = type.Length > 0 ? type : ModHub.ModType,
            Name = Get("name") is { Length: > 0 } name ? name : remote.Name,
            Description = Get("description"),
            Repository = Get("repository"),
            Remote = remote,
            Commit = commit,
            Path = path,
            Icon = Get("icon").Replace('\\', '/').Trim('/'),
            Authors = Get("authors"),
            Warning = Get("warning"),
            Tags = Get("tags").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        };
    }

    public static bool ValidId(string id)
    {
        return EntryId().IsMatch(id);
    }

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex CommitHash();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$")]
    private static partial Regex EntryId();
}
