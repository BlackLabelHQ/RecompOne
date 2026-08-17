namespace RecompOne.Runtime.Cdrom.Chd;

internal static class ChdMap
{
    private const int EntrySize = 12;

    public static ChdMapEntry[] Read(Stream stream, ChdHeader header)
    {
        return header.IsCompressed ? ReadCompressed(stream, header) : ReadUncompressed(stream, header);
    }

    private static ChdMapEntry[] ReadUncompressed(Stream stream, ChdHeader header)
    {
        uint count = header.HunkCount;
        var raw = new byte[count * 4];
        stream.Seek((long)header.MapOffset, SeekOrigin.Begin);
        stream.ReadExactly(raw);

        var entries = new ChdMapEntry[count];
        for (uint i = 0; i < count; i++)
        {
            ulong offset = (ulong)ChdBig.U32(raw, (int)(i * 4)) * header.HunkBytes;
            entries[i] = new ChdMapEntry(ChdCompression.None, header.HunkBytes, offset, 0);
        }
        return entries;
    }

    private static ChdMapEntry[] ReadCompressed(Stream stream, ChdHeader header)
    {
        var head = new byte[16];
        stream.Seek((long)header.MapOffset, SeekOrigin.Begin);
        stream.ReadExactly(head);

        uint mapBytes = ChdBig.U32(head, 0);
        ulong firstOffset = ChdBig.U48(head, 4);
        int lengthBits = head[12];
        int selfBits = head[13];
        int parentBits = head[14];

        var compressed = new byte[mapBytes];
        stream.Seek((long)header.MapOffset + 16, SeekOrigin.Begin);
        stream.ReadExactly(compressed);

        var reader = new ChdBitReader(compressed);
        var huffman = ChdHuffman.ImportRle(reader, 16, 8);

        uint count = header.HunkCount;
        var types = new ChdCompression[count];

        int repeat = 0;
        var last = ChdCompression.Type0;
        for (uint i = 0; i < count; i++)
        {
            if (repeat > 0)
            {
                types[i] = last;
                repeat--;
                continue;
            }

            var value = (ChdCompression)huffman.DecodeOne(reader);
            if (value == ChdCompression.RleSmall)
            {
                types[i] = last;
                repeat = 2 + huffman.DecodeOne(reader);
            }
            else if (value == ChdCompression.RleLarge)
            {
                types[i] = last;
                repeat = 2 + 16 + (huffman.DecodeOne(reader) << 4);
                repeat += huffman.DecodeOne(reader);
            }
            else
            {
                types[i] = last = value;
            }
        }

        var entries = new ChdMapEntry[count];
        ulong current = firstOffset;
        uint lastSelf = 0;
        ulong lastParent = 0;

        for (uint i = 0; i < count; i++)
        {
            var type = types[i];
            ulong offset = current;
            uint length = 0;
            ushort crc = 0;

            switch (type)
            {
                case ChdCompression.Type0:
                case ChdCompression.Type1:
                case ChdCompression.Type2:
                case ChdCompression.Type3:
                    length = reader.Read(lengthBits);
                    current += length;
                    crc = (ushort)reader.Read(16);
                    break;

                case ChdCompression.None:
                    length = header.HunkBytes;
                    current += length;
                    crc = (ushort)reader.Read(16);
                    break;

                case ChdCompression.Self:
                    lastSelf = reader.Read(selfBits);
                    offset = lastSelf;
                    break;

                case ChdCompression.Parent:
                    offset = reader.Read(parentBits);
                    lastParent = offset;
                    break;

                case ChdCompression.Self1:
                    lastSelf++;
                    goto case ChdCompression.Self0;

                case ChdCompression.Self0:
                    type = ChdCompression.Self;
                    offset = lastSelf;
                    break;

                case ChdCompression.ParentSelf:
                    type = ChdCompression.Parent;
                    lastParent = offset = (ulong)i * header.HunkBytes / header.UnitBytes;
                    break;

                case ChdCompression.Parent1:
                    lastParent += header.HunkBytes / header.UnitBytes;
                    goto case ChdCompression.Parent0;

                case ChdCompression.Parent0:
                    type = ChdCompression.Parent;
                    offset = lastParent;
                    break;
            }

            entries[i] = new ChdMapEntry(type, length, offset, crc);
        }

        return entries;
    }
}
