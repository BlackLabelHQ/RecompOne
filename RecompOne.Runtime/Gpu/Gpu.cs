using RecompOne.Runtime.Hle;

namespace RecompOne.Runtime;

//ToDO: HW Renderer must use a separate "view" quad text so widescren patches are possible
// or find a betrter approach, maybe allongate the vram as needed? a dufferent tex seens more ideal?
public sealed partial class Gpu
{
    public const int VramWidth = VramShadow.Width;
    public const int VramHeight = VramShadow.Height;

    public readonly VramShadow Shadow = new();
    public ushort[] Vram => Shadow.Pixels;

    private int _drawAreaLeft, _drawAreaTop, _drawAreaRight = VramWidth - 1, _drawAreaBottom = VramHeight - 1;
    private int _drawOffsetX, _drawOffsetY;

    private int _texPageX, _texPageY;
    private int _texDepth;
    private int _blendMode;
    private bool _dither;
    private bool _texDisable;

    private int _texWinMaskX, _texWinMaskY, _texWinOffX, _texWinOffY;

    private bool _setMask, _checkMask;

    private int _dispVramX, _dispVramY;
    private int _hRange1 = 0x200, _hRange2 = 0xC00, _vRange1 = 0x10, _vRange2 = 0x100;
    private int _hres;
    private bool _hres368, _vres480, _pal, _disp24, _interlace, _displayDisabled = true;
    private int _dmaDir;

    private readonly uint[] _fifo = new uint[1024];
    private int _fifoCount;
    private int _need;
    private bool _polyline;

    private bool _loadImage;
    private int _loadX, _loadY, _loadW, _loadH, _loadPx;

    private bool _readImage;
    private int _readX, _readY, _readW, _readH, _readPx;
    private uint _gpuRead;

    private bool _statField;

    public int DisplayX => _dispVramX;
    public int DisplayY => _dispVramY;
    public bool DisplayEnabled => !_displayDisabled;
    public bool Display24Bit => _disp24;
    public bool Pal => _pal;

    private int CyclesPerPixel => _hres368 ? 7 : _hres switch { 0 => 10, 1 => 8, 2 => 5, _ => 4 };

    public int DisplayWidth
    {
        get
        {
            var w = ((_hRange2 - _hRange1) / CyclesPerPixel + 2) & ~3;
            return Math.Clamp(w, 0, VramWidth);
        }
    }

    public int DisplayHeight
    {
        get
        {
            var lines = _vRange2 - _vRange1;
            if (_vres480) lines <<= 1;
            return Math.Clamp(lines, 0, VramHeight);
        }
    }

    public uint ReadStat()
    {
        uint s = 0;
        s |= (uint)((_texPageX / 64) & 0xF);
        s |= (uint)(((_texPageY / 256) & 1) << 4);
        s |= (uint)((_blendMode & 3) << 5);
        s |= (uint)((_texDepth & 3) << 7);

        if (_dither) s |= 1u << 9;
        s |= 1u << 10;
        if (_setMask) s |= 1u << 11;
        if (_checkMask) s |= 1u << 12;
        s |= 1u << 13;

        if (_texDisable) s |= 1u << 15;
        if (_hres368) s |= 1u << 16;

        s |= (uint)((_hres & 3) << 17);

        if (_vres480) s |= 1u << 19;
        if (_pal) s |= 1u << 20;
        if (_disp24) s |= 1u << 21;
        if (_interlace) s |= 1u << 22;
        if (_displayDisabled) s |= 1u << 23;

        s |= 1u << 26;
        s |= 1u << 27;
        s |= 1u << 28;
        s |= (uint)((_dmaDir & 3) << 29);
        s |= _dmaDir switch { 1 => 1u << 25, 2 => 1u << 28, 3 => 1u << 27, _ => 0u };

        _statField = !_statField;
        if (_statField) s |= 1u << 31;
        return s;
    }

    public uint ReadData()
    {
        if (!_readImage) return _gpuRead;
        var lo = ReadImageHalfword();
        var hi = ReadImageHalfword();
        return (uint)(lo | (hi << 16));
    }

    private bool _polylineShaded;

    private void Push(uint word)
    {
        if (_fifoCount < _fifo.Length) _fifo[_fifoCount++] = word;
    }

    public void WriteGp0Packet(ReadOnlySpan<uint> words)
    {
        if (words.Length == 0) return;

        if (_loadImage || _polyline || _fifoCount != 0 || words.Length > _fifo.Length)
        {
            foreach (var w in words) WriteGp0(w);
            return;
        }

        var need = CommandLength(words[0]);
        if (need != words.Length)
        {
            foreach (var w in words) WriteGp0(w);
            return;
        }

        words.CopyTo(_fifo);
        _fifoCount = words.Length;
        Execute();
        if (!_loadImage) _fifoCount = 0;
    }

    public void WriteGp0(uint word)
    {
        if (_loadImage)
        {
            StoreImageHalfword((ushort)word);
            StoreImageHalfword((ushort)(word >> 16));
            return;
        }

        if (_polyline)
        {
            //the terminator only sits where a vertex /colour pair start and the first pair is aways consumed untesting, psx_spx was unclear about dat, the implementation on pcsx redux is broken(somehow worse than mine), duckstation was the one that correctly implemented this, thanks duckstaion!!!!
            var data = _fifoCount - 1;
            var testable = _polylineShaded ? data >= 3 && (data & 1) == 1 : data >= 2;

            if (testable && (word & 0xF000F000u) == 0x50005000u)
            {
                _polyline = false;
                ExecutePolyline();
                _fifoCount = 0;
            }
            else
            {
                Push(word);
            }

            return;
        }

        Push(word);
        if (_fifoCount == 1)
        {
            _need = CommandLength(word);
            if (_need == LenPolyline)
            {
                _polyline = true;
                _polylineShaded = (word & (1u << 28)) != 0;
                return;
            }

            if (_need == LenImageLoad) _need = 3;
        }

        if (_fifoCount >= _need)
        {
            Execute();
            if (!_loadImage) _fifoCount = 0;
        }
    }

    public void WriteGp1(uint word)
    {
        var op = (word >> 24) & 0xFF;
        var p = word & 0xFFFFFF;
        switch (op)
        {
            case >= 0x05 and <= 0x08:
                WriteGp1Display(op, p);
                GpuHle.NotifyDisplay(_dispVramX, _dispVramY, DisplayWidth, DisplayHeight);
                return;
            case 0x00: Reset(); break;
            case 0x01:
                _fifoCount = 0;
                _polyline = false;
                _loadImage = false;
                break;
            case 0x02: break;
            case 0x03: _displayDisabled = (p & 1) != 0; break;
            case 0x04: _dmaDir = (int)(p & 3); break;
            case 0x10: SetGpuInfo(p); break;
        }
    }

    private void WriteGp1Display(uint op, uint p)
    {
        switch (op)
        {
            case 0x05:
                _dispVramX = (int)(p & 0x3FF);
                _dispVramY = (int)((p >> 10) & 0x1FF);
                break;
            case 0x06:
                _hRange1 = (int)(p & 0xFFF);
                _hRange2 = (int)((p >> 12) & 0xFFF);
                break;
            case 0x07:
                _vRange1 = (int)(p & 0x3FF);
                _vRange2 = (int)((p >> 10) & 0x3FF);
                break;
            case 0x08:
                _hres = (int)(p & 3);
                _hres368 = (p & 0x40) != 0;
                _vres480 = (p & 4) != 0;
                _pal = (p & 8) != 0;
                _disp24 = (p & 0x10) != 0;
                _interlace = (p & 0x20) != 0;
                break;
        }
    }

    private void Reset()
    {
        _fifoCount = 0;
        _polyline = _loadImage = _readImage = false;
        _displayDisabled = true;
        _dmaDir = 0;
        _texPageX = _texPageY = _texDepth = _blendMode = 0;
        _dither = _texDisable = false;
        _texWinMaskX = _texWinMaskY = _texWinOffX = _texWinOffY = 0;
        _drawAreaLeft = _drawAreaTop = 0;
        _drawAreaRight = VramWidth - 1;
        _drawAreaBottom = VramHeight - 1;
        _drawOffsetX = _drawOffsetY = 0;
        _setMask = _checkMask = false;
        _dispVramX = _dispVramY = 0;
    }

    private void SetGpuInfo(uint p)
    {
        switch (p & 0xFF)
        {
            case 0x03: _gpuRead = (uint)(_drawAreaLeft | (_drawAreaTop << 10)); break;
            case 0x04: _gpuRead = (uint)(_drawAreaRight | (_drawAreaBottom << 10)); break;
            case 0x05: _gpuRead = (uint)((_drawOffsetX & 0x7FF) | ((_drawOffsetY & 0x7FF) << 11)); break;
            default: _gpuRead = 0; break;
        }
    }
}