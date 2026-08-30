namespace RecompOne.Runtime.Cdrom.Chd;

internal sealed class ChdHuffman
{
    private const int MaxCodeLength = 32;

    private readonly int _numCodes;
    private readonly int _maxBits;
    private readonly byte[] _lengths;
    private readonly int[] _firstCode = new int[MaxCodeLength + 1];
    private readonly int[] _firstIndex = new int[MaxCodeLength + 1];
    private readonly int[] _counts = new int[MaxCodeLength + 1];
    private int[] _symbols = [];

    private ChdHuffman(int numCodes, int maxBits)
    {
        _numCodes = numCodes;
        _maxBits = maxBits;
        _lengths = new byte[numCodes];
    }

    public static ChdHuffman ImportRle(ChdBitReader reader, int numCodes, int maxBits)
    {
        var huffman = new ChdHuffman(numCodes, maxBits);
        huffman.ReadRleLengths(reader);
        huffman.AssignCanonicalCodes();
        return huffman;
    }

    public int DecodeOne(ChdBitReader reader)
    {
        var code = 0;
        for (var length = 1; length <= _maxBits; length++)
        {
            code = (code << 1) | (int)reader.Read(1);
            var offset = code - _firstCode[length];
            if (_counts[length] > 0 && offset >= 0 && offset < _counts[length])
                return _symbols[_firstIndex[length] + offset];
        }

        throw new InvalidDataException("chd huffman code not found");
    }

    private void ReadRleLengths(ChdBitReader reader)
    {
        var numBits = _maxBits >= 16 ? 5 : _maxBits >= 8 ? 4 : 3;

        for (var current = 0; current < _numCodes;)
        {
            var nodeBits = (int)reader.Read(numBits);
            if (nodeBits != 1)
            {
                _lengths[current++] = (byte)nodeBits;
                continue;
            }

            nodeBits = (int)reader.Read(numBits);
            if (nodeBits == 1)
            {
                _lengths[current++] = (byte)nodeBits;
                continue;
            }

            var repeat = (int)reader.Read(numBits) + 3;
            if (repeat + current > _numCodes)
                throw new InvalidDataException("chd huffman rle has overflown");
            while (repeat-- > 0)
                _lengths[current++] = (byte)nodeBits;
        }
    }

    private void AssignCanonicalCodes()
    {
        var histogram = new int[MaxCodeLength + 1];
        foreach (var length in _lengths)
        {
            if (length > _maxBits)
                throw new InvalidDataException("chd huffman length exceds maxbits");
            if (length <= MaxCodeLength)
                histogram[length]++;
        }

        var start = 0;
        for (var length = MaxCodeLength; length > 0; length--)
        {
            var next = (start + histogram[length]) >> 1;
            if (length != 1 && next * 2 != start + histogram[length])
                throw new InvalidDataException("chd huffman tree is not consistent"); //is this possible?
            var total = histogram[length];
            histogram[length] = start;
            _firstCode[length] = start;
            _counts[length] = total;
            start = next;
        }

        var index = 0;
        for (var length = 1; length <= MaxCodeLength; length++)
        {
            _firstIndex[length] = index;
            index += _counts[length];
        }

        _symbols = new int[index];
        var cursor = new int[MaxCodeLength + 1];
        for (var symbol = 0; symbol < _numCodes; symbol++)
        {
            int length = _lengths[symbol];
            if (length == 0) continue;
            _symbols[_firstIndex[length] + cursor[length]++] = symbol;
        }
    }
}