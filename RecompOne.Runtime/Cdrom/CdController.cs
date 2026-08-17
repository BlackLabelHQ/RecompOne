using RecompOne.Runtime.Bios;
using RecompOne.Runtime.Dispatch;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Cdrom;

public sealed class CdController
{
    private readonly DiscFs _fs;
    private readonly IMemory _m;

    private byte _index;
    private readonly Queue<byte> _paramFifo = new();
    private readonly Queue<byte> _responseFifo = new();
    private readonly Queue<(byte irqType, byte[] response)> _pendingIrqs = new();
    private byte _irqFlags;
    private int _seekLba;
    private byte[] _dataBuf = new byte[2048];

    private int _dataFifoPos;
    private bool _dataReady;
    private bool _reading;
    private bool _streamPending;
    private byte _lastIrq;

    private byte _mode;
    private byte _filterFile;
    private byte _filterChannel;
    private bool _playing;
    private bool _seeking;

    private readonly object _dbgGate = new();
    private readonly Queue<string> _dbgEvents = new();
    private const int DbgMaxEvents = 256;
    private long _sectorsRead;
    private int _lastReadLba;

    public struct CdDebug
    {
        public int SeekLba, LastReadLba;
        public bool Reading, StreamPending, DataReady;
        public byte IrqFlags, LastIrq, Index;
        public int PendingIrqCount, ParamCount, ResponseCount, DataFifoPos, DataBufLength;
        public long SectorsRead;
    }

    private sealed class ReadRun
    {
        public int Start, Count;
        public string Time = "";
    }

    private readonly Dictionary<string, ReadRun> _runs = new();

    private void DbgEvent(string msg)
    {
        lock (_dbgGate)
        {
            FlushRunsLocked();
            EnqueueLocked($"{DateTime.Now:HH:mm:ss.fff}  {msg}");
        }
    }

    private void DbgReadRun(string source, int lba)
    {
        lock (_dbgGate)
        {
            if (_runs.TryGetValue(source, out var run))
            {
                if (lba == run.Start + run.Count) { run.Count++; return; }
                EnqueueLocked(RunLine(source, run));
            }
            _runs[source] = new ReadRun { Start = lba, Count = 1, Time = DateTime.Now.ToString("HH:mm:ss.fff") };
        }
    }

    private void FlushRunsLocked()
    {
        foreach (var (source, run) in _runs)
            EnqueueLocked(RunLine(source, run));
        _runs.Clear();
    }

    private void EnqueueLocked(string line)
    {
        _dbgEvents.Enqueue(line);
        while (_dbgEvents.Count > DbgMaxEvents) _dbgEvents.Dequeue();
    }

    private static string RunLine(string source, ReadRun run) =>
        run.Count == 1
            ? $"{run.Time}  {source} lba={run.Start}"
            : $"{run.Time}  {source} lba={run.Start}..{run.Start + run.Count - 1} ({run.Count} sectors)";

    public void ClearDebugEvents()
    {
        lock (_dbgGate)
        {
            _dbgEvents.Clear();
            _runs.Clear();
        }
    }

    public void CaptureDebug(out CdDebug d, List<string> events)
    {
        d = new CdDebug {
            SeekLba = _seekLba,
            LastReadLba = _lastReadLba,
            Reading = _reading,
            StreamPending = _streamPending,
            DataReady = _dataReady,
            IrqFlags = _irqFlags,
            LastIrq = _lastIrq,
            Index = _index,
            PendingIrqCount = _pendingIrqs.Count,
            ParamCount = _paramFifo.Count,
            ResponseCount = _responseFifo.Count,
            DataFifoPos = _dataFifoPos,
            DataBufLength = _dataBuf.Length,
            SectorsRead = _sectorsRead
        };
        lock (_dbgGate)
        {
            events.Clear();
            events.AddRange(_dbgEvents);
            foreach (var (source, run) in _runs)
                events.Add(RunLine(source, run));
        }
    }

    private static string CmdName(byte cmd) => cmd switch {
        0x01 => "GetStat",
        0x02 => "Setloc",
        0x03 => "Play",
        0x04 => "Forward",
        0x05 => "Backward",
        0x06 => "ReadN",
        0x07 => "Standby",
        0x08 => "Stop",
        0x09 => "Pause",
        0x0A => "Init",
        0x0B => "Mute",
        0x0C => "Demute",
        0x0D => "Setfilter",
        0x0E => "Setmode",
        0x0F => "Getparam",
        0x10 => "GetlocL",
        0x11 => "GetlocP",
        0x13 => "GetTN",
        0x14 => "GetTD",
        0x15 => "SeekL",
        0x16 => "SeekP",
        0x19 => "Test",
        0x1A => "GetID",
        0x1B => "ReadS",
        0x1E => "ReadTOC",
        _ => $"0x{cmd:X2}"
    };

    public CdController(DiscFs fs, IMemory m)
    {
        _fs = fs;
        _m = m;
        BiosA.SetFs(fs);
        BiosA.SetCd(this);
        Runtime.Cd = this;
        Assets.AssetReplacerManager.Instance.LoadAll();
    }

    public void LoadToMemory(string path, uint address, int offset = 0, int length = -1)
    {
        var data = _fs.ReadFile(path);
        int count = length < 0 ? data.Length - offset : length;
        for (int i = 0; i < count; i++)
            _m.WriteU8(address + (uint)i, data[offset + i]);
        RecompOne.Runtime.Log.Cd($"{path} -> 0x{address:X8} | {count} bytes");
        DbgEvent($"file {path} -> 0x{address:X8} ({count} bytes)");
        Dispatcher.TryLoad(CdUtils.OverlayName(CdUtils.ExtractFileName(path)));
    }

    public byte Read(uint phys)
    {
        return (phys & 3) switch
        {
            0 => (byte)((_index & 3) | (_paramFifo.Count == 0 ? 0x08 : 0) | 0x10 | (_responseFifo.Count > 0 ? 0x20 : 0) | (_dataReady ? 0x40 : 0)),
            1 => _responseFifo.Count > 0 ? _responseFifo.Dequeue() : (byte)0,
            2 => ReadDataByte(),
            _ => _index == 1 ? _irqFlags : (byte)0,
        };
    }

    public void Write(uint phys, byte val)
    {
        switch (phys & 3)
        {
            case 0:
                _index = (byte)(val & 3);
                break;
            case 1:
                if (_index == 0) ExecuteCommand(val);
                break;
            case 2:
                if (_index == 0) _paramFifo.Enqueue(val);
                else if (_index == 1) _paramFifo.Clear();
                break;
            case 3:
                if (_index == 0)
                {
                    if ((val & 0x80) != 0) { _dataFifoPos = 0; _dataReady = true; }
                    else _dataReady = false;
                }
                else if (_index == 1)
                {
                    _irqFlags &= (byte)~val;
                    if (_irqFlags == 0) AfterAck();
                }
                break;
        }
    }

    private void ExecuteCommand(byte cmd)
    {
        RecompOne.Runtime.Log.Cd($"cmd 0x{cmd:X2}");
        var prms = new List<byte>();
        while (_paramFifo.Count > 0) prms.Add(_paramFifo.Dequeue());
        DbgEvent(prms.Count > 0
            ? $"{CmdName(cmd)} ({string.Join(" ", prms.Select(p => p.ToString("X2")))}) lba={_seekLba}"
            : $"{CmdName(cmd)} lba={_seekLba}");

        switch (cmd)
        {
            case 0x01:
                QueueIrq(3, [DriveStatus()]);
                break;
            case 0x02: //Setloc
                if (prms.Count >= 3)
                    _seekLba = BcdToLba(prms[0], prms[1], prms[2]);
                QueueIrq(3, [DriveStatus()]);
                break;
            case 0x03: // cd-da play (not sure if it goes used in games?)
                _reading = false;
                _playing = true;
                if (prms.Count == 1 && prms[0] != 0 && _fs.TrackStartLba(BcdToInt(prms[0]), out int trackLba))
                    _seekLba = trackLba - 150;
                QueueIrq(3, [DriveStatus()]);
                break;
            case 0x04: // frwd
            case 0x05: // bkwrd
                QueueIrq(3, [DriveStatus()]);
                break;
            case 0x06: // ReadN
                if (IsAudioRegion(_seekLba) && (_mode & 0x01) == 0) //h40 if not da mode
                {
                    _reading = false;
                    QueueIrq(5, [(byte)(DriveStatus() | 0x01), 0x40]);
                    break;
                }
                _reading = true;
                _playing = false;
                ReadNextSector();
                QueueIrq(3, [DriveStatus()]);
                QueueIrq(1, [DriveStatus()]);
                break;
            case 0x07: //standby
                QueueIrq(3, [DriveStatus()]);
                QueueIrq(2, [DriveStatus()]);
                break;
            case 0x08: //Stop
                _reading = false;
                _playing = false;
                _streamPending = false;
                QueueIrq(3, [DriveStatus()]);
                QueueIrq(2, [DriveStatus()]);
                break;
            case 0x09: // Pause
                _reading = false;
                _playing = false;
                _streamPending = false;
                QueueIrq(3, [DriveStatus()]);
                QueueIrq(2, [DriveStatus()]);
                break;
            case 0x0A:
                _mode = 0;
                _reading = false;
                _playing = false;
                _streamPending = false;
                QueueIrq(3, [DriveStatus()]);
                QueueIrq(2, [DriveStatus()]);
                break;
            case 0x0B: // mute
                QueueIrq(3, [DriveStatus()]);
                break;
            case 0x0C: // demute
                QueueIrq(3, [DriveStatus()]);
                break;
            case 0x0D: // set filter
                if (prms.Count >= 2) { _filterFile = prms[0]; _filterChannel = prms[1]; }
                QueueIrq(3, [DriveStatus()]);
                break;
            case 0x0E: // set mode
                if (prms.Count >= 1) _mode = prms[0];
                QueueIrq(3, [DriveStatus()]);
                break;
            case 0x0F: // get param
                QueueIrq(3, [DriveStatus(), _mode, 0x00, _filterFile, _filterChannel]);
                break;
            case 0x10: // Getloc L
                QueueIrq(3, GetlocL());
                break;
            case 0x11: // Getloc P
                QueueIrq(3, GetlocP());
                break;
            case 0x13: // GetTN
                QueueIrq(3, [DriveStatus(), IntToBcd(_fs.FirstTrack), IntToBcd(_fs.LastTrack)]);
                break;
            case 0x14: // getTD
            {
                int track = prms.Count >= 1 ? BcdToInt(prms[0]) : 0;
                int lba = track == 0 || !_fs.TrackStartLba(track, out int tl) ? _fs.LeadoutLba : tl;
                LbaToMsf(lba, out byte tmm, out byte tss, out _);
                QueueIrq(3, [DriveStatus(), tmm, tss]);
                break;
            }
            case 0x15: // seek L
                _seeking = true;
                QueueIrq(3, [DriveStatus()]);
                _seeking = false;
                //04h if outside
                if (IsAudioRegion(_seekLba)) 
                {
                    _reading = false;
                    _playing = false;
                    QueueIrq(5, [(byte)(DriveStatus() | 0x04), 0x04]);
                }
                else
                {
                    QueueIrq(2, [DriveStatus()]);
                }
                break;
            case 0x16: //seek P
                _seeking = true;
                QueueIrq(3, [DriveStatus()]);
                _seeking = false;
                QueueIrq(2, [DriveStatus()]);
                break;
            case 0x19: // tst
                if (prms.Count >= 1 && prms[0] == 0x20)
                    QueueIrq(3, [0x94, 0x09, 0x19, 0xC0]);
                else
                    QueueIrq(3, [DriveStatus()]);
                break;
            case 0x1A: // get id
                QueueIrq(3, [DriveStatus()]);
                QueueIrq(2, [0x02, 0x00, 0x20, 0x00, 0x53, 0x43, 0x45, 0x41]);
                break;
            case 0x1B: // read s
                //40h if outside
                if (IsAudioRegion(_seekLba) && (_mode & 0x01) == 0)
                {
                    _reading = false;
                    QueueIrq(5, [(byte)(DriveStatus() | 0x01), 0x40]);
                    break;
                }
                _reading = true;
                _playing = false;
                ReadNextSector();
                QueueIrq(3, [DriveStatus()]);
                QueueIrq(1, [DriveStatus()]);
                break;
            case 0x1E: // Read toc
                QueueIrq(3, [DriveStatus()]);
                QueueIrq(2, [DriveStatus()]);
                break;
            default:
                Console.WriteLine($"[CD] command 0x{cmd:X2} is unknow");
                QueueIrq(5, [DriveStatus(), 0x40]);
                break;
        }
    }

    
    private void QueueIrq(byte irqType, byte[] response)
    {
        if (_irqFlags == 0 && _pendingIrqs.Count == 0)
            DeliverImmediate(irqType, response);
        else
            _pendingIrqs.Enqueue((irqType, response));
    }

    private void AfterAck()
    {
        if (_pendingIrqs.Count > 0) { DeliverNext(); return; }
        if (_reading && _lastIrq == 1) _streamPending = true;
        ClearInInterrupt();
    }

    public void AdvanceStreaming()
    {
        if (!_reading || !_streamPending) return;
        if (_irqFlags != 0 || _pendingIrqs.Count > 0) return;
        _streamPending = false;
        ReadNextSector();
        DeliverImmediate(1, [DriveStatus()]);
    }

    private void DeliverImmediate(byte irqType, byte[] response)
    {
        _responseFifo.Clear();
        foreach (var b in response) _responseFifo.Enqueue(b);
        _irqFlags = irqType;
        _lastIrq = irqType;
        SetInInterrupt(1);
    }

    private void DeliverNext()
    {
        var (irqType, response) = _pendingIrqs.Dequeue();
        _responseFifo.Clear();
        foreach (var b in response) _responseFifo.Enqueue(b);
        _irqFlags = irqType;
        _lastIrq = irqType;
        SetInInterrupt(1);
    }
    private byte ReadDataByte()
    {
        if (!_dataReady || _dataFifoPos >= _dataBuf.Length) { _dataReady = false; return 0; }
        byte b = _dataBuf[_dataFifoPos++];
        if (_dataFifoPos >= _dataBuf.Length) _dataReady = false;
        return b;
    }

    public void DmaReadData(IMemory m, uint addr, uint byteCount)
    {
        for (uint i = 0; i < byteCount; i++)
            m.WriteU8(addr + i, _dataFifoPos < _dataBuf.Length ? _dataBuf[_dataFifoPos++] : (byte)0);
        if (_dataFifoPos >= _dataBuf.Length) _dataReady = false;
    }

    public void LoadSectorToFifo(byte[] data)
    {
        _dataBuf = (byte[])data.Clone();
        _dataFifoPos = 0;
        _dataReady = true;
    }

    private void SetInInterrupt(ushort val)
    {
        if (BiosB.IntrEnvInInterruptAddr != 0)
            _m.WriteU16(BiosB.IntrEnvInInterruptAddr, val);
    }

    private void ClearInInterrupt()
    {
        if (BiosB.IntrEnvInInterruptAddr != 0)
            _m.WriteU16(BiosB.IntrEnvInInterruptAddr, 0);
    }

    private void ReadNextSector()
    {
        try
        {
            _dataBuf = _fs.ReadSector(_seekLba);
            DbgReadRun("read", _seekLba);
            _lastReadLba = _seekLba;
            _sectorsRead++;
            _seekLba++;
        }
        catch
        {
            Array.Clear(_dataBuf);
        }
    }

    public DiscFs Fs => _fs;
    public byte DriveStatusByte() => DriveStatus();

    public byte[] ReadSectorData(int lba)
    {
        _seekLba = lba;
        ReadNextSector();
        return (byte[])_dataBuf.Clone();
    }

    public byte[] ReadSectorData(int lba, int size)
    {
        DbgReadRun(size == 2336 ? "readXA" : "read", lba);
        _lastReadLba = lba;
        _sectorsRead++;
        return _fs.ReadSectorData(lba, size);
    }

    public void QueueAsyncSeekL(byte mm, byte ss, byte ff)
    {
        _seekLba = BcdToLba(mm, ss, ff);
        DbgEvent($"async SeekL lba={_seekLba}");
        QueueIrq(3, [DriveStatus()]);
        QueueIrq(2, [DriveStatus()]);
    }

    public void QueueAsyncGetStatus()
    {
        QueueIrq(3, [DriveStatus()]);
    }

    public void QueueAsyncSetMode(byte mode)
    {
        DbgEvent($"async Setmode {mode:X2}");
        QueueIrq(3, [DriveStatus()]);
    }

    public void QueueAsyncReadSector(uint count, uint dstAddr, uint mode)
    {
        DbgEvent($"async ReadSector lba={_seekLba} count={count} dst=0x{dstAddr:X8}");
        for (uint i = 0; i < count; i++)
        {
            ReadNextSector();
            int sectorSize = (mode & 0x30) == 0 ? 2048 : 2048; //fix
            for (int j = 0; j < Math.Min(_dataBuf.Length, sectorSize); j++)
                _m.WriteU8(dstAddr + i * (uint)sectorSize + (uint)j, _dataBuf[j]);
            _seekLba++;
        }
        QueueIrq(3, [DriveStatus()]);
        QueueIrq(1, [DriveStatus()]);
        QueueIrq(2, [DriveStatus()]);
    }

    
    private bool IsAudioRegion(int lba) => lba >= _fs.DataSectors;

    private byte DriveStatus()
    {
        byte s = 0x02;
        if (_reading) s |= 0x20;
        if (_seeking) s |= 0x40;
        if (_playing) s |= 0x80;
        return s;
    }

    private byte[] GetlocL()
    {
        LbaToMsf(_lastReadLba + 150, out byte amm, out byte ass, out byte aff);
        return [amm, ass, aff, _mode, _filterFile, _filterChannel, 0, 0];
    }
    private byte[] GetlocP()
    {
        int abs = _seekLba + 150;
        LbaToMsf(abs, out byte amm, out byte ass, out byte aff);
        int track = 1;
        int rel = _seekLba;
        if (_fs.HasTracks)
        {
            for (int t = _fs.FirstTrack; t <= _fs.LastTrack; t++)
            {
                if (_fs.TrackStartLba(t, out int tl) && abs >= tl)
                {
                    track = t;
                    rel = abs - tl;
                }
            }
        }
        LbaToMsf(rel, out byte rmm, out byte rss, out byte rff);
        return [IntToBcd(track), 0x01, rmm, rss, rff, amm, ass, aff];
    }

    private static byte IntToBcd(int n) => (byte)(((n / 10) << 4) | (n % 10));
    private static int BcdToInt(byte b) => (b >> 4) * 10 + (b & 0xF);
    
    //not sure if its correct
    private static void LbaToMsf(int lba, out byte mm, out byte ss, out byte ff)
    {
        if (lba < 0) lba = 0;
        ff = IntToBcd(lba % 75);
        ss = IntToBcd(lba / 75 % 60);
        mm = IntToBcd(lba / 75 / 60);
    }

    private static int BcdToLba(byte mm, byte ss, byte ff)
    {
        int m = (mm >> 4) * 10 + (mm & 0xF);
        int s = (ss >> 4) * 10 + (ss & 0xF);
        int f = (ff >> 4) * 10 + (ff & 0xF);
        return (m * 60 + s) * 75 + f - 150;
    }
}
