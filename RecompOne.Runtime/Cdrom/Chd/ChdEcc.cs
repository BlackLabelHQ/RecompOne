namespace RecompOne.Runtime.Cdrom.Chd;

internal static class ChdEcc
{
    private const int SyncBytes = 12;
    private const int ModeOffset = 0x00F;
    private const int POffset = 0x81C;
    private const int PNumBytes = 86;
    private const int PComponents = 24;
    private const int QOffset = POffset + 2 * PNumBytes;
    private const int QNumBytes = 52;
    private const int QComponents = 43;

    private const int Polynomial = 0x11D;

    private static readonly byte[] Low = BuildLow();
    private static readonly byte[] High = BuildHigh();
    private static readonly ushort[,] POffsets = BuildOffsets(PNumBytes, PComponents, 43, 1);
    private static readonly ushort[,] QOffsets = BuildOffsets(QNumBytes, QComponents, 44, 43);

    public static void Generate(byte[] sector, int start)
    {
        for (int i = 0; i < PNumBytes; i++)
            Compute(sector, start, POffsets, i, PComponents, start + POffset + i, start + POffset + PNumBytes + i);

        for (int i = 0; i < QNumBytes; i++)
            Compute(sector, start, QOffsets, i, QComponents, start + QOffset + i, start + QOffset + QNumBytes + i);
    }

    private static void Compute(byte[] sector, int start, ushort[,] offsets, int row, int components, int dest1, int dest2)
    {
        byte value1 = 0;
        byte value2 = 0;

        for (int i = 0; i < components; i++)
        {
            byte source = SourceByte(sector, start, offsets[row, i]);
            value1 ^= source;
            value2 ^= source;
            value1 = Low[value1];
        }

        value1 = High[Low[value1] ^ value2];
        value2 ^= value1;

        sector[dest1] = value1;
        sector[dest2] = value2;
    }

    private static byte SourceByte(byte[] sector, int start, int offset)
    {
        if (sector[start + ModeOffset] == 2 && offset < 4) return 0;
        return sector[start + SyncBytes + offset];
    }

    private static byte[] BuildLow()
    {
        var table = new byte[256];
        for (int i = 0; i < 256; i++)
            table[i] = (byte)((i << 1) ^ ((i & 0x80) != 0 ? Polynomial & 0xFF : 0));
        return table;
    }

    private static byte[] BuildHigh()
    {
        byte inverse = 0;
        for (int x = 1; x < 256; x++)
            if (Multiply(3, (byte)x) == 1) { inverse = (byte)x; break; }

        var table = new byte[256];
        for (int i = 0; i < 256; i++)
            table[i] = Multiply((byte)i, inverse);
        return table;
    }

    private static byte Multiply(byte a, byte b) //this is a bit convoluted
    {
        int result = 0;
        int value = a;
        while (b != 0)
        {
            if ((b & 1) != 0) result ^= value; 
            value <<= 1;
            if ((value & 0x100) != 0) value ^= Polynomial;
            b >>= 1;
        }
        return (byte)result;
    }

    private static ushort[,] BuildOffsets(int rows, int components, int step, int rowStep)
    {
        var table = new ushort[rows, components];
        int modulus = 43 * 26;
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < components; j++)
            {
                int index = rowStep == 1 ? (i / 2) + (j * step) : ((i / 2) * rowStep + (j * step)) % modulus;
                table[i, j] = (ushort)(index * 2 + (i & 1));
            }
        }
        return table;
    }
}
