namespace RecompOne.Runtime.Hardware;

public sealed class Sio0
{
    private const ushort StatTxDataClear = 0x0001;
    private const ushort StatRxNotEmpty = 0x0002;
    private const ushort StatTxFinished = 0x0004;
    private const ushort StatAck = 0x0080;
    private const ushort StatIrq = 0x0200;
    private const ushort CtrlTxEnable = 0x0001;
    private const ushort CtrlSelect = 0x0002;
    private const ushort CtrlResetErr = 0x0010;
    private const ushort CtrlReset = 0x0040;
    private const ushort CtrlAckIrqEn = 0x1000;
    private const ushort CtrlPort2 = 0x2000;
    private const byte DeviceNone = 0x00;
    private const byte DevicePad = 0x01;
    private const byte DeviceCard = 0x81;
    private const byte CardId1 = 0x5A;
    private const byte CardId2 = 0x5D;
    private const byte CardAck1 = 0x5C;
    private const byte CardAck2 = 0x5D;
    private const byte CardGood = 0x47;
    private const byte CardBadChecksum = 0x4E;
    private const byte CardBadSector = 0xFF;
    private const int CardFrameSize = 128;
    private byte _cardCmd;
    private int _cardSector;
    private int _cardOffset;
    private byte _cardChecksum;
    private byte _cardPrev;
    private readonly byte[] _cardFrame = new byte[CardFrameSize];
    private static byte _dirFlagA = 0x08;
    private static byte _dirFlagB = 0x08;

    private ushort _status = StatTxDataClear | StatTxFinished;
    private ushort _mode;
    private ushort _ctrl;
    private ushort _baud;

    private byte _rx = 0xFF;
    private int _irqDelay;

    private static bool _ackPending;

    public static bool ConsumeAck()
    {
        if (!_ackPending) return false;
        _ackPending = false;
        return true;
    }

    private byte _device = DeviceNone;
    private int _step;

    public static bool InRange(uint phys)
    {
        return phys >= 0x1F801040u && phys <= 0x1F80104Fu;
    }

    public uint Read(uint phys)
    {
        switch (phys & ~1u)
        {
            case 0x1F801040u: return ReadData();
            case 0x1F801044u: return ReadStatus();
            case 0x1F801048u: return _mode;
            case 0x1F80104Au: return _ctrl;
            case 0x1F80104Eu: return _baud;
            default: return 0u;
        }
    }

    public void Write(uint phys, uint value)
    {
        switch (phys & ~1u)
        {
            case 0x1F801040u: WriteData((byte)value); break;
            case 0x1F801044u: break;
            case 0x1F801048u: _mode = (ushort)value; break;
            case 0x1F80104Au: WriteCtrl((ushort)value); break;
            case 0x1F80104Eu: _baud = (ushort)value; break;
        }
    }

    private ushort ReadStatus()
    {
        if (_irqDelay > 0 && --_irqDelay == 0)
            _status |= StatIrq;
        return _status;
    }

    private byte ReadData()
    {
        var v = _rx;
        _status &= unchecked((ushort)~StatRxNotEmpty);
        _rx = 0xFF;
        return v;
    }

    private void WriteData(byte value)
    {
        _status &= unchecked((ushort)~StatTxDataClear);
        if ((_ctrl & CtrlTxEnable) == 0 || (_status & StatTxFinished) == 0)
            return;
        Transfer(value);
        _status |= StatTxDataClear | StatTxFinished;
    }

    private void WriteCtrl(ushort value)
    {
        var deselected = (_ctrl & CtrlSelect) != 0 && (value & CtrlSelect) == 0;
        var portChanged = (_ctrl & CtrlPort2) != 0 && (value & CtrlPort2) == 0;

        _ctrl = value;

        if ((value & CtrlResetErr) != 0)
        {
            _status &= unchecked((ushort)~(StatIrq | StatAck));
            _irqDelay = 0;
        }

        if (deselected || portChanged || (value & CtrlReset) != 0)
        {
            _device = DeviceNone;
            _step = 0;
            _rx = 0xFF;
            _status &= unchecked((ushort)~StatRxNotEmpty);
        }

        if ((value & CtrlReset) != 0)
        {
            _status = StatTxDataClear | StatTxFinished;
            _mode = 0;
            _baud = 0;
        }
    }

    private void Transfer(byte value)
    {
        if (_device == DeviceNone)
        {
            _device = value;
            _step = 0;
        }

        byte rx = 0xFF;
        var ack = _device switch
        {
            DevicePad => PadTransfer(value, out rx),
            DeviceCard => CardTransfer(value, out rx),
            _ => false
        };

        _cardPrev = value;
        _rx = rx;
        _status |= StatRxNotEmpty;

        if (!ack) return;
        if ((_ctrl & CtrlAckIrqEn) == 0 || (_ctrl & CtrlTxEnable) == 0) return;
        _irqDelay = 2;
        _ackPending = true;
    }

    private bool CardTransfer(byte value, out byte rx)
    {
        var port2 = (_ctrl & CtrlPort2) != 0;
        var card = port2 ? Runtime.CardB : Runtime.CardA;
        rx = 0xFF;
        if (card == null || !card.Enabled)
        {
            _step = 0;
            _device = DeviceNone;
            return false;
        }

        var step = _step++;
        if (step == 0) return true;

        if (step == 1)
        {
            _cardCmd = value;
            _cardChecksum = 0;
            _cardOffset = 0;
            rx = port2 ? _dirFlagB : _dirFlagA;
            return true;
        }

        return _cardCmd switch
        {
            0x52 => CardRead(value, step, card, port2, out rx),
            0x57 => CardWrite(value, step, card, port2, out rx),
            _ => EndCard(out rx)
        };
    }

    private bool EndCard(out byte rx)
    {
        rx = 0xFF;
        _step = 0;
        _device = DeviceNone;
        return false;
    }

    private bool CardRead(byte value, int step, MemoryCard card, bool port2, out byte rx)
    {
        switch (step)
        {
            case 2:
                rx = CardId1;
                return true;
            case 3:
                rx = CardId2;
                return true;
            case 4:
                _cardSector = value << 8;
                rx = 0x00;
                return true;
            case 5:
                _cardSector |= value;
                _cardOffset = 0;
                rx = _cardPrev;
                if (_cardSector < 1024) card.FrameRead(_cardSector, _cardFrame);
                return true;
            case 6:
                rx = CardAck1;
                return true;
            case 7:
                rx = CardAck2;
                return true;
            case 8:
                rx = (byte)(_cardSector >> 8);
                return true;
            case 9:
                rx = (byte)_cardSector;
                _cardChecksum = (byte)((_cardSector >> 8) ^ (_cardSector & 0xFF));
                return true;
            default:
                if (step >= 10 && step < 10 + CardFrameSize)
                {
                    rx = _cardSector < 1024 ? _cardFrame[_cardOffset++] : CardBadSector;
                    _cardChecksum ^= rx;
                    return true;
                }

                if (step == 10 + CardFrameSize)
                {
                    rx = _cardChecksum;
                    return true;
                }

                if (step == 11 + CardFrameSize)
                {
                    rx = CardGood;
                    return EndCard(out _) || true;
                }

                return EndCard(out rx);
        }
    }

    private bool CardWrite(byte value, int step, MemoryCard card, bool port2, out byte rx)
    {
        switch (step)
        {
            case 2:
                rx = CardId1;
                return true;
            case 3:
                rx = CardId2;
                return true;
            case 4:
                _cardSector = value << 8;
                rx = 0x00;
                return true;
            case 5:
                _cardSector |= value;
                _cardOffset = 0;
                _cardChecksum = (byte)((_cardSector >> 8) ^ (_cardSector & 0xFF));
                rx = _cardPrev;
                return true;
            default:
                if (step >= 6 && step < 6 + CardFrameSize)
                {
                    if (_cardOffset < CardFrameSize) _cardFrame[_cardOffset++] = value;
                    _cardChecksum ^= value;
                    rx = _cardPrev;
                    return true;
                }

                if (step == 6 + CardFrameSize)
                {
                    if (_cardSector >= 1024)
                    {
                        rx = CardBadSector;
                        return EndCard(out _) || true;
                    }

                    rx = value == _cardChecksum ? CardAck1 : CardBadChecksum;
                    if (value != _cardChecksum) return EndCard(out _) || true;
                    return true;
                }

                if (step == 7 + CardFrameSize)
                {
                    rx = CardAck2;
                    return true;
                }

                if (step == 8 + CardFrameSize)
                {
                    card.FrameWrite(_cardSector, _cardFrame);
                    if (port2) _dirFlagB = 0x00;
                    else _dirFlagA = 0x00;
                    rx = CardGood;
                    EndCard(out _);
                    return false;
                }

                return EndCard(out rx);
        }
    }

    private bool PadTransfer(byte value, out byte rx)
    {
        var port2 = (_ctrl & CtrlPort2) != 0;
        if (port2 && !Controller.Connected2)
        {
            rx = 0xFF;
            _step = 0;
            return false;
        }

        var buttons = port2 ? Controller.State2 : Controller.State;
        var analog = port2 ? Controller.Analog2 : Controller.Analog;
        var step = _step++;
        switch (step)
        {
            case 0:
                rx = 0xFF;
                return true;
            case 1:
                if (value != 0x42)
                {
                    rx = 0xFF;
                    _step = 0;
                    return false;
                }

                rx = analog ? (byte)0x73 : (byte)0x41;
                return true;
            case 2:
                rx = 0x5A;
                return true;
            case 3:
                rx = (byte)buttons;
                return true;
            case 4:
                rx = (byte)(buttons >> 8);
                if (analog) return true;
                _step = 0;
                _device = DeviceNone;
                return false;
            case 5:
                rx = port2 ? Controller.RightX2 : Controller.RightX;
                return true;
            case 6:
                rx = port2 ? Controller.RightY2 : Controller.RightY;
                return true;
            case 7:
                rx = port2 ? Controller.LeftX2 : Controller.LeftX;
                return true;
            case 8:
                rx = port2 ? Controller.LeftY2 : Controller.LeftY;
                _step = 0;
                _device = DeviceNone;
                return false;
            default:
                rx = 0xFF;
                _step = 0;
                return false;
        }
    }
}
//shit