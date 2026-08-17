namespace RecompOne.Runtime.Cdrom.Chd;

//this uses raw flac stream?
internal static class ChdFlac
{
    private const int Channels = 2;
    private const int BitsPerSample = 16;

    public static int Decode(byte[] src, int srcOffset, byte[] dst, int dstOffset, int length)
    {
        var reader = new BitReader(src, srcOffset);
        int written = 0;

        var left = new int[65536];
        var right = new int[65536];

        while (written < length)
        {
            int blockSize = ReadFrame(reader, left, right);
            for (int i = 0; i < blockSize && written < length; i++)
            {
                dst[dstOffset + written++] = (byte)(left[i] >> 8);
                if (written < length) dst[dstOffset + written++] = (byte)left[i];
                if (written < length) dst[dstOffset + written++] = (byte)(right[i] >> 8);
                if (written < length) dst[dstOffset + written++] = (byte)right[i];
            }
            reader.AlignToByte();
            reader.Skip(16);
        }

        return reader.BytePosition - srcOffset;
    }

    //rdd a
    private static int ReadFrame(BitReader reader, int[] left, int[] right)
    {
        uint sync = reader.Read(14);
        if (sync != 0x3FFE) throw new InvalidDataException("flac sync lost");

        reader.Skip(1);
        int blockingStrategy = (int)reader.Read(1);

        int blockSizeCode = (int)reader.Read(4);
        int sampleRateCode = (int)reader.Read(4);
        int channelAssignment = (int)reader.Read(4);
        reader.Skip(3);
        reader.Skip(1);

        ReadUtf8(reader, blockingStrategy != 0);

        int blockSize = blockSizeCode switch
        {
            1 => 192,
            >= 2 and <= 5 => 576 << (blockSizeCode - 2),
            6 => (int)reader.Read(8) + 1,
            7 => (int)reader.Read(16) + 1,
            >= 8 and <= 15 => 256 << (blockSizeCode - 8),
            _ => throw new InvalidDataException("flac reserved block size"),
        };

        if (sampleRateCode == 12) reader.Skip(8);
        else if (sampleRateCode is 13 or 14) reader.Skip(16);

        reader.Skip(8);

        int leftBits = BitsPerSample + (channelAssignment == 9 ? 1 : 0);
        int rightBits = BitsPerSample + (channelAssignment is 8 or 10 ? 1 : 0);

        ReadSubframe(reader, left, blockSize, leftBits);
        ReadSubframe(reader, right, blockSize, rightBits);

        switch (channelAssignment)
        {
            case 8:
                for (int i = 0; i < blockSize; i++) right[i] = left[i] - right[i];
                break;
            case 9:
                for (int i = 0; i < blockSize; i++) left[i] += right[i];
                break;
            case 10:
                for (int i = 0; i < blockSize; i++)
                {
                    int side = right[i];
                    int mid = (left[i] << 1) | (side & 1);
                    left[i] = (mid + side) >> 1;
                    right[i] = (mid - side) >> 1;
                }
                break;
        }

        return blockSize;
    }

    private static void ReadSubframe(BitReader reader, int[] output, int blockSize, int bits)
    {
        reader.Skip(1);
        int type = (int)reader.Read(6);

        int wasted = 0;
        if (reader.Read(1) != 0)
        {
            wasted = 1;
            while (reader.Read(1) == 0) wasted++;
        }
        bits -= wasted;

        if (type == 0)
        {
            int value = reader.ReadSigned(bits);
            for (int i = 0; i < blockSize; i++) output[i] = value;
        }
        else if (type == 1)
        {
            for (int i = 0; i < blockSize; i++) output[i] = reader.ReadSigned(bits);
        }
        else if (type is >= 8 and <= 12)
        {
            int order = type - 8;
            for (int i = 0; i < order; i++) output[i] = reader.ReadSigned(bits);
            ReadResidual(reader, output, blockSize, order);
            RestoreFixed(output, blockSize, order);
        }
        else if (type >= 32)
        {
            int order = type - 31;
            for (int i = 0; i < order; i++) output[i] = reader.ReadSigned(bits);

            int precision = (int)reader.Read(4) + 1;
            int shift = reader.ReadSigned(5);
            var coefficients = new int[order];
            for (int i = 0; i < order; i++) coefficients[i] = reader.ReadSigned(precision);

            ReadResidual(reader, output, blockSize, order);
            RestoreLpc(output, blockSize, order, coefficients, shift);
        }
        else
        {
            throw new InvalidDataException("flac reserved subframe type err");
        }

        if (wasted > 0)
            for (int i = 0; i < blockSize; i++) output[i] <<= wasted;
    }

    private static void ReadResidual(BitReader reader, int[] output, int blockSize, int order)
    {
        int method = (int)reader.Read(2);
        if (method > 1) throw new InvalidDataException("flac reserved residual method err");

        int paramBits = method == 0 ? 4 : 5;
        int escape = method == 0 ? 0x0F : 0x1F;

        int partitionOrder = (int)reader.Read(4);
        int partitions = 1 << partitionOrder;
        int partitionSamples = blockSize >> partitionOrder;

        int index = order;
        for (int partition = 0; partition < partitions; partition++)
        {
            int count = partition == 0 ? partitionSamples - order : partitionSamples;
            int parameter = (int)reader.Read(paramBits);

            if (parameter == escape)
            {
                int raw = (int)reader.Read(5);
                for (int i = 0; i < count; i++)
                    output[index++] = raw == 0 ? 0 : reader.ReadSigned(raw);
            }
            else
            {
                for (int i = 0; i < count; i++)
                    output[index++] = reader.ReadRice(parameter);
            }
        }
    }

    private static void RestoreFixed(int[] data, int blockSize, int order)
    {
        switch (order)
        {
            case 0:
                break;
            case 1:
                for (int i = 1; i < blockSize; i++) data[i] += data[i - 1];
                break;
            case 2:
                for (int i = 2; i < blockSize; i++) data[i] += 2 * data[i - 1] - data[i - 2];
                break;
            case 3:
                for (int i = 3; i < blockSize; i++)
                    data[i] += 3 * data[i - 1] - 3 * data[i - 2] + data[i - 3];
                break;
            case 4:
                for (int i = 4; i < blockSize; i++)
                    data[i] += 4 * data[i - 1] - 6 * data[i - 2] + 4 * data[i - 3] - data[i - 4];
                break;
        }
    }

    private static void RestoreLpc(int[] data, int blockSize, int order, int[] coefficients, int shift)
    {
        for (int i = order; i < blockSize; i++)
        {
            long sum = 0;
            for (int j = 0; j < order; j++)
                sum += (long)coefficients[j] * data[i - 1 - j];
            data[i] += (int)(sum >> shift);
        }
    }

    private static void ReadUtf8(BitReader reader, bool wide)
    {
        uint first = reader.Read(8);
        int extra = 0;
        if ((first & 0x80) != 0)
        {
            byte mask = 0x40;
            while ((first & mask) != 0)
            {
                extra++;
                mask >>= 1;
            }
        }
        for (int i = 0; i < extra; i++) reader.Skip(8);
        if (wide && extra == 0) { }
    }

    private sealed class BitReader
    {
        private readonly byte[] _data;
        private int _position;
        private ulong _accumulator;
        private int _available;

        public BitReader(byte[] data, int offset)
        {
            _data = data;
            _position = offset;
        }

        public int BytePosition => _position - (_available >> 3);

        public uint Read(int count)
        {
            if (count == 0) return 0;
            Fill(count);
            uint value = (uint)((_accumulator >> (_available - count)) & ((1UL << count) - 1));
            _available -= count;
            _accumulator &= _available >= 64 ? ulong.MaxValue : (1UL << _available) - 1;
            return value;
        }

        public int ReadSigned(int count)
        {
            if (count == 0) return 0;
            uint raw = Read(count);
            int sign = 1 << (count - 1);
            return (int)raw >= sign ? (int)raw - (sign << 1) : (int)raw;
        }

        public int ReadRice(int parameter)
        {
            int quotient = 0;
            while (Read(1) == 0) quotient++;
            int value = (quotient << parameter) | (parameter > 0 ? (int)Read(parameter) : 0);
            return (value & 1) != 0 ? -((value >> 1) + 1) : value >> 1;
        }

        public void Skip(int count)
        {
            while (count > 32)
            {
                Read(32);
                count -= 32;
            }
            Read(count);
        }

        public void AlignToByte()
        {
            int extra = _available & 7;
            if (extra != 0) Read(extra);
        }

        private void Fill(int count)
        {
            while (_available < count)
            {
                byte next = _position < _data.Length ? _data[_position++] : (byte)0;
                _accumulator = (_accumulator << 8) | next;
                _available += 8;
            }
        }
    }
}
