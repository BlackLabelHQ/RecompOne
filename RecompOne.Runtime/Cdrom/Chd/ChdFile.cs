using System.Text;

namespace RecompOne.Runtime.Cdrom.Chd;

public sealed class ChdMetadataEntry
{
    public uint Tag { get; init; }
    public byte[] Data { get; init; } = [];
    public string Text => Encoding.ASCII.GetString(Data).TrimEnd('\0');
}

public sealed class ChdFile : IDisposable
{
    public static ReadOnlySpan<byte> Magic => "MComprHD"u8;

    private const int MetadataHeaderSize = 16;
    private const int HunkCacheSize = 8;

    private readonly FileStream _stream;
    private readonly ChdHeader _header;
    private readonly ChdMapEntry[] _map;
    private readonly List<ChdMetadataEntry> _metadata = [];
    private readonly ChdCodecSet _codecs;
    private readonly object _gate = new();

    private readonly int[] _cacheIndex = new int[HunkCacheSize];
    private readonly byte[][] _cacheData = new byte[HunkCacheSize][];
    private int _cacheCursor;

    private ChdFile(FileStream stream, ChdHeader header, ChdMapEntry[] map)
    {
        _stream = stream;
        _header = header;
        _map = map;
        _codecs = new ChdCodecSet(header);
        for (int i = 0; i < HunkCacheSize; i++)
        {
            _cacheIndex[i] = -1;
            _cacheData[i] = new byte[header.HunkBytes];
        }
    }

    public static ChdFile Open(string path)
    {
        var stream = File.OpenRead(path);
        try
        {
            var raw = new byte[ChdHeader.V5Length];
            stream.ReadExactly(raw, 0, 16);
            if (!raw.AsSpan(0, 8).SequenceEqual(Magic))
                throw new InvalidDataException("not a chd file");

            uint headerLength = ChdBig.U32(raw, 8);
            uint version = ChdBig.U32(raw, 12);
            if (version != 5)
                throw new NotSupportedException($"chd version {version} is not supported, only v5 is supported currently");
            if (headerLength != ChdHeader.V5Length)
                throw new InvalidDataException($"unexpected chd v5 header length {headerLength}");

            stream.Seek(0, SeekOrigin.Begin);
            stream.ReadExactly(raw, 0, ChdHeader.V5Length);

            var header = ChdHeader.ReadV5(raw);
            var map = ChdMap.Read(stream, header);
            var chd = new ChdFile(stream, header, map);
            chd.ReadMetadata();
            return chd;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public uint HunkBytes => _header.HunkBytes;

    public uint UnitBytes => _header.UnitBytes;

    public ulong LogicalBytes => _header.LogicalBytes;

    public uint HunkCount => _header.HunkCount;

    public IReadOnlyList<ChdMetadataEntry> Metadata => _metadata;

    public byte[] ReadHunk(int index)
    {
        lock (_gate)
        {
            for (int i = 0; i < HunkCacheSize; i++)
                if (_cacheIndex[i] == index) return _cacheData[i];

            int slot = _cacheCursor;
            _cacheCursor = (_cacheCursor + 1) % HunkCacheSize;
            var dest = _cacheData[slot];
            _cacheIndex[slot] = -1;
            DecodeHunk(index, dest);
            _cacheIndex[slot] = index;
            return dest;
        }
    }

    private void DecodeHunk(int index, byte[] dest)
    {
        if (index < 0 || index >= _map.Length)
        {
            Array.Clear(dest);
            return;
        }

        var entry = _map[index];
        switch (entry.Compression)
        {
            case ChdCompression.Type0:
            case ChdCompression.Type1:
            case ChdCompression.Type2:
            case ChdCompression.Type3:
            {
                var source = ReadRaw((long)entry.Offset, (int)entry.Length);
                _codecs.Decompress((int)entry.Compression, source, dest);
                break;
            }

            case ChdCompression.None:
            {
                _stream.Seek((long)entry.Offset, SeekOrigin.Begin);
                _stream.ReadExactly(dest, 0, dest.Length);
                break;
            }

            case ChdCompression.Self:
            {
                DecodeHunk((int)entry.Offset, dest);
                break;
            }

            default:
                Array.Clear(dest);
                break;
        }
    }

    private byte[] ReadRaw(long offset, int length)
    {
        var buffer = new byte[length];
        _stream.Seek(offset, SeekOrigin.Begin);
        _stream.ReadExactly(buffer, 0, length);
        return buffer;
    }

    private void ReadMetadata()
    {
        ulong offset = _header.MetaOffset;
        var head = new byte[MetadataHeaderSize];

        while (offset != 0 && offset < (ulong)_stream.Length)
        {
            _stream.Seek((long)offset, SeekOrigin.Begin);
            _stream.ReadExactly(head, 0, MetadataHeaderSize);

            uint tag = ChdBig.U32(head, 0);
            uint length = ChdBig.U32(head, 4) & 0x00FFFFFF;
            ulong next = ChdBig.U64(head, 8);

            var data = new byte[length];
            _stream.ReadExactly(data, 0, (int)length);
            _metadata.Add(new ChdMetadataEntry { Tag = tag, Data = data });

            offset = next;
        }
    }

    public void Dispose() => _stream.Dispose();
}
