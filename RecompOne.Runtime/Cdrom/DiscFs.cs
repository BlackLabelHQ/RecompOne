namespace RecompOne.Runtime.Cdrom;

//better disc handling
public sealed class DiscFs : IDisposable
{
    private record Entry(int Lba, uint Size, bool IsDir, string Name);

    public readonly record struct DiscEntry(string Path, int Lba, uint Size, bool IsDir);

    private readonly IDiscImage _image;

    private DiscFs(IDiscImage image)
    {
        _image = image;
    }

    public static DiscFs Open(string path)
    {
        return new DiscFs(DiscImage.Open(path));
    }

    public static DiscFs FromImage(IDiscImage image)
    {
        return new DiscFs(image);
    }

    public IDiscImage Image => _image;

    public string Format => _image.Format;

    public int FirstTrack => _image.FirstTrack;

    public int LastTrack => _image.LastTrack;

    public bool HasTracks => _image.HasTracks;

    public int LeadoutLba => _image.LeadoutLba;

    public int DataSectors => _image.DataSectors;

    public IReadOnlyList<DiscTrack> Tracks => _image.Tracks;

    public bool TrackStartLba(int track, out int lba)
    {
        return _image.TrackStartLba(track, out lba);
    }

    public byte[] ReadSector(int lba)
    {
        return _image.ReadSectorData(lba, 2048);
    }

    public byte[] ReadSectorData(int lba, int size)
    {
        return _image.ReadSectorData(lba, size);
    }

    public byte[] ReadSectors(int lba, int size)
    {
        return ReadExtent(lba, size);
    }

    public byte[] ReadFile(string path)
    {
        path = path.TrimStart('/', '\\');
        var parts = path.Split('/', '\\');
        var dir = Root();
        for (var i = 0; i < parts.Length - 1; i++)
            dir = Find(dir, StripVersion(parts[i]), true);
        var file = Find(dir, StripVersion(parts[^1]), false);
        return ReadExtent(file.Lba, (int)file.Size);
    }

    public bool Exists(string path)
    {
        try
        {
            ReadFile(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public IEnumerable<DiscEntry> Enumerate()
    {
        return Walk(Root(), "");
    }

    private IEnumerable<DiscEntry> Walk(Entry dir, string prefix)
    {
        foreach (var e in Entries(dir))
        {
            var path = prefix.Length > 0 ? prefix + "/" + e.Name : e.Name;
            yield return new DiscEntry(path, e.Lba, e.Size, e.IsDir);
            if (!e.IsDir) continue;
            foreach (var child in Walk(e, path)) yield return child;
        }
    }

    public string? FindFile(string name)
    {
        return Search(Root(), "", name.ToUpperInvariant());
    }

    public bool Locate(string name, out int lba, out uint size)
    {
        lba = 0;
        size = 0;
        var entry = LocateEntry(name);
        if (entry == null) return false;
        lba = entry.Lba;
        size = entry.Size;
        return true;
    }

    private Entry? LocateEntry(string name)
    {
        name = name.TrimStart('/', '\\');
        try
        {
            var parts = name.Split('/', '\\');
            var dir = Root();
            for (var i = 0; i < parts.Length - 1; i++)
                dir = Find(dir, StripVersion(parts[i]), true);
            return Find(dir, StripVersion(parts[^1]), false);
        }
        catch (FileNotFoundException)
        {
        }

        var slash = name.LastIndexOfAny(['/', '\\']);
        var basename = slash >= 0 ? name[(slash + 1)..] : name;
        return SearchEntry(Root(), StripVersion(basename).ToUpperInvariant());
    }

    private Entry? SearchEntry(Entry dir, string name)
    {
        foreach (var e in Entries(dir))
            if (e.IsDir)
            {
                var found = SearchEntry(e, name);
                if (found != null) return found;
            }
            else if (e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return e;
            }

        return null;
    }

    private string? Search(Entry dir, string basePath, string name)
    {
        foreach (var e in Entries(dir))
            if (e.IsDir)
            {
                var p = basePath.Length > 0 ? basePath + "/" + e.Name : e.Name;
                var found = Search(e, p, name);
                if (found != null) return found;
            }
            else if (e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return basePath.Length > 0 ? basePath + "/" + e.Name : e.Name;
            }

        return null;
    }

    private Entry Root()
    {
        var pvd = ReadSector(16);
        return ParseEntry(pvd, 156);
    }

    private Entry Find(Entry dir, string name, bool wantDir)
    {
        var upper = name.ToUpperInvariant();
        foreach (var e in Entries(dir))
            if (e.IsDir == wantDir && e.Name.Equals(upper, StringComparison.OrdinalIgnoreCase))
                return e;
        throw new FileNotFoundException($"{(wantDir ? "directory" : "file")} not found: {name}");
    }

    private IEnumerable<Entry> Entries(Entry dir)
    {
        var data = ReadExtent(dir.Lba, (int)dir.Size);
        var i = 0;
        while (i < data.Length)
        {
            var len = data[i];
            if (len == 0)
            {
                i = (i / 2048 + 1) * 2048;
                continue;
            }

            var e = ParseEntry(data, i);
            if (e.Name is not ("\x00" or "\x01"))
                yield return e;
            i += len;
        }
    }

    private byte[] ReadExtent(int lba, int size)
    {
        var result = new byte[size];
        var done = 0;
        var cur = lba;
        while (done < size)
        {
            var sector = ReadSector(cur++);
            var n = Math.Min(2048, size - done);
            sector.AsSpan(0, n).CopyTo(result.AsSpan(done));
            done += n;
        }

        return result;
    }

    private static string StripVersion(string name)
    {
        var semi = name.IndexOf(';');
        return semi >= 0 ? name[..semi] : name;
    }

    private static Entry ParseEntry(byte[] data, int off)
    {
        var lba = BitConverter.ToInt32(data, off + 2);
        var size = BitConverter.ToUInt32(data, off + 10);
        var isDir = (data[off + 25] & 0x02) != 0;
        int nameLen = data[off + 32];
        var raw = System.Text.Encoding.ASCII.GetString(data, off + 33, nameLen);
        var semi = raw.IndexOf(';');
        return new Entry(lba, size, isDir, semi >= 0 ? raw[..semi] : raw);
    }

    public void Dispose()
    {
        _image.Dispose();
    }
}