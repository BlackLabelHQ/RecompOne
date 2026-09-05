namespace RecompOne.Recompiler.Psx.Compression;

public sealed class Rnc : ICompressionFormat
{
    private const int HeaderSize = 18;
    private const int Leaves = 16;

    public string Name => "rnc";

    public bool Detectable => true;

    public bool Matches(ReadOnlySpan<byte> data)
    {
        return data.Length > HeaderSize && data[0] == 'R' && data[1] == 'N' && data[2] == 'C' &&
               data[3] is 1 or 2;
    }

    public byte[] Decode(byte[] data)
    {
        if (!Matches(data)) throw new InvalidDataException("not an rnc stram");

        var method = data[3];
        var unpackedSize = (int)ReadBig(data, 4);
        var packedSize = (int)ReadBig(data, 8);
        var unpackedCrc = (ushort)(data[0x0C] << 8 | data[0x0D]);
        var packedCrc = (ushort)(data[0x0E] << 8 | data[0x0F]);

        if (HeaderSize + packedSize > data.Length)
            throw new InvalidDataException("rnc stream is shorter than its header claims");

        if (Crc(data, HeaderSize, packedSize) != packedCrc)
            throw new InvalidDataException("rnc packed crc mismatch");

        var output = method == 1
            ? UnpackOne(data, packedSize, unpackedSize)
            : UnpackTwo(data, packedSize, unpackedSize);

        if (Crc(output, 0, output.Length) != unpackedCrc)
            throw new InvalidDataException("rnc unpacked crc mismatch");

        return output;
    }

    private static readonly (int Bits, int Pattern, int Distance)[] Distances =
    [
        (1, 0b0, 0x000), (3, 0b110, 0x100), (4, 0b1000, 0x200), (4, 0b1001, 0x300),
        (5, 0b10101, 0x400), (5, 0b10111, 0x500), (5, 0b11101, 0x600), (5, 0b11111, 0x700),
        (6, 0b101000, 0x800), (6, 0b101001, 0x900), (6, 0b101100, 0xA00), (6, 0b101101, 0xB00),
        (6, 0b111000, 0xC00), (6, 0b111001, 0xD00), (6, 0b111100, 0xE00), (6, 0b111101, 0xF00)
    ];

    private static byte[] UnpackTwo(byte[] data, int packedSize, int unpackedSize)
    {
        var output = new byte[unpackedSize];
        var written = 0;
        var state = new Stream2(data, HeaderSize, HeaderSize + packedSize);

        state.Bits(2);

        while (true)
        {
            int count, distance;

            if (state.Bit() == 0)
            {
                output[written++] = state.NextByte();
                continue;
            }

            if (state.Bit() == 0)
            {
                if (state.Bit() == 0)
                {
                    count = state.Bit() == 0 ? 4 : state.Bit() == 0 ? 6 : 7;
                }
                else if (state.Bit() == 0)
                {
                    count = 5;
                }
                else if (state.Bit() == 0)
                {
                    count = 8;
                }
                else
                {
                    var literals = state.Bits(4) * 4 + 12;
                    for (var i = 0; i < literals; i++) output[written++] = state.NextByte();
                    continue;
                }

                distance = state.Distance();
            }
            else if (state.Bit() == 0)
            {
                var near = state.NextByte() + 1;
                output[written] = output[written - near];
                written++;
                output[written] = output[written - near];
                written++;
                continue;
            }
            else if (state.Bit() == 0)
            {
                count = 3;
                distance = state.Distance();
            }
            else
            {
                var extra = state.NextByte();
                if (extra == 0)
                {
                    if (state.Bit() == 0) break;
                    continue;
                }

                count = extra + 8;
                distance = state.Distance();
            }

            var back = distance + state.NextByte() + 1;
            for (var i = 0; i < count; i++, written++) output[written] = output[written - back];
        }

        return output;
    }

    private sealed class Stream2(byte[] data, int start, int end)
    {
        private int _at = start;
        private int _buffer;
        private int _left;

        public byte NextByte()
        {
            return _at < end ? data[_at++] : (byte)0;
        }

        public int Bit()
        {
            if (_left == 0)
            {
                _buffer = NextByte();
                _left = 8;
            }

            var bit = (_buffer >> 7) & 1;
            _buffer = (_buffer << 1) & 0xFF;
            _left--;
            return bit;
        }

        public int Bits(int count)
        {
            var value = 0;
            while (count-- > 0) value = (value << 1) | Bit();
            return value;
        }

        public int Distance()
        {
            var code = 0;
            for (var bits = 1; bits <= 6; bits++)
            {
                code = (code << 1) | Bit();
                foreach (var (width, pattern, distance) in Distances)
                    if (width == bits && pattern == code)
                        return distance;
            }

            throw new InvalidDataException("rnc method 2 has an unknown distance code");
        }
    }

    private static byte[] UnpackOne(byte[] data, int packedSize, int unpackedSize)
    {
        var output = new byte[unpackedSize];
        var written = 0;

        var state = new Bits(data, HeaderSize, HeaderSize + packedSize);
        var raw = new int[Leaves];
        var distance = new int[Leaves];
        var length = new int[Leaves];
        var rawCode = new int[Leaves];
        var distanceCode = new int[Leaves];
        var lengthCode = new int[Leaves];

        state.Read(2);

        while (written < unpackedSize)
        {
            ReadTable(state, raw, rawCode);
            ReadTable(state, distance, distanceCode);
            ReadTable(state, length, lengthCode);

            var chunks = (int)state.Read(16);
            while (chunks-- > 0)
            {
                var literals = (int)Decode(state, raw, rawCode);
                if (literals > 0)
                {
                    for (var i = 0; i < literals; i++) output[written++] = state.NextByte();
                    state.Resync();
                }

                if (chunks == 0) break;

                var back = (int)Decode(state, distance, distanceCode) + 1;
                var count = (int)Decode(state, length, lengthCode) + 2;
                for (var i = 0; i < count; i++, written++) output[written] = output[written - back];
            }
        }

        return output;
    }

    private static void ReadTable(Bits state, int[] depth, int[] code)
    {
        Array.Clear(depth);
        Array.Clear(code);

        var leaves = (int)state.Read(5);
        if (leaves == 0) return;
        if (leaves > Leaves) leaves = Leaves;

        for (var i = 0; i < leaves; i++) depth[i] = (int)state.Read(4);

        var next = 0;
        for (var bits = 1; bits <= 16; bits++)
        {
            for (var i = 0; i < leaves; i++)
            {
                if (depth[i] != bits) continue;
                code[i] = Mirror(next, bits);
                next++;
            }

            next <<= 1;
        }
    }

    private static uint Decode(Bits state, int[] depth, int[] code)
    {
        for (var i = 0; i < Leaves; i++)
        {
            if (depth[i] == 0) continue;
            if (code[i] != (int)(state.Peek() & ((1u << depth[i]) - 1u))) continue;

            state.Read(depth[i]);
            if (i < 2) return (uint)i;
            return state.Read(i - 1) | (1u << (i - 1));
        }

        throw new InvalidDataException("rnc stream has a code that is in no table");
    }

    private static int Mirror(int value, int bits)
    {
        var result = 0;
        for (var i = 0; i < bits; i++)
        {
            result = (result << 1) | (value & 1);
            value >>= 1;
        }

        return result;
    }

    private static uint ReadBig(byte[] data, int offset)
    {
        return (uint)(data[offset] << 24 | data[offset + 1] << 16 | data[offset + 2] << 8 | data[offset + 3]);
    }

    private static ushort Crc(byte[] data, int offset, int size)
    {
        ushort crc = 0;
        for (var i = 0; i < size; i++)
        {
            crc ^= data[offset + i];
            for (var bit = 0; bit < 8; bit++)
                crc = (ushort)((crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1);
        }

        return crc;
    }

    private sealed class Bits(byte[] data, int start, int end)
    {
        private int _at = start;
        private uint _buffer;
        private int _count;

        public uint Peek()
        {
            return _buffer;
        }

        public byte NextByte()
        {
            return _at < end ? data[_at++] : (byte)0;
        }

        public uint Read(int bits)
        {
            uint value = 0;
            uint mask = 1;
            while (bits-- > 0)
            {
                if (_count == 0)
                {
                    var low = NextByte();
                    var high = NextByte();
                    _buffer = (uint)(Ahead(1) << 24 | Ahead(0) << 16 | high << 8 | low);
                    _count = 16;
                }

                if ((_buffer & 1) != 0) value |= mask;
                _buffer >>= 1;
                mask <<= 1;
                _count--;
            }

            return value;
        }

        public void Resync()
        {
            var window = (uint)(Ahead(2) << 16 | Ahead(1) << 8 | Ahead(0));
            _buffer = (window << _count) | (_buffer & ((1u << _count) - 1u));
        }

        private int Ahead(int offset)
        {
            var index = _at + offset;
            return index < end ? data[index] : 0;
        }
    }
}
