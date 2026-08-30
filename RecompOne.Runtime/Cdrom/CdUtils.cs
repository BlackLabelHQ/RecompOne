namespace RecompOne.Runtime.Cdrom;

public static class CdUtils
{
    public static string ExtractFileName(string rawPath)
    {
        var colon = rawPath.IndexOf(':');
        var path = colon >= 0 ? rawPath[(colon + 1)..] : rawPath;
        var semi = path.IndexOf(';');
        if (semi >= 0) path = path[..semi];
        path = path.Replace('\\', '/');
        var slash = path.LastIndexOf('/');
        return slash >= 0 ? path[(slash + 1)..] : path;
    }


    public static string OverlayName(string fileName)
    {
        return Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
    }
}