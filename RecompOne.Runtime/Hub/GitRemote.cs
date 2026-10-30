using System.Text.Json.Nodes;

namespace RecompOne.Runtime.Hub;

internal enum GitHost
{
    GitHub,
    GitLab,
    Gitea,
    Other
}

internal sealed record GitRemote(GitHost Host, string Server, string Project, string CloneUrl)
{
    public string WebUrl => $"https://{Server}/{Project}";
    public string Name => Project[(Project.LastIndexOf('/') + 1)..];
    public string Key => $"{Server}/{Project}".ToLowerInvariant();
    public bool HasStars => Host is GitHost.GitHub or GitHost.GitLab;
    private string GitLabApi => $"https://{Server}/api/v4/projects/{Uri.EscapeDataString(Project)}";

    public static bool TryParse(string url, out GitRemote remote)
    {
        remote = null!;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
        var project = uri.AbsolutePath.Trim('/');
        if (project.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) project = project[..^4];
        if (project.Contains("/-/") || project.Split('/').Length < 2 || project.Split('/').Any(p => p.Length == 0 || p is "." or ".."))
            return false;

        var server = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        var host = uri.Host.ToLowerInvariant() switch
        {
            "github.com" => GitHost.GitHub,
            "gitlab.com" => GitHost.GitLab,
            "codeberg.org" or "gitea.com" => GitHost.Gitea,
            var h when h.StartsWith("gitlab.") => GitHost.GitLab,
            var h when h.StartsWith("gitea.") || h.StartsWith("forgejo.") => GitHost.Gitea,
            _ => GitHost.Other
        };
        remote = new GitRemote(host, server, project, uri.GetLeftPart(UriPartial.Path).TrimEnd('/'));
        return true;
    }

    public static GitRemote FromShorthand(string repository)
    {
        return TryParse(repository, out var remote)
            ? remote
            : new GitRemote(GitHost.GitHub, "github.com", repository.Trim('/'), $"https://github.com/{repository.Trim('/')}.git");
    }

    public string? ArchiveUrl(string reference, bool branch)
    {
        var escaped = Uri.EscapeDataString(reference);
        return Host switch
        {
            GitHost.GitHub => branch
                ? $"https://codeload.github.com/{Project}/zip/refs/heads/{escaped}"
                : $"https://codeload.github.com/{Project}/zip/{escaped}",
            GitHost.GitLab => $"{GitLabApi}/repository/archive.zip?sha={escaped}",
            GitHost.Gitea => $"https://{Server}/{Project}/archive/{escaped}.zip",
            _ => null
        };
    }

    public string? RawUrl(string commit, string file)
    {
        var escaped = string.Join('/', file.Split('/').Select(Uri.EscapeDataString));
        return Host switch
        {
            GitHost.GitHub => $"https://raw.githubusercontent.com/{Project}/{commit}/{escaped}",
            GitHost.GitLab => $"{GitLabApi}/repository/files/{Uri.EscapeDataString(file)}/raw?ref={commit}",
            GitHost.Gitea => $"https://{Server}/{Project}/raw/commit/{commit}/{escaped}",
            _ => null
        };
    }

    public string? StarsUrl => Host switch
    {
        GitHost.GitHub => $"https://api.github.com/repos/{Project}",
        GitHost.GitLab => GitLabApi,
        _ => null
    };

    public int? ReadStars(JsonNode? info)
    {
        return Host switch
        {
            GitHost.GitHub => info?["stargazers_count"]?.GetValue<int>(),
            GitHost.GitLab => info?["star_count"]?.GetValue<int>(),
            _ => null
        };
    }
}
