namespace RecompOne.Runtime.Cdrom.Chd;

//basically 7zip
internal static class ChdLzma
{
    private const int Lc = 3;
    private const int Lp = 0;
    private const int Pb = 2;

    private const int NumStates = 12;
    private const int NumPosBitsMax = 4;
    private const int NumTopBits = 24;
    private const uint TopValue = 1u << NumTopBits;
    private const int NumBitModelTotalBits = 11;
    private const uint BitModelTotal = 1u << NumBitModelTotalBits;
    private const int NumMoveBits = 5;
    private const int NumLenToPosStates = 4;
    private const int NumAlignBits = 4;
    private const int EndPosModelIndex = 14;
    private const int NumFullDistances = 1 << (EndPosModelIndex >> 1);
    private const int MatchMinLen = 2;

    public static void Decode(byte[] src, int srcOffset, int srcLength, byte[] dst, int dstOffset, int dstLength)
        => new Decoder(src, srcOffset, srcLength, dst, dstOffset, dstLength).Run();

    private sealed class Decoder
    {
        private readonly byte[] _src;
        private readonly int _srcEnd;
        private int _srcPos;

        private readonly byte[] _dst;
        private readonly int _dstStart;
        private readonly int _dstEnd;
        private int _dstPos;

        private uint _range;
        private uint _code;

        private readonly ushort[] _isMatch = NewProbs(NumStates << NumPosBitsMax);
        private readonly ushort[] _isRep = NewProbs(NumStates);
        private readonly ushort[] _isRepG0 = NewProbs(NumStates);
        private readonly ushort[] _isRepG1 = NewProbs(NumStates);
        private readonly ushort[] _isRepG2 = NewProbs(NumStates);
        private readonly ushort[] _isRep0Long = NewProbs(NumStates << NumPosBitsMax);
        private readonly ushort[] _posSlot = NewProbs(NumLenToPosStates << 6);
        private readonly ushort[] _specPos = NewProbs(NumFullDistances - EndPosModelIndex);
        private readonly ushort[] _align = NewProbs(1 << NumAlignBits);
        private readonly ushort[] _literal = NewProbs(0x300 << (Lc + Lp));

        private readonly LenDecoder _lenDecoder = new();
        private readonly LenDecoder _repLenDecoder = new();

        public Decoder(byte[] src, int srcOffset, int srcLength, byte[] dst, int dstOffset, int dstLength)
        {
            _src = src;
            _srcPos = srcOffset;
            _srcEnd = srcOffset + srcLength;
            _dst = dst;
            _dstStart = dstOffset;
            _dstPos = dstOffset;
            _dstEnd = dstOffset + dstLength;
        }

        public void Run()
        {
            InitRange();

            uint rep0 = 0, rep1 = 0, rep2 = 0, rep3 = 0;
            uint state = 0;
            byte previous = 0;

            while (_dstPos < _dstEnd)
            {
                uint posState = (uint)(_dstPos - _dstStart) & ((1u << Pb) - 1);

                if (DecodeBit(_isMatch, (state << NumPosBitsMax) + posState) == 0)
                {
                    uint literalIndex = ((uint)(_dstPos - _dstStart) & ((1u << Lp) - 1)) << Lc;
                    literalIndex += (uint)previous >> (8 - Lc);
                    literalIndex *= 0x300;

                    previous = state < 7
                        ? DecodeLiteral(literalIndex)
                        : DecodeMatchedLiteral(literalIndex, PeekBack(rep0));

                    _dst[_dstPos++] = previous;
                    state = state < 4 ? 0 : state < 10 ? state - 3 : state - 6;
                    continue;
                }

                uint len;

                if (DecodeBit(_isRep, state) != 0)
                {
                    if (DecodeBit(_isRepG0, state) == 0)
                    {
                        if (DecodeBit(_isRep0Long, (state << NumPosBitsMax) + posState) == 0)
                        {
                            state = state < 7 ? 9u : 11u;
                            previous = PeekBack(rep0);
                            _dst[_dstPos++] = previous;
                            continue;
                        }
                    }
                    else
                    {
                        uint distance;
                        if (DecodeBit(_isRepG1, state) == 0)
                        {
                            distance = rep1;
                        }
                        else
                        {
                            if (DecodeBit(_isRepG2, state) == 0)
                            {
                                distance = rep2;
                            }
                            else
                            {
                                distance = rep3;
                                rep3 = rep2;
                            }
                            rep2 = rep1;
                        }
                        rep1 = rep0;
                        rep0 = distance;
                    }

                    len = _repLenDecoder.Decode(this, posState) + MatchMinLen;
                    state = state < 7 ? 8u : 11u;
                }
                else
                {
                    rep3 = rep2;
                    rep2 = rep1;
                    rep1 = rep0;

                    len = _lenDecoder.Decode(this, posState);
                    state = state < 7 ? 7u : 10u;
                    rep0 = DecodeDistance(len);
                    len += MatchMinLen;
                }

                for (uint i = 0; i < len && _dstPos < _dstEnd; i++)
                {
                    previous = PeekBack(rep0);
                    _dst[_dstPos++] = previous;
                }
            }
        }

        private byte PeekBack(uint distance)
        {
            long index = _dstPos - (long)distance - 1;
            return index < _dstStart ? (byte)0 : _dst[index];
        }

        private uint DecodeDistance(uint len)
        {
            uint lenState = len < NumLenToPosStates ? len : NumLenToPosStates - 1;
            uint posSlot = BitTreeDecode(_posSlot, lenState << 6, 6);
            if (posSlot < 4) return posSlot;

            int numDirectBits = (int)((posSlot >> 1) - 1);
            uint distance = (2 | (posSlot & 1)) << numDirectBits;

            if (posSlot < EndPosModelIndex)
                distance += BitTreeReverseDecode(_specPos, (int)(distance - posSlot - 1), numDirectBits);
            else
            {
                distance += DecodeDirectBits(numDirectBits - NumAlignBits) << NumAlignBits;
                distance += BitTreeReverseDecode(_align, 0, NumAlignBits);
            }

            return distance;
        }

        private byte DecodeLiteral(uint index)
        {
            uint symbol = 1;
            do symbol = (symbol << 1) | DecodeBit(_literal, index + symbol);
            while (symbol < 0x100);
            return (byte)symbol;
        }

        private byte DecodeMatchedLiteral(uint index, byte matchByte)
        {
            uint symbol = 1;
            do
            {
                uint matchBit = (uint)(matchByte >> 7) & 1;
                matchByte <<= 1;
                uint bit = DecodeBit(_literal, index + ((1 + matchBit) << 8) + symbol);
                symbol = (symbol << 1) | bit;
                if (matchBit != bit)
                {
                    while (symbol < 0x100)
                        symbol = (symbol << 1) | DecodeBit(_literal, index + symbol);
                    break;
                }
            }
            while (symbol < 0x100);
            return (byte)symbol;
        }

        private void InitRange()
        {
            _code = 0;
            _range = 0xFFFFFFFF;
            NextByte();
            for (int i = 0; i < 4; i++)
                _code = (_code << 8) | NextByte();
        }

        private byte NextByte() => _srcPos < _srcEnd ? _src[_srcPos++] : (byte)0;

        internal uint DecodeBit(ushort[] probs, uint index)
        {
            ushort prob = probs[index];
            uint bound = (_range >> NumBitModelTotalBits) * prob;
            uint result;

            if (_code < bound)
            {
                _range = bound;
                probs[index] = (ushort)(prob + ((BitModelTotal - prob) >> NumMoveBits));
                result = 0;
            }
            else
            {
                _range -= bound;
                _code -= bound;
                probs[index] = (ushort)(prob - (prob >> NumMoveBits));
                result = 1;
            }

            if (_range < TopValue)
            {
                _range <<= 8;
                _code = (_code << 8) | NextByte();
            }

            return result;
        }

        private uint DecodeDirectBits(int count)
        {
            uint result = 0;
            for (int i = count; i > 0; i--)
            {
                _range >>= 1;
                uint bit = (_code - _range) >> 31;
                if (bit == 0) _code -= _range;
                result = (result << 1) | (1 - bit);

                if (_range < TopValue)
                {
                    _range <<= 8;
                    _code = (_code << 8) | NextByte();
                }
            }
            return result;
        }

        internal uint BitTreeDecode(ushort[] probs, uint offset, int levels)
        {
            uint m = 1;
            for (int i = 0; i < levels; i++)
                m = (m << 1) + DecodeBit(probs, offset + m);
            return m - ((uint)1 << levels);
        }

        private uint BitTreeReverseDecode(ushort[] probs, int offset, int levels)
        {
            uint m = 1;
            uint symbol = 0;
            for (int i = 0; i < levels; i++)
            {
                uint bit = DecodeBit(probs, (uint)offset + m);
                m = (m << 1) + bit;
                symbol |= bit << i;
            }
            return symbol;
        }

        private static ushort[] NewProbs(int size)
        {
            var probs = new ushort[size];
            Array.Fill(probs, (ushort)(BitModelTotal >> 1));
            return probs;
        }

        private sealed class LenDecoder
        {
            private readonly ushort[] _choice = NewProbs(2);
            private readonly ushort[] _low = NewProbs(1 << (NumPosBitsMax + 3));
            private readonly ushort[] _mid = NewProbs(1 << (NumPosBitsMax + 3));
            private readonly ushort[] _high = NewProbs(1 << 8);

            public uint Decode(Decoder decoder, uint posState)
            {
                if (decoder.DecodeBit(_choice, 0) == 0)
                    return decoder.BitTreeDecode(_low, posState << 3, 3);
                if (decoder.DecodeBit(_choice, 1) == 0)
                    return 8 + decoder.BitTreeDecode(_mid, posState << 3, 3);
                return 16 + decoder.BitTreeDecode(_high, 0, 8);
            }
        }
    }
}
