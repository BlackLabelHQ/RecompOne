using RecompOne.Runtime.Context;
using RecompOne.Runtime.Dispatch;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Sdk;

public static class LibCd
{
    private const byte Nop = 0x01,
        Setloc = 0x02,
        Play = 0x03,
        Forward = 0x04,
        Backward = 0x05,
        ReadN = 0x06,
        Standby = 0x07,
        Stop = 0x08,
        Pause = 0x09,
        Init = 0x0A,
        Mute = 0x0B,
        Demute = 0x0C,
        Setfilter = 0x0D,
        Setmode = 0x0E,
        Getparam = 0x0F,
        GetlocL = 0x10,
        GetlocP = 0x11,
        GetTN = 0x13,
        GetTD = 0x14,
        SeekL = 0x15,
        SeekP = 0x16,
        ReadS = 0x1B;

    private const int Complete = 0x02;
    private const int DataEnd = 0x04;
    private const int DataReady = 0x01;
    private const int DiskError = 0x05;
    private const byte ModeSize1 = 0x20, ModeSize0 = 0x10;

    private const byte StatMotor = 0x02;
    private const byte StatRead = 0x20;
    private const byte StatSeek = 0x40;
    private const byte StatPlay = 0x80;
    private static byte _status;
    private static byte _mode;
    private static byte _com;
    private static readonly byte[] _pos = new byte[4];
    private static readonly byte[] _lastResult = new byte[8];
    private static int _lastIntr = Complete;
    private static int _dataIntr;

    private static bool _cddaPlaying;
    private static bool _cddaAutoPause;
    private static double _cddaPos;
    private static int _cddaEndLba;

    private static uint _cbSync;
    private static uint _cbReady;
    private static uint _cbData;
    private static uint _cbRead;

    private static bool _readActive;
    private static bool _xaActive;
    private static byte _filterFile;
    private static byte _filterChannel;

    internal static readonly object DiscLock = new();
    private static readonly object _posGate = new();
    private static readonly object _streamGate = new();

    private static Thread? _xaThread;
    private static volatile bool _xaRun;

    private static readonly bool[] NeedsLoc = BuildNeedsLoc();

    private static bool[] BuildNeedsLoc()
    {
        var t = new bool[32];
        t[Play] = t[ReadN] = t[SeekL] = t[SeekP] = t[ReadS] = true;
        return t;
    }

    public static void CdInit(CpuContext c, IMemory m)
    {
        CdResetState();
        Runtime.Spu?.CdInitVolume();
        c.V0 = CdInitInternal() ? 1u : 0u;
    }

    public static void CdReset(CpuContext c, IMemory m)
    {
        CdResetState();
        c.V0 = CdInitInternal() ? 1u : 0u;
    }

    public static void CdControl(CpuContext c, IMemory m)
    {
        c.V0 = (uint)(CommandWait(m, (byte)c.A0, c.A1, c.A2, 0) == 0 ? 1 : 0);
    }

    public static void CdControlF(CpuContext c, IMemory m)
    {
        c.V0 = (uint)(CommandWait(m, (byte)c.A0, c.A1, 0, 1) == 0 ? 1 : 0);
    }

    public static void CdControlB(CpuContext c, IMemory m)
    {
        if (CommandWait(m, (byte)c.A0, c.A1, c.A2, 0) != 0)
        {
            c.V0 = 0;
            return;
        }

        var result = c.A2;
        PumpReady(1);
        c.V0 = (uint)(SyncResult(m, result) == Complete ? 1 : 0);
    }

    public static void CdSync(CpuContext c, IMemory m)
    {
        var result = c.A1;
        PumpSync();
        PumpReady(1);
        c.V0 = (uint)SyncResult(m, result);
    }

    public static void CdReady(CpuContext c, IMemory m)
    {
        var result = c.A1;
        PumpSync();
        PumpReady(1);
        if (_readActive && _cbReady == 0 && _cbData == 0) _dataIntr = DataReady;

        var intr = _dataIntr;
        _dataIntr = 0;

        if (result != 0) WriteResult(m, result);
        c.V0 = (uint)intr;
    }

    public static void CdRead(CpuContext c, IMemory m)
    {
        var sectors = (int)c.A0;
        var buf = c.A1;
        _mode = (byte)c.A2;
        var lba = CurrentLba;

        _xaActive = false;
        lock (_dataIrqQueue)
        {
            _dataIrqQueue.Clear();
        }

        if (IsAudioRegion(lba) && (_mode & 0x01) == 0)
        {
            Log.Sdk($"CdRead out range lba={lba}");
            SetError(0x40, 0x01);
            c.V0 = 1;
            return;
        }

        var size = SectorSize(_mode);
        Dispatcher.LoadByLba(lba);
        Log.Sdk($"CdRead sectors={sectors} buf=0x{buf:X8} mode=0x{_mode:X2} lba={lba} size={size}");

        for (var i = 0; i < sectors; i++)
        {
            Dispatcher.LoadByLba(lba + i);
            byte[] data;
            lock (DiscLock)
            {
                data = Runtime.Cd!.ReadSectorData(lba + i, size);
            }

            for (var j = 0; j < data.Length; j++)
                m.WriteU8(buf + (uint)(i * size + j), data[j]);
        }

        _lastIntr = Complete;
        c.V0 = 1;
        FireRead(c, m);
    }

    private static bool _inReadCb;

    private static void FireRead(CpuContext c, IMemory m)
    {
        if (_inReadCb || _cbRead == 0) return;

        _inReadCb = true;
        var snap = c.Snapshot();
        try
        {
            c.A0 = Complete;
            c.A1 = PublishStatus(m);
            Dispatcher.Call(c, m, _cbRead);
        }
        finally
        {
            c.Restore(snap);
            _inReadCb = false;
        }
    }

    internal static int CurrentLba
    {
        get
        {
            lock (_posGate)
            {
                return PosToInt(_pos);
            }
        }
    }

    internal static double SectorsPerSecond => (_mode & 0x80) != 0 ? 150.0 : 75.0; //cd pacer

    private static int _cddaStart;

    private static void StartCdda()
    {
        _cddaPlaying = false;
        var fs = Runtime.Cd?.Fs;
        if (fs == null) return;

        var lba = CurrentLba;
        _cddaAutoPause = (_mode & 0x02) != 0;
        _cddaPos = lba;
        _cddaEndLba = _cddaAutoPause ? NextTrack(fs, lba) : fs.LeadoutLba;
        _cddaStart = lba;
        _cddaFeed = lba;
        _cddaPlaying = true;
        EnsureXaThread();
        WakeXa();
        Log.Sdk($"cdda play lba= {lba} end= {_cddaEndLba} autopause={_cddaAutoPause}");
    }

    private static int NextTrack(Cdrom.DiscFs fs, int lba)
    {
        var end = fs.LeadoutLba;
        for (var t = 1; t <= 99; t++)
        {
            if (!fs.TrackStartLba(t, out var start)) break;
            if (start > lba && start < end) end = start;
        }

        return end;
    }

    private static void StopCdda()
    {
        if (!_cddaPlaying) return;
        _cddaPlaying = false;
        XaAudio.Reset();
    }

    private static void TickCdda()
    {
        if (!_cddaPlaying) return;

        _cddaPos = AudiblePos();

        var at = _cddaPos < _cddaEndLba ? (int)_cddaPos : _cddaEndLba;
        lock (_posGate)
        {
            IntToPos(at, out _pos[0], out _pos[1], out _pos[2]);
        }

        if (_cddaPos < _cddaEndLba) return;

        _cddaPlaying = false;
        _status = StatMotor;
        if (!_cddaAutoPause) return;

        _lastIntr = DataEnd;
        _dataIntr = DataEnd;
        Log.Sdk($"DataEnd being deliver");
        if (LibDs.Active)
        {
            LibDs.CddaEnd();
            return;
        }

        var c = Runtime.Cpu;
        var m = Runtime.Mem;
        if (c == null || m == null || _cbReady == 0) return;
        var snap = c.Snapshot();
        c.A0 = DataEnd;
        c.A1 = PublishStatus(m);
        Dispatcher.Call(c, m, _cbReady);
        c.Restore(snap);
    }

    internal static void Tick()
    {
        _frameSectors = 0;
        TickCdda();
        if (LibDs.Active)
        {
            LibDs.Tick();
            return;
        }

        PumpSync();
        var xaMode = (_mode & 0x40) != 0;

        if (_xaActive && xaMode)
        {
            PumpDataIrq();
            return;
        }

        if (_inDataCb || !_readActive || (_cbData == 0 && _cbReady == 0)) return;
        var c = Runtime.Cpu;
        var m = Runtime.Mem;
        if (c == null || m == null) return;

        var snap = c.Snapshot();
        if (_cbData != 0)
        {
            _inDataCb = true;
            try
            {
                for (var i = 0; i < DeliveryLimit() && _cbData != 0; i++)
                {
                    if (FrameBudgetSpent()) break;
                    _lastIntr = DataReady;
                    _dataIntr = DataReady;
                    var lba = CurrentLba;
                    _int1Lba = lba;
                    FifoReload(lba);
                    if (_cbReady != 0)
                    {
                        c.A0 = DataReady;
                        c.A1 = PublishStatus(m);
                        Dispatcher.Call(c, m, _cbReady);
                    }

                    AdvancePos(1);
                    Dispatcher.LoadByLba(CurrentLba);
                    if (_cbData != 0)
                    {
                        c.A0 = DataReady;
                        c.A1 = PublishStatus(m);
                        Dispatcher.Call(c, m, _cbData);
                    }
                }
            }
            finally
            {
                _int1Lba = -1;
                _inDataCb = false;
            }
        }
        else
        {
            c.Restore(snap);
            if (_readyOnlyRead) PumpReady(1);
            return;
        }

        c.Restore(snap);
    }

    private static readonly Queue<(int Intr, byte[] Result)> _syncQueue = new();
    private static bool _inSyncCb;

    internal static byte StatusByte => _status;
    internal static byte ModeByte => _mode;
    internal static byte LastComByte => _com;
    internal static int LastIntrCode => _lastIntr;
    internal static bool CddaActive => _cddaPlaying;
    internal static int SectorBytes => SectorSize(_mode);

    internal static int Primitive(IMemory m, byte com, uint param, uint result)
    {
        return CommandWait(m, com, param, result, 0);
    }

    internal static void GrabResult(byte[] dst)
    {
        Array.Copy(_lastResult, dst, 8);
    }

    internal static void PutResult(IMemory m, uint addr, byte[] src)
    {
        if (addr == 0) return;
        for (var i = 0; i < 8; i++) m.WriteU8(addr + (uint)i, src[i]);
    }

    internal static void GrabPos(byte[] dst)
    {
        lock (_posGate)
        {
            Array.Copy(_pos, dst, 4);
        }
    }

    internal static void SeekTo(int lba)
    {
        lock (_posGate)
        {
            IntToPos(lba, out _pos[0], out _pos[1], out _pos[2]);
        }
    }

    internal static void Step(int sectors)
    {
        AdvancePos(sectors);
    }

    internal static int MsfToLba(byte mm, byte ss, byte ff)
    {
        return (Bcd(mm) * 60 + Bcd(ss)) * 75 + Bcd(ff) - 150;
    }

    internal static void LbaToMsf(int lba, out byte mm, out byte ss, out byte ff)
    {
        IntToPos(lba, out mm, out ss, out ff);
    }

    internal static uint ShowHeader(IMemory m, int lba)
    {
        return PublishHeader(m, lba);
    }

    internal static uint ShowStatus(IMemory m)
    {
        return PublishStatus(m);
    }

    internal static void ClearState()
    {
        CdResetState();
    }

    private const uint ResultAddr = 0x8000F800u;

    private static uint Publish(IMemory m, byte[] src)
    {
        for (var i = 0; i < 8; i++) m.WriteU8(ResultAddr + (uint)i, src[i]);
        return ResultAddr;
    }

    private static readonly byte[] _statResult = new byte[8];

    private static uint PublishStatus(IMemory m)
    {
        _statResult[0] = _status;
        for (var i = 1; i < 8; i++) _statResult[i] = 0;
        return Publish(m, _statResult);
    }

    private static readonly byte[] _headerResult = new byte[8];

    private static uint PublishHeader(IMemory m, int lba)
    {
        GrabHeader(lba, _headerResult);
        return Publish(m, _headerResult);
    }

    internal static void GrabHeader(int lba, byte[] dst)
    {
        if (lba >= 0 && Runtime.Cd != null && lba < Runtime.Cd.Fs.DataSectors)
        {
            byte[] sec;
            lock (DiscLock)
            {
                sec = Runtime.Cd.ReadSectorData(lba, 2336);
            }

            NoteSectorHeader(lba, sec);
        }

        lock (_locGate)
        {
            Array.Copy(_locL, dst, 8);
        }
    }

    private static void QueueSync(int intr)
    {
        if (_cbSync == 0) return;
        var snapshot = new byte[8];
        Array.Copy(_lastResult, snapshot, 8);
        lock (_syncQueue)
        {
            _syncQueue.Enqueue((intr, snapshot));
        }
    }

    public static void Pump()
    {
        PumpSync();
    }

    private static void PumpSync()
    {
        if (_inSyncCb || _cbSync == 0) return;
        var c = Runtime.Cpu;
        var m = Runtime.Mem;
        if (c == null || m == null) return;

        _inSyncCb = true;
        var snap = c.Snapshot();
        try
        {
            while (true)
            {
                int intr;
                byte[] result;
                lock (_syncQueue)
                {
                    if (_syncQueue.Count == 0 || _cbSync == 0) break;
                    (intr, result) = _syncQueue.Dequeue();
                }

                c.A0 = (uint)intr;
                c.A1 = Publish(m, result);
                Dispatcher.Call(c, m, _cbSync);
            }
        }
        finally
        {
            c.Restore(snap);
            _inSyncCb = false;
        }
    }

    private static bool _inDataCb;

    private static int _frameSectors;

    private const int MaxSectorsPerTick = 400000;

    private static bool _readyOnlyRead;

    public static int SectorsPerFrame { get; set; } = -1;

    private static int DeliveryLimit()
    {
        return SectorsPerFrame > 0 ? SectorsPerFrame : MaxSectorsPerTick;
    }

    private static bool FrameBudgetSpent()
    {
        var cap = SectorsPerFrame;
        if (cap <= 0) return false;
        if (_frameSectors >= cap) return true;

        _frameSectors++;
        return false;
    }

    private static void PumpReady(int maxSectors)
    {
        if (_inDataCb || !_readActive || _cbReady == 0 || _cbData != 0) return;
        var c = Runtime.Cpu;
        var m = Runtime.Mem;
        if (c == null || m == null) return;

        _inDataCb = true;
        var snap = c.Snapshot();
        try
        {
            for (var i = 0; i < maxSectors && _readActive && _cbReady != 0; i++)
            {
                if (FrameBudgetSpent()) break;
                var lba = CurrentLba;
                _int1Lba = lba;
                FifoReload(lba);
                _lastIntr = DataReady;
                _dataIntr = DataReady;
                _cdDataPending = false;
                c.A0 = DataReady;
                c.A1 = PublishStatus(m);
                Dispatcher.Call(c, m, _cbReady);
                AdvancePos(1);
                Dispatcher.LoadByLba(CurrentLba);
                if (!_cdDataPending) break;
            }
        }
        finally
        {
            _int1Lba = -1;
            c.Restore(snap);
            _inDataCb = false;
        }
    }

    private static void EnsureXaThread()
    {
        if (_xaThread is { IsAlive: true }) return;
        _xaRun = true;
        _xaThread = new Thread(XaLoop) { IsBackground = true, Name = "CdXa" };
        _xaThread.Start();
    }

    private static readonly ManualResetEventSlim _xaWake = new(false);

    internal static void WakeXa()
    {
        _xaWake.Set();
    }

    private static void XaLoop()
    {
        while (_xaRun)
            if (_cddaPlaying && Runtime.Cd != null)
            {
                PumpCdda();
                _xaWake.Reset();
                _xaWake.Wait(8);
            }
            else if (_xaActive && !LibDs.Active && !LibCdStream.Streaming && (_mode & 0x40) != 0 &&
                     Runtime.Cd != null)
            {
                PumpXa();
                _xaWake.Reset();
                _xaWake.Wait(8);
            }
            else
            {
                _xaWake.Reset();
                _xaWake.Wait(50);
            }
    }

    private const int CddaFrames = 588;
    private const int CddaBuffer = 16384;
    private static int _cddaFeed;
    private static bool _cddaMute;

    private static readonly int[] _cddaBlock = new int[CddaFrames];

    private static int AudiblePos()
    {
        var at = _cddaFeed - XaAudio.BufferedSamples / CddaFrames;
        return at < _cddaStart ? _cddaStart : at;
    }

    private static void PumpCdda()
    {
        var cd = Runtime.Cd;
        if (cd == null) return;

        var scanned = 0;

        while (_cddaPlaying && _cddaFeed < _cddaEndLba
               && XaAudio.BufferedSamples < CddaBuffer && scanned < 64)
        {
            byte[] raw;
            lock (DiscLock)
            {
                raw = cd.ReadRawSector(_cddaFeed);
            }

            _cddaFeed++;
            scanned++;

            if (_cddaMute)
            {
                Array.Clear(_cddaBlock);
            }
            else
            {
                for (var i = 0; i < CddaFrames; i++)
                {
                    var o = i * 4;
                    var l = (short)(raw[o] | (raw[o + 1] << 8));
                    var r = (short)(raw[o + 2] | (raw[o + 3] << 8));
                    _cddaBlock[i] = (ushort)l | (r << 16);
                }
            }

            XaAudio.PushFrames(_cddaBlock, CddaFrames, 44100);
        }
    }

    private static readonly System.Diagnostics.Stopwatch _xaClock = System.Diagnostics.Stopwatch.StartNew();
    private static double _xaLastMs;
    private static double _xaCredit;
    private const double XaBurst = 16.0;

    private const double SeekMs = 120.0;
    private static bool _seekPending;

    private static void StartXaPacer(bool seek = false)
    {
        _xaLastMs = _xaClock.Elapsed.TotalMilliseconds;
        _xaCredit = seek ? -SeekMs * SectorsPerSecond / 1000.0 : XaBurst;
    }

    private static void PumpXa()
    {
        if (Runtime.Cd == null) return;
        if (_xaActive && Assets.Xa.XaRouter.PumpDirect()) return;
        const int MinBuffer = 4096;
        const int MaxScan = 32;
        var scanned = 0;

        var now = _xaClock.Elapsed.TotalMilliseconds;
        _xaCredit += (now - _xaLastMs) * SectorsPerSecond / 1000.0;
        _xaLastMs = now;
        if (_xaCredit > XaBurst) _xaCredit = XaBurst;

        while (true)
        {
            lock (_streamGate)
            {
                if (!_xaActive || (_mode & 0x40) == 0 || _xaCredit < 1.0 ||
                    XaAudio.BufferedSamples >= MinBuffer || scanned >= MaxScan) break;
                if (!PumpXaSector((_mode & 0x08) != 0)) break;
                scanned++;
            }
        }

        Assets.Xa.XaRouter.PumpTail();
    }

    private static bool PumpXaSector(bool useFilter)
    {
        var lba = CurrentLba;
        if (lba < 0) return false;
        if (lba >= Runtime.Cd!.Fs.DataSectors)
        {
            _xaActive = false;
            return false;
        }

        _xaCredit -= 1.0;
        byte[] sec;
        lock (DiscLock)
        {
            sec = Runtime.Cd.ReadSectorData(lba, 2336);
        }

        NoteSectorHeader(lba, sec);
        AdvancePos(1);
        if ((sec[2] & 0x04) == 0)
        {
            QueueDataIrq(lba);
            CarrierMiss();
            return true;
        }

        if (!useFilter && _xaFirstSector && sec[1] != 0xFF)
        {
            _filterFile = sec[0];
            _filterChannel = sec[1];
            _xaFirstSector = false;
        }

        if (sec[1] == 0xFF || sec[0] != _filterFile || sec[1] != _filterChannel)
        {
            CarrierMiss();
            return true;
        }

        _xaFirstSector = false;
        _carrierMiss = 0;
        Assets.Xa.XaRouter.Sector(lba, sec, false);
        return true;
    }

    private static bool _xaFirstSector;

    private static readonly Queue<int> _dataIrqQueue = new();

    private static void QueueDataIrq(int lba)
    {
        lock (_dataIrqQueue)
        {
            if (_dataIrqQueue.Count >= 64) _dataIrqQueue.Dequeue();
            _dataIrqQueue.Enqueue(lba);
        }
    }

    private static int _int1Lba = -1;
    private static int _fifoLba = -1;
    private static int _fifoOff;

    private static void FifoReload(int lba)
    {
        _fifoLba = lba;
        _fifoOff = 0;
    }

    private static bool _cdDataPending;

    private static bool PumpDataIrq()
    {
        if (_inDataCb || _cbReady == 0) return false;
        var c = Runtime.Cpu;
        var m = Runtime.Mem;
        if (c == null || m == null) return false;

        var served = false;
        _inDataCb = true;
        var snap = c.Snapshot();
        try
        {
            for (var i = 0; i < 16; i++)
            {
                int lba;
                lock (_dataIrqQueue)
                {
                    if (_dataIrqQueue.Count == 0 || _cbReady == 0) break;
                    lba = _dataIrqQueue.Dequeue();
                }

                _int1Lba = lba;
                FifoReload(lba);
                _lastIntr = DataReady;
                _dataIntr = DataReady;
                _cdDataPending = false;
                served = true;
                c.A0 = DataReady;
                c.A1 = PublishStatus(m);
                Dispatcher.Call(c, m, _cbReady);

                if (_cdDataPending && _cbData != 0)
                {
                    _cdDataPending = false;
                    c.A0 = DataReady;
                    c.A1 = PublishStatus(m);
                    Dispatcher.Call(c, m, _cbData);
                }
            }
        }
        finally
        {
            _int1Lba = -1;
            c.Restore(snap);
            _inDataCb = false;
        }

        return served;
    }

    private static readonly object _locGate = new();
    private static readonly byte[] _locL = new byte[8];

    private static void NoteSectorHeader(int lba, byte[] sec)
    {
        IntToPos(lba, out var mm, out var ss, out var ff);
        lock (_locGate)
        {
            _locL[0] = mm;
            _locL[1] = ss;
            _locL[2] = ff;
            _locL[3] = 2;
            _locL[4] = sec[0];
            _locL[5] = sec[1];
            _locL[6] = sec[2];
            _locL[7] = sec[3];
        }
    }


    private const int CarrierMissLimit = 96;
    private static int _carrierMiss;

    private static void CarrierMiss()
    {
        if (++_carrierMiss < CarrierMissLimit) return;
        _carrierMiss = 0;
        if (!Assets.Xa.XaRouter.WantsCarrier(out var rewindLba)) return;
        lock (_posGate)
        {
            IntToPos(rewindLba, out _pos[0], out _pos[1], out _pos[2]);
        }

        Log.Sdk($"[assets] xa carrier rewinds to {rewindLba}");
    }

    private static void AdvancePos(int n)
    {
        lock (_posGate)
        {
            IntToPos(PosToInt(_pos) + n, out _pos[0], out _pos[1], out _pos[2]);
        }
    }

    public static void CdReadSync(CpuContext c, IMemory m)
    {
        if (c.A1 != 0) WriteResult(m, c.A1);
        c.V0 = _lastIntr == DiskError ? 0xFFFFFFFFu : 0u;
    }

    public static void CdGetSector(CpuContext c, IMemory m)
    {
        var madr = c.A0;
        var words = (int)c.A1;
        var lba = LibDs.Active && LibDs.SectorLba >= 0 ? LibDs.SectorLba
            : _int1Lba >= 0 ? _int1Lba : CurrentLba;
        byte[] data;
        lock (DiscLock)
        {
            data = Runtime.Cd!.ReadSectorData(lba, SectorSize(_mode));
        }

        if (lba != _fifoLba) FifoReload(lba);

        var bytes = Math.Min(data.Length - _fifoOff, words * 4);
        if (bytes < 0) bytes = 0;

        for (var j = 0; j < bytes; j++) m.WriteU8(madr + (uint)j, data[_fifoOff + j]);
        _fifoOff += bytes;

        _cdDataPending = true;
        if (!_inDataCb && _readActive && _cbReady == 0 && _cbData == 0)
        {
            AdvancePos(1);
            Dispatcher.LoadByLba(CurrentLba);
        }

        c.V0 = 1;
    }

    public static void CdDataSync(CpuContext c, IMemory m)
    {
        c.V0 = 0;
    }

    public static void CdSearchFile(CpuContext c, IMemory m)
    {
        var fp = c.A0;
        var name = ReadCString(m, c.A1);

        if (Runtime.Cd == null || !Runtime.Cd.Fs.Locate(name, out var lba, out var size))
        {
            Log.Sdk($"CdSearchFile '{name}'wasnt found");
            c.V0 = 0;
            return;
        }

        Log.Sdk($"CdSearchFile '{name}' lba={lba} size={size}");

        IntToPos(lba, out var mm, out var ss, out var ff);
        m.WriteU8(fp + 0, mm);
        m.WriteU8(fp + 1, ss);
        m.WriteU8(fp + 2, ff);
        m.WriteU8(fp + 3, 0);
        m.WriteU32(fp + 4, size);

        var slash = name.LastIndexOfAny(['/', '\\']);
        var basename = slash >= 0 ? name[(slash + 1)..] : name;

        for (var i = 0; i < 16; i++) m.WriteU8(fp + 8 + (uint)i, i < basename.Length ? (byte)basename[i] : (byte)0);

        c.V0 = fp;
    }

    public static void CdFlush(CpuContext c, IMemory m)
    {
        _lastIntr = Complete;
        _dataIntr = 0;
        _cdDataPending = false;
        lock (_dataIrqQueue)
        {
            _dataIrqQueue.Clear();
        }

        c.V0 = 0u;
    }

    public static void CdSyncCallback(CpuContext c, IMemory m)
    {
        c.V0 = _cbSync;
        _cbSync = c.A0;
    }

    public static void CdReadyCallback(CpuContext c, IMemory m)
    {
        c.V0 = _cbReady;
        _cbReady = c.A0;
    }

    public static void CdReadCallback(CpuContext c, IMemory m)
    {
        c.V0 = _cbRead;
        _cbRead = c.A0;
    }

    public static void CdDataCallback(CpuContext c, IMemory m)
    {
        c.V0 = _cbData;
        _cbData = c.A0;
        if (c.A0 != 0) _readyOnlyRead = false;
    }

    internal static void ClearStreamCallbacks()
    {
        _cbData = 0;
        _cbReady = 0;
    }

    public static void CdStatus(CpuContext c, IMemory m)
    {
        PumpReady(1);
        c.V0 = _status;
    }

    public static void CdMode(CpuContext c, IMemory m)
    {
        c.V0 = _mode;
    }

    public static void CdLastCom(CpuContext c, IMemory m)
    {
        c.V0 = _com;
    }

    public static void CdMix(CpuContext c, IMemory m)
    {
        if (c.A0 != 0)
            Runtime.Spu?.SetCdMix(m.ReadU8(c.A0), m.ReadU8(c.A0 + 1), m.ReadU8(c.A0 + 2), m.ReadU8(c.A0 + 3));
        c.V0 = 1;
    }


    internal static void Detach()
    {
        _cbSync = _cbReady = _cbData = _cbRead = 0;
        _inReadCb = false;
        _readActive = false;
        _readyOnlyRead = false;
        lock (_syncQueue)
        {
            _syncQueue.Clear();
        }

        lock (_dataIrqQueue)
        {
            _dataIrqQueue.Clear();
        }

        LibDs.Detach();
    }

    internal static void Reset()
    {
        _xaRun = false;
        _xaThread = null;
        CdResetState();
    }

    private static void CdResetState()
    {
        LibDs.Reset();
        LibCdStream.OnStopStream();
        _status = StatMotor; //drive aways spin
        _mode = 0;
        _com = 0;
        _lastIntr = Complete;
        _dataIntr = 0;
        _cbSync = _cbReady = _cbData = _cbRead = 0;
        lock (_syncQueue)
        {
            _syncQueue.Clear();
        }

        lock (_dataIrqQueue)
        {
            _dataIrqQueue.Clear();
        }

        _readActive = false;
        _readyOnlyRead = false;
        _xaActive = false;
        FifoReload(-1);
        _cddaMute = false;
        _filterFile = _filterChannel = 0;
        Array.Clear(_pos);
        Array.Clear(_lastResult);
        Runtime.Spu?.SetCdMix(0x80, 0, 0x80, 0); //reset mix
        Dispatcher.ClearPending();
    }

    private static bool CdInitInternal()
    {
        _lastIntr = Complete;
        _lastResult[0] = _status;
        return true;
    }

    private static int CommandWait(IMemory m, byte com, uint param, uint result, uint arg)
    {
        if (param != 0 && com < NeedsLoc.Length && NeedsLoc[com])
            ExecCommand(m, Setloc, param, 0);
        var intr = ExecCommand(m, com, param, result);
        QueueSync(intr == 0 ? _lastIntr : intr);
        return intr;
    }

    private static int ExecCommand(IMemory m, byte com, uint param, uint result)
    {
        lock (_streamGate)
        {
            return ExecCommandLocked(m, com, param, result);
        }
    }

    private static int ExecCommandLocked(IMemory m, byte com, uint param, uint result)
    {
        _com = com;
        _lastIntr = Complete;
        Log.Sdk($"Cd cmd 0x{com:X2} param=0x{param:X8} pos={_pos[0]:X2}:{_pos[1]:X2}:{_pos[2]:X2}");

        switch (com)
        {
            case Setloc:
                if (param != 0)
                    lock (_posGate)
                    {
                        for (var i = 0; i < 4; i++) _pos[i] = m.ReadU8(param + (uint)i);
                    }

                _seekPending = true;
                _readActive = false;
                _readyOnlyRead = false;
                lock (_dataIrqQueue)
                {
                    _dataIrqQueue.Clear();
                }

                break;
            case Setmode:
                if (param != 0) _mode = m.ReadU8(param);
                break;
            case Setfilter:
                if (param != 0)
                {
                    _filterFile = m.ReadU8(param);
                    _filterChannel = m.ReadU8(param + 1);
                }

                break;
            case ReadN:
                StopCdda();
                if (IsAudioRegion(CurrentLba) && (_mode & 0x01) == 0)
                {
                    _readActive = false;
                    Log.Sdk($"readn out range lba={CurrentLba}");
                    SetError(0x40, 0x01);
                    if (result != 0) WriteResult(m, result);
                    return DiskError;
                }

                _readActive = true;
                _readyOnlyRead = _cbData == 0;
                _xaActive = true;
                _xaFirstSector = true;
                StartXaPacer(_seekPending);
                _seekPending = false;
                WakeXa();

                _status = (byte)(StatMotor | StatRead);
                Dispatcher.LoadByLba(CurrentLba);
                EnsureXaThread();
                break;
            case ReadS:
                StopCdda();
                if (IsAudioRegion(CurrentLba) && (_mode & 0x01) == 0)
                {
                    _xaActive = false;
                    _readActive = false;
                    Log.Sdk($"ReadS out range lba={CurrentLba}");
                    SetError(0x40, 0x01);
                    if (result != 0) WriteResult(m, result);
                    return DiskError;
                }

                _status = (byte)(StatMotor | StatRead);
                if (LibCdStream.Streaming)
                {
                    _xaActive = false;
                    _readActive = false;
                    _readyOnlyRead = false;
                    lock (_dataIrqQueue)
                    {
                        _dataIrqQueue.Clear();
                    }

                    LibCdStream.OnReadStream(CurrentLba);
                    break;
                }

                _xaActive = true;
                _xaFirstSector = true;
                _readActive = (_mode & 0x40) == 0;
                _readyOnlyRead = _cbData == 0;
                lock (_streamGate)
                {
                    StartXaPacer(_seekPending);
                }

                _seekPending = false;
                LibCdStream.OnReadStream(CurrentLba);
                EnsureXaThread();
                break;
            case Play:
                _readActive = false;
                _readyOnlyRead = false;
                _xaActive = false;
                _status = (byte)(StatMotor | StatPlay);
                StartCdda();
                break;
            case Getparam:
                _lastResult[0] = _status;
                _lastResult[1] = _mode;
                _lastResult[2] = 0;
                _lastResult[3] = _filterFile;
                _lastResult[4] = _filterChannel;
                _lastResult[5] = 0;
                _lastResult[6] = 0;
                _lastResult[7] = 0;
                if (result != 0) WriteResult(m, result);
                return 0;
            case GetlocL:
            {
                lock (_locGate)
                {
                    Array.Copy(_locL, _lastResult, 8);
                }

                if (result != 0) WriteResult(m, result);
                return 0;
            }
            case GetlocP:
            {
                var track = ResolveTrack(CurrentLba, out var rel);
                MsfRel(rel < 0 ? -rel : rel, out var rmm, out var rss, out var rff);
                _lastResult[0] = ToBcd(track);
                _lastResult[1] = rel < 0 ? (byte)0x00 : (byte)0x01;
                _lastResult[2] = rmm;
                _lastResult[3] = rss;
                _lastResult[4] = rff;
                lock (_posGate)
                {
                    _lastResult[5] = _pos[0];
                    _lastResult[6] = _pos[1];
                    _lastResult[7] = _pos[2];
                }

                if (result != 0) WriteResult(m, result);
                return 0;
            }
            case GetTN:
            {
                var fs = Runtime.Cd?.Fs;
                _lastResult[0] = _status;
                _lastResult[1] = ToBcd(fs?.FirstTrack ?? 1);
                _lastResult[2] = ToBcd(fs?.LastTrack ?? 1);
                for (var i = 3; i < _lastResult.Length; i++) _lastResult[i] = 0;
                if (result != 0) WriteResult(m, result);
                return 0;
            }
            case GetTD:
            {
                var fs = Runtime.Cd?.Fs;
                var track = param != 0 ? Bcd(m.ReadU8(param)) : 0;
                var lba = fs == null ? 0
                    : track == 0 || !fs.TrackStartLba(track, out var tl) ? fs.LeadoutLba : tl;
                MsfAbs(lba, out var tmm, out var tss);
                Log.Sdk($"Get-TD track={track} lba={lba} :: {tmm:D2}:{tss:D2}");
                _lastResult[0] = _status;
                _lastResult[1] = tmm;
                _lastResult[2] = tss;
                for (var i = 3; i < _lastResult.Length; i++) _lastResult[i] = 0;
                if (result != 0) WriteResult(m, result);
                return 0;
            }
            case Pause:
            case Stop:
            case Init:
                Assets.Xa.XaRouter.StopDirect();
                lock (_dataIrqQueue)
                {
                    _dataIrqQueue.Clear();
                }

                _cddaPlaying = false;
                if (com != Pause) XaAudio.Reset();
                LibCdStream.OnStopStream();
                _readActive = false;
                _readyOnlyRead = false;
                _xaActive = false;
                _status = StatMotor;
                Dispatcher.ClearPending();
                break;
            case SeekL:
                StopCdda();
                if (IsAudioRegion(CurrentLba))
                {
                    Log.Sdk($"SeekL out range lba={CurrentLba}");
                    SetError(0x04, 0x04);
                    if (result != 0) WriteResult(m, result);
                    return 0;
                }

                break;
            case Mute:
                _cddaMute = true;
                break;
            case Demute:
                _cddaMute = false;
                break;
            case Nop:
            case Forward:
            case Backward:
            case Standby:
            case SeekP:
                break;
            default:
                break;
        }

        _lastResult[0] = _status;
        for (var i = 1; i < _lastResult.Length; i++) _lastResult[i] = 0;
        if (result != 0) WriteResult(m, result);
        return 0;
    }

    private static int ResolveTrack(int lba, out int rel)
    {
        rel = lba;
        var fs = Runtime.Cd?.Fs;
        if (fs is not { HasTracks: true }) return 1;

        var track = fs.FirstTrack;
        var start = 0;
        var best = int.MinValue;

        foreach (var t in fs.Tracks)
            if (lba >= t.PregapLba && t.PregapLba > best)
            {
                best = t.PregapLba;
                track = t.Number;
                start = t.StartLba;
            }

        if (best == int.MinValue && fs.TrackStartLba(fs.FirstTrack, out var first)) start = first;
        rel = lba - start;
        return track;
    }

    private static void MsfRel(int sectors, out byte mm, out byte ss, out byte ff)
    {
        if (sectors < 0) sectors = 0;
        ff = ToBcd(sectors % 75);
        ss = ToBcd(sectors / 75 % 60);
        mm = ToBcd(sectors / 75 / 60);
    }

    private static void MsfAbs(int lba, out byte mm, out byte ss)
    {
        if (lba < 0) lba = 0;
        var abs = lba + 150;
        ss = ToBcd(abs / 75 % 60);
        mm = ToBcd(abs / 75 / 60);
    }

    private static bool IsAudioRegion(int lba)
    {
        var fs = Runtime.Cd?.Fs;
        return fs != null && lba >= fs.DataSectors;
    }

    private static void SetError(byte errByte, byte extraStat)
    {
        _lastIntr = DiskError;
        _dataIntr = DiskError;
        _lastResult[0] = (byte)(_status | extraStat);
        _lastResult[1] = errByte;
        for (var i = 2; i < _lastResult.Length; i++) _lastResult[i] = 0;
    }


    private static int SyncResult(IMemory m, uint result)
    {
        if (result != 0) WriteResult(m, result);
        return _lastIntr;
    }

    private static void WriteResult(IMemory m, uint addr)
    {
        for (var i = 0; i < _lastResult.Length; i++)
            m.WriteU8(addr + (uint)i, _lastResult[i]);
    }

    private static int SectorSize(byte mode)
    {
        if ((mode & ModeSize1) != 0) return 2340;
        if ((mode & ModeSize0) != 0) return 2328;
        return 2048;
    }

    private static string ReadCString(IMemory m, uint addr)
    {
        var sb = new System.Text.StringBuilder();
        for (uint i = 0; i < 128; i++)
        {
            var b = m.ReadU8(addr + i);
            if (b == 0) break;
            sb.Append((char)b);
        }

        return sb.ToString();
    }

    private static int Bcd(byte b)
    {
        return (b >> 4) * 10 + (b & 0xF);
    }

    private static byte ToBcd(int n)
    {
        return (byte)(((n / 10) << 4) + (n % 10));
    }

    private static int PosToInt(byte[] p)
    {
        return (Bcd(p[0]) * 60 + Bcd(p[1])) * 75 + Bcd(p[2]) - 150;
    }

    private static void IntToPos(int i, out byte mm, out byte ss, out byte ff)
    {
        i += 150;
        ff = ToBcd(i % 75);
        ss = ToBcd(i / 75 % 60);
        mm = ToBcd(i / 75 / 60);
    }
}
