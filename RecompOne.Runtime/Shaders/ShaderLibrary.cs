namespace RecompOne.Runtime.Shaders;

public sealed record InstalledPreset(string Relative, string Name, string Folder);

public sealed class ShaderLibrary
{
    public readonly string Root;
    private IReadOnlyList<InstalledPreset> presets = [];

    public ShaderLibrary(string root)
    {
        Root = Path.GetFullPath(root);
    }

    public IReadOnlyList<InstalledPreset> Presets => presets;

    public int Version { get; private set; }

    public void Refresh()
    {
        var found = new List<InstalledPreset>();
        if (Directory.Exists(Root))
            foreach (var file in Directory.EnumerateFiles(Root, "*.slangp", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(Root, file).Replace('\\', '/');
                if (relative.Split('/').Any(part => part.StartsWith('.'))) continue;
                var folder = Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? "";
                found.Add(new InstalledPreset(relative, Path.GetFileNameWithoutExtension(file), folder));
            }

        found.Sort((a, b) =>
        {
            var byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : string.Compare(a.Folder, b.Folder, StringComparison.OrdinalIgnoreCase);
        });

        presets = found;
        Version++;
    }

    public string FullPath(string relative)
    {
        return Path.GetFullPath(Path.Combine(Root, relative));
    }

    public bool Contains(string relative)
    {
        return File.Exists(FullPath(relative));
    }

    public void Remove(string relative)
    {
        try
        {
            var path = FullPath(relative);
            if (path.StartsWith(Root, StringComparison.Ordinal) && File.Exists(path)) File.Delete(path);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[Shaders] failed to remove {relative}: {e.Message}");
        }

        Refresh();
    }
}
