namespace RecompOne.Runtime;

public sealed partial class Gpu
{
    private const int LenPolyline = -1;
    private const int LenImageLoad = -2;

    private static readonly int[] CommandLengths = BuildCommandLengths();

    private static int[] BuildCommandLengths()
    {
        var table = new int[256];
        for (var op = 0; op < 256; op++) table[op] = ComputeCommandLength((uint)op << 24);
        return table;
    }

    private static int CommandLength(uint word)
    {
        return CommandLengths[word >> 24];
    }

    private static int ComputeCommandLength(uint word)
    {
        var op = word >> 24;
        switch (op)
        {
            case 0x02: return 3;
            case >= 0x20 and <= 0x3F:
            {
                var n = (word & (1u << 27)) != 0 ? 4 : 3;
                var shaded = (word & (1u << 28)) != 0;
                var tex = (word & (1u << 26)) != 0;
                return 1 + n + (shaded ? n - 1 : 0) + (tex ? n : 0);
            }
            case >= 0x40 and <= 0x5F:
            {
                if ((word & (1u << 27)) != 0) return LenPolyline;
                var shaded = (word & (1u << 28)) != 0;
                return 1 + 2 + (shaded ? 1 : 0);
            }
            case >= 0x60 and <= 0x7F:
            {
                var sz = (int)((word >> 27) & 3);
                var tex = (word & (1u << 26)) != 0;
                return 1 + 1 + (tex ? 1 : 0) + (sz == 0 ? 1 : 0);
            }
            case >= 0x80 and <= 0x9F: return 4;
            case >= 0xA0 and <= 0xBF: return LenImageLoad;
            case >= 0xC0 and <= 0xDF: return 3;
            default: return 1;
        }
    }

    private void Execute()
    {
        var word = _fifo[0];
        var op = word >> 24;
        switch (op)
        {
            case 0x02: FillRect(); break;
            case >= 0x20 and <= 0x3F: DrawPolygon(); break;
            case >= 0x40 and <= 0x5F: DrawLine(); break;
            case >= 0x60 and <= 0x7F: DrawRectangle(); break;
            case >= 0x80 and <= 0x9F: CopyVramToVram(); break;
            case >= 0xA0 and <= 0xBF: BeginImageLoad(); break;
            case >= 0xC0 and <= 0xDF: BeginImageRead(); break;
            case 0xE1: SetDrawMode(word); break;
            case 0xE2: SetTextureWindow(word); break;
            case 0xE3:
                _drawAreaLeft = (int)(word & 0x3FF);
                _drawAreaTop = (int)((word >> 10) & 0x3FF);
                break;
            case 0xE4:
                _drawAreaRight = (int)(word & 0x3FF);
                _drawAreaBottom = (int)((word >> 10) & 0x3FF);
                break;
            case 0xE5:
                _drawOffsetX = SignExtend11(word & 0x7FF);
                _drawOffsetY = SignExtend11((word >> 11) & 0x7FF);
                break;
            case 0xE6:
                _setMask = (word & 1) != 0;
                _checkMask = (word & 2) != 0;
                break;
        }
    }

    private void SetDrawMode(uint word)
    {
        _texPageX = (int)(word & 0xF) * 64;
        _texPageY = (int)((word >> 4) & 1) * 256;
        _blendMode = (int)((word >> 5) & 3);
        _texDepth = (int)((word >> 7) & 3);
        _dither = (word & (1u << 9)) != 0;
        _texDisable = (word & (1u << 11)) != 0;
    }

    private void SetTextureWindow(uint word)
    {
        _texWinMaskX = (int)(word & 0x1F);
        _texWinMaskY = (int)((word >> 5) & 0x1F);
        _texWinOffX = (int)((word >> 10) & 0x1F);
        _texWinOffY = (int)((word >> 15) & 0x1F);
    }

    private void FillRect()
    {
        var color = To15((int)(_fifo[0] & 0xFF), (int)((_fifo[0] >> 8) & 0xFF), (int)((_fifo[0] >> 16) & 0xFF));
        var x = (int)(_fifo[1] & 0x3F0);
        var y = (int)((_fifo[1] >> 16) & 0x1FF);
        var w = (int)(((_fifo[2] & 0x3FF) + 0xF) & ~0xF);
        var h = (int)((_fifo[2] >> 16) & 0x1FF);

        if (x + w <= VramWidth)
            for (var dy = 0; dy < h; dy++)
                Vram.AsSpan(((y + dy) & (VramHeight - 1)) * VramWidth + x, w).Fill(color);
        else
            for (var dy = 0; dy < h; dy++)
            for (var dx = 0; dx < w; dx++)
            {
                var px = (x + dx) & (VramWidth - 1);
                var py = (y + dy) & (VramHeight - 1);
                Vram[py * VramWidth + px] = color;
            }

        Assets.Textures.VramTracker.MarkCpuWrite(x, y, w, h);

        if (HleOn) HleFill(x, y, w, h, color);
    }

    private void CopyVramToVram()
    {
        int sx = (int)(_fifo[1] & 0x3FF), sy = (int)((_fifo[1] >> 16) & 0x1FF);
        int dx = (int)(_fifo[2] & 0x3FF), dy = (int)((_fifo[2] >> 16) & 0x1FF);
        var w = (int)(_fifo[3] & 0x3FF);
        if (w == 0) w = 0x400;
        var h = (int)((_fifo[3] >> 16) & 0x1FF);
        if (h == 0) h = 0x200;
        for (var row = 0; row < h; row++)
        for (var col = 0; col < w; col++)
        {
            var s = ((sy + row) & (VramHeight - 1)) * VramWidth + ((sx + col) & (VramWidth - 1));
            var d = ((dy + row) & (VramHeight - 1)) * VramWidth + ((dx + col) & (VramWidth - 1));
            var px = Vram[s];
            if (_checkMask && (Vram[d] & 0x8000) != 0) continue;
            if (_setMask) px |= 0x8000;
            Vram[d] = px;
        }

        Assets.Textures.VramTracker.MarkCpuWrite(dx, dy, w, h);

        if (HleOn) HleCopy(sx, sy, dx, dy, w, h);
    }

    private void BeginImageLoad()
    {
        _loadX = (int)(_fifo[1] & 0x3FF);
        _loadY = (int)((_fifo[1] >> 16) & 0x1FF);
        _loadW = (int)(_fifo[2] & 0xFFFF);
        if (_loadW == 0) _loadW = 0x400;
        else _loadW &= 0x3FF;
        if (_loadW == 0) _loadW = 0x400;
        _loadH = (int)((_fifo[2] >> 16) & 0xFFFF);
        if (_loadH == 0) _loadH = 0x200;
        else _loadH &= 0x1FF;
        if (_loadH == 0) _loadH = 0x200;
        _loadPx = 0;
        _loadImage = true;
        HleLoadBegin();
        _fifoCount = 0;
    }

    private void StoreImageHalfword(ushort value)
    {
        if (!_loadImage) return;
        var stored = _setMask ? (ushort)(value | 0x8000) : value;
        {
            var x = (_loadX + _loadPx % _loadW) & (VramWidth - 1);
            var y = (_loadY + _loadPx / _loadW) & (VramHeight - 1);
            var idx = y * VramWidth + x;
            if (!(_checkMask && (Vram[idx] & 0x8000) != 0))
                Vram[idx] = stored;
        }
        HleLoadPut(stored);
        if (++_loadPx >= _loadW * _loadH)
        {
            _loadImage = false;
            Assets.Textures.VramTracker.MarkCpuWrite(_loadX, _loadY, _loadW, _loadH);
            HleLoadFlush();
        }
    }

    private void BeginImageRead()
    {
        _readX = (int)(_fifo[1] & 0x3FF);
        _readY = (int)((_fifo[1] >> 16) & 0x1FF);
        _readW = (int)(_fifo[2] & 0x3FF);
        if (_readW == 0) _readW = 0x400;
        _readH = (int)((_fifo[2] >> 16) & 0x1FF);
        if (_readH == 0) _readH = 0x200;
        _readPx = 0;
        _readImage = true;
        if (HleOn) HleReadback(_readX, _readY, _readW, _readH);
    }

    private ushort ReadImageHalfword()
    {
        if (!_readImage) return 0;
        ushort v;
        if (HleOn)
        {
            v = _readPx < _readBuf.Length ? _readBuf[_readPx] : (ushort)0;
        }
        else
        {
            var x = (_readX + _readPx % _readW) & (VramWidth - 1);
            var y = (_readY + _readPx / _readW) & (VramHeight - 1);
            v = Vram[y * VramWidth + x];
        }

        if (++_readPx >= _readW * _readH) _readImage = false;
        return v;
    }

    private static int SignExtend11(uint v)
    {
        return (int)(v & 0x400) != 0 ? (int)(v | 0xFFFFF800) : (int)v;
    }
}