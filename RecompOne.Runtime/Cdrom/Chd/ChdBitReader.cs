namespace RecompOne.Runtime.Cdrom.Chd;

internal sealed class ChdBitReader
{
    private readonly byte[] _buffer;
    private int _position;
    private ulong _accumulator;
    private int _available;

    public ChdBitReader(byte[] buffer)
    {
        _buffer = buffer;
        _position = 0;
    }

    public bool Overflow { get; private set; }

    public uint Peek(int count)
    {
        if (count == 0) return 0;
        Fill(count);
        return (uint)((_accumulator >> (_available - count)) & Mask(count));
    }

    public void Remove(int count)
    {
        if (count == 0) return;
        _available -= count;
        _accumulator &= Mask(_available);
    }

    public uint Read(int count)
    {
        uint value = Peek(count);
        Remove(count);
        return value;
    }

    private void Fill(int count)
    {
        while (_available < count)
        {
            byte next;
            if (_position < _buffer.Length)
            {
                next = _buffer[_position++];
            }
            else
            {
                next = 0;
                Overflow = true;
            }
            _accumulator = (_accumulator << 8) | next;
            _available += 8;
        }
    }

    private static ulong Mask(int bits) => bits >= 64 ? ulong.MaxValue : (1UL << bits) - 1;
}
