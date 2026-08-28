using RecompOne.Runtime.Context;
using RecompOne.Runtime.Dispatch;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Sdk;

public static class LibCd
{
    const byte Nop = 0x01,
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

    const int Complete = 0x02;
    const int DataEnd = 0x04;
    const int DataReady = 0x01;
    const int DiskError = 0x05;
    const byte ModeSize1 = 0x20, ModeSize0 = 0x10;

    const byte StatMotor = 0x02;
    const byte StatRead = 0x20;
    const byte StatSeek = 0x40;
    const byte StatPlay = 0x80;
    static byte _status;
    static byte _mode;
    static byte _com;
    static readonly byte[] _pos = new byte[4];
    static readonly byte[] _lastResult = new byte[8];
    static int _lastIntr = Complete;

    static bool _cddaPlaying;
    static bool _cddaAutoPause;
    static double _cddaPos;
    static int _cddaEndLba;

    static uint _cbSync;
    static uint _cbReady;
    static uint _cbData;

    static bool _readActive;
    static bool _xaActive;
    static byte _filterFile;
    static byte _filterChannel;

    internal static readonly object DiscLock = new();
    static readonly object _posGate = new();

    static Thread? _xaThread;
    static volatile bool _xaRun;

    static readonly bool[] NeedsLoc = BuildNeedsLoc();

    static bool[] BuildNeedsLoc()
    {
        var t = new bool[32];
        t[Play] = t[ReadN] = t[SeekL] = t[SeekP] = t[ReadS] = true;
        return t;
    }

    public static void CdInit(CpuContext c, IMemory m)
    {
        CdResetState();
        Runtime.Spu?.CdInitVolume();
        c.V0 = CdInitInternal() ? 0u : 1u;
    }

    public static void CdReset(CpuContext c, IMemory m)
    {
        CdResetState();
        c.V0 = CdInitInternal() ? 1u : 0u;
    }

    public static void CdControl(CpuContext c, IMemory m) => c.V0 = (uint)(CommandWait(m, (byte)c.A0, c.A1, c.A2, 0) == 0 ? 1 : 0);
    public static void CdControlF(CpuContext c, IMemory m) => c.V0 = (uint)(CommandWait(m, (byte)c.A0, c.A1, 0, 1) == 0 ? 1 : 0);

    public static void CdControlB(CpuContext c, IMemory m)
    {
        if (CommandWait(m, (byte)c.A0, c.A1, c.A2, 0) != 0) { c.V0 = 0; return; }
        uint result = c.A2;
        PumpReady(1);
        c.V0 = (uint)(SyncResult(m, result) == Complete ? 1 : 0);
    }

    public static void CdSync(CpuContext c, IMemory m)
    {
        uint result = c.A1;
        PumpSync();
        PumpReady(1);
        c.V0 = (uint)SyncResult(m, result);
    }

    public static void CdReady(CpuContext c, IMemory m)
    {
        uint result = c.A1;
        PumpSync();
        PumpReady(1);
        if (_readActive && _cbReady == 0 && _cbData == 0) _lastIntr = DataReady;
        if (result != 0) WriteResult(m, result);
        c.V0 = (uint)_lastIntr;
    }

    public static void CdRead(CpuContext c, IMemory m)
    {
        int sectors = (int)c.A0;
        uint buf = c.A1;
        _mode = (byte)c.A2;
        int lba = CurrentLba;

        _xaActive = false;
        lock (_dataIrqQueue) _dataIrqQueue.Clear();
        if (IsAudioRegion(lba) && (_mode & 0x01) == 0)
        {
            Log.Sdk($"CdRead out range lba={lba}");
            SetError(0x40, 0x01);
            c.V0 = 1;
            return;
        }
        int size = SectorSize(_mode);
        Dispatcher.LoadByLba(lba);
        Log.Sdk($"CdRead sectors={sectors} buf=0x{buf:X8} mode=0x{_mode:X2} lba={lba} size={size}");

        for (int i = 0; i < sectors; i++)
        {
            Dispatcher.LoadByLba(lba + i);
            byte[] data;
            lock (DiscLock) data = Runtime.Cd!.ReadSectorData(lba + i, size);
            for (int j = 0; j < data.Length; j++)
                m.WriteU8(buf + (uint)(i * size + j), data[j]);
        }
        _lastIntr = Complete;
        c.V0 = 1;
    }

    internal static int CurrentLba { get { lock (_posGate) return PosToInt(_pos); } }
    internal static double SectorsPerSecond => (_mode & 0x80) != 0 ? 150.0 : 75.0; //cd pacer

    const int MaxSectorsPerTick = 400000;


    static void StartCdda()
    {
        _cddaPlaying = false;
        var fs = Runtime.Cd?.Fs;
        if (fs == null) return;

        int lba = CurrentLba;
        int end = fs.LeadoutLba;
        for (int t = 1; t <= 99; t++)
        {
            if (!fs.TrackStartLba(t, out int start)) break;
            if (start > lba && start < end) end = start;
        }

        _cddaPos = lba;
        _cddaEndLba = end;
        _cddaAutoPause = (_mode & 0x02) != 0;
        _cddaPlaying = true;
        Log.Sdk($"cdda play lba= {lba} end= {end} autopause={_cddaAutoPause}");
    }

    static void TickCdda()
    {
        if (!_cddaPlaying) return;

        _cddaPos += 75.0 / 60.0;
        if (_cddaPos < _cddaEndLba) return;

        _cddaPlaying = false;
        _status = StatMotor;
        if (!_cddaAutoPause) return;

        _lastIntr = DataEnd;
        Log.Sdk($"DataEnd being deliver");
        var c = Runtime.Cpu;
        var m = Runtime.Mem;
        if (c == null || m == null || _cbReady == 0) return;
        var snap = c.Snapshot();
        c.A0 = DataEnd;
        c.A1 = 0;
        Dispatcher.Call(c, m, _cbReady);
        c.Restore(snap);
    }

    internal static void Tick()
    {
        PumpSync();
        PumpDataIrq();
        TickCdda();
        bool xaMode = (_mode & 0x40) != 0;

        if (_xaActive && xaMode) return;

        if (!_readActive || (_cbData == 0 && _cbReady == 0)) return;
        var c = Runtime.Cpu;
        var m = Runtime.Mem;
        if (c == null || m == null) return;

        var snap = c.Snapshot();
        if (_cbData != 0)
        {
            while (_cbData != 0)
            {
                _lastIntr = DataReady;
                if (_cbReady != 0) { c.A0 = DataReady; c.A1 = 0; Dispatcher.Call(c, m, _cbReady); }
                AdvancePos(1);
                Dispatcher.LoadByLba(CurrentLba);
                if (_cbData != 0) { c.A0 = DataReady; c.A1 = 0; Dispatcher.Call(c, m, _cbData); }
            }
        }
        else
        {
            c.Restore(snap);
            PumpReady(MaxSectorsPerTick);
            return;
        }
        c.Restore(snap);
    }

    static readonly Queue<int> _syncQueue = new();
    static bool _inSyncCb;

    static void QueueSync(int intr)
    {
        if (_cbSync == 0) return;
        lock (_syncQueue) _syncQueue.Enqueue(intr);
    }

    static void PumpSync()
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
                lock (_syncQueue)
                {
                    if (_syncQueue.Count == 0 || _cbSync == 0) break;
                    intr = _syncQueue.Dequeue();
                }
                c.A0 = (uint)intr;
                c.A1 = 0;
                Dispatcher.Call(c, m, _cbSync);
            }
        }
        finally
        {
            c.Restore(snap);
            _inSyncCb = false;
        }
    }

    static bool _pumping;
    
    
    
    static void PumpReady(int maxSectors)
    {
        if (_pumping || !_readActive || _cbReady == 0 || _cbData != 0) return;
        var c = Runtime.Cpu;
        var m = Runtime.Mem;
        if (c == null || m == null) return;

        _pumping = true;
        var snap = c.Snapshot();
        try
        {
            for (int i = 0; i < maxSectors && _readActive && _cbReady != 0; i++)
            {
                _lastIntr = DataReady;
                c.A0 = DataReady;
                c.A1 = 0;
                Dispatcher.Call(c, m, _cbReady);
                AdvancePos(1);
                Dispatcher.LoadByLba(CurrentLba);
            }
        }
        finally
        {
            c.Restore(snap);
            _pumping = false;
        }
    }

    static void EnsureXaThread()
    {
        if (_xaThread is { IsAlive: true }) return;
        _xaRun = true;
        _xaThread = new Thread(XaLoop) { IsBackground = true, Name = "CdXa" };
        _xaThread.Start();
    }

    static readonly ManualResetEventSlim _xaWake = new(false);
    internal static void WakeXa() => _xaWake.Set();

    static void XaLoop()
    {
        while (_xaRun)
        {
            if (_xaActive && (_mode & 0x40) != 0 && Runtime.Cd != null)
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
    }

    static readonly System.Diagnostics.Stopwatch _xaClock = System.Diagnostics.Stopwatch.StartNew();
    static double _xaLastMs;
    static double _xaCredit;
    const double XaBurst = 16.0;

    static void StartXaPacer()
    {
        _xaLastMs = _xaClock.Elapsed.TotalMilliseconds;
        _xaCredit = XaBurst;
    }

    static void PumpXa()
    {
        if (Runtime.Cd == null) return;
        const int MinBuffer = 4096;
        const int MaxScan = 32;
        bool useFilter = (_mode & 0x08) != 0;
        int scanned = 0;

        double now = _xaClock.Elapsed.TotalMilliseconds;
        _xaCredit += (now - _xaLastMs) * SectorsPerSecond / 1000.0;
        _xaLastMs = now;
        if (_xaCredit > XaBurst) _xaCredit = XaBurst;

        while (_xaActive && _xaCredit >= 1.0 && XaAudio.BufferedSamples < MinBuffer && scanned < MaxScan)
        {
            int lba = CurrentLba;
            if (lba < 0) break;
            if (lba >= Runtime.Cd.Fs.DataSectors) { _xaActive = false; break; }
            _xaCredit -= 1.0;
            byte[] sec;
            lock (DiscLock) sec = Runtime.Cd.ReadSectorData(lba, 2336);
            NoteSectorHeader(lba, sec);
            AdvancePos(1);
            scanned++;
            bool audio = (sec[2] & 0x04) != 0;
            if (!audio) { QueueDataIrq(lba); CarrierMiss(); continue; }

            if (!useFilter && _xaFirstSector && sec[1] != 0xFF)
            {
                _filterFile = sec[0];
                _filterChannel = sec[1];
                _xaFirstSector = false;
            }

            if (sec[1] == 0xFF || sec[0] != _filterFile || sec[1] != _filterChannel) { CarrierMiss(); continue; }
            _xaFirstSector = false;
            _carrierMiss = 0;
            Assets.Xa.XaRouter.Sector(lba, sec, false);
        }

        Assets.Xa.XaRouter.PumpTail();
    }

    static bool _xaFirstSector;

    static readonly Queue<int> _dataIrqQueue = new();

    static void QueueDataIrq(int lba)
    {
        lock (_dataIrqQueue)
        {
            if (_dataIrqQueue.Count >= 64) _dataIrqQueue.Dequeue();
            _dataIrqQueue.Enqueue(lba);
        }
    }

    static int _int1Lba = -1;
    static bool _cdDataPending;

    static void PumpDataIrq()
    {
        if (_cbReady == 0) return;
        var c = Runtime.Cpu;
        var m = Runtime.Mem;
        if (c == null || m == null) return;

        var snap = c.Snapshot();
        try
        {
            for (int i = 0; i < 16; i++)
            {
                int lba;
                lock (_dataIrqQueue)
                {
                    if (_dataIrqQueue.Count == 0 || _cbReady == 0) break;
                    lba = _dataIrqQueue.Dequeue();
                }

                _int1Lba = lba;
                _lastIntr = DataReady;
                _cdDataPending = false;
                c.A0 = DataReady;
                c.A1 = 0;
                Dispatcher.Call(c, m, _cbReady);

                if (_cdDataPending && _cbData != 0)
                {
                    _cdDataPending = false;
                    c.A0 = DataReady;
                    c.A1 = 0;
                    Dispatcher.Call(c, m, _cbData);
                }
            }
        }
        finally
        {
            _int1Lba = -1;
            c.Restore(snap);
        }
    }

    static readonly object _locGate = new();
    static readonly byte[] _locL = new byte[8];

    static void NoteSectorHeader(int lba, byte[] sec)
    {
        IntToPos(lba, out byte mm, out byte ss, out byte ff);
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
    
    
    const int CarrierMissLimit = 96;
    static int _carrierMiss;

    static void CarrierMiss()
    {
        if (++_carrierMiss < CarrierMissLimit) return;
        _carrierMiss = 0;
        if (!Assets.Xa.XaRouter.WantsCarrier(out int rewindLba)) return;
        lock (_posGate) IntToPos(rewindLba, out _pos[0], out _pos[1], out _pos[2]);
        Log.Sdk($"[assets] xa carrier rewinds to {rewindLba}"); 
    }

    static void AdvancePos(int n)
    {
        lock (_posGate)
            IntToPos(PosToInt(_pos) + n, out _pos[0], out _pos[1], out _pos[2]);
    }

    public static void CdReadSync(CpuContext c, IMemory m)
    {
        if (c.A1 != 0) WriteResult(m, c.A1);
        c.V0 = _lastIntr == DiskError ? 0xFFFFFFFFu : 0u;
    }

    public static void CdGetSector(CpuContext c, IMemory m)
    {
        uint madr = c.A0;
        int words = (int)c.A1;
        int lba = _int1Lba >= 0 ? _int1Lba : CurrentLba;
        byte[] data;
        lock (DiscLock) data = Runtime.Cd!.ReadSectorData(lba, SectorSize(_mode));
        int bytes = Math.Min(data.Length, words * 4);
        
        for (int j = 0; j < bytes; j++) m.WriteU8(madr + (uint)j, data[j]);

        _cdDataPending = true;
        if (_readActive && _cbReady == 0 && _cbData == 0)
        {
            AdvancePos(1);
            Dispatcher.LoadByLba(CurrentLba);
        }
        c.V0 = 1;
    }

    public static void CdDataSync(CpuContext c, IMemory m) => c.V0 = 0;

    public static void CdSearchFile(CpuContext c, IMemory m)
    {
        uint fp = c.A0;
        string name = ReadCString(m, c.A1);

        if (Runtime.Cd == null || !Runtime.Cd.Fs.Locate(name, out int lba, out uint size))
        {
            Log.Sdk($"CdSearchFile '{name}'wasnt found");
            c.V0 = 0;
            return;
        }
        Log.Sdk($"CdSearchFile '{name}' lba={lba} size={size}");

        IntToPos(lba, out byte mm, out byte ss, out byte ff);
        m.WriteU8(fp + 0, mm);
        m.WriteU8(fp + 1, ss);
        m.WriteU8(fp + 2, ff);
        m.WriteU8(fp + 3, 0);
        m.WriteU32(fp + 4, size);

        int slash = name.LastIndexOfAny(['/', '\\']);
        string basename = slash >= 0 ? name[(slash + 1)..] : name;
        
        for (int i = 0; i < 16; i++)
        {
            m.WriteU8(fp + 8 + (uint)i, i < basename.Length ? (byte)basename[i] : (byte)0);
        }

        c.V0 = fp;
    }

    public static void CdSyncCallback(CpuContext c, IMemory m) { c.V0 = _cbSync; _cbSync = c.A0; }
    public static void CdReadyCallback(CpuContext c, IMemory m) { c.V0 = _cbReady; _cbReady = c.A0; }
    public static void CdReadCallback(CpuContext c, IMemory m) { c.V0 = _cbData; _cbData = c.A0; }
    public static void CdDataCallback(CpuContext c, IMemory m) { c.V0 = _cbData; _cbData = c.A0; }

    public static void CdStatus(CpuContext c, IMemory m)
    {
        PumpReady(1);
        c.V0 = _status;
    }

    public static void CdMode(CpuContext c, IMemory m) => c.V0 = _mode;
    public static void CdLastCom(CpuContext c, IMemory m) => c.V0 = _com;

    public static void CdMix(CpuContext c, IMemory m)
    {
        if (c.A0 != 0)
            Runtime.Spu?.SetCdMix(m.ReadU8(c.A0), m.ReadU8(c.A0 + 1), m.ReadU8(c.A0 + 2), m.ReadU8(c.A0 + 3));
        c.V0 = 1;
    }


    internal static void Reset()
    {
        _xaRun = false;
        _xaThread = null;
        CdResetState();
    }

    static void CdResetState()
    {
        LibCdStream.OnStopStream();
        _status = StatMotor; //drive aways spin
        _mode = 0;
        _com = 0;
        _lastIntr = Complete;
        _cbSync = _cbReady = _cbData = 0;
        lock (_syncQueue) _syncQueue.Clear();
        lock (_dataIrqQueue) _dataIrqQueue.Clear();
        _readActive = false;
        _xaActive = false;
        _filterFile = _filterChannel = 0;
        Array.Clear(_pos);
        Array.Clear(_lastResult);
        Runtime.Spu?.SetCdMix(0x80, 0, 0x80, 0); //reset mix
        Dispatcher.ClearPending();
    }

    static bool CdInitInternal()
    {
        _lastIntr = Complete;
        _lastResult[0] = _status;
        return true;
    }

    static int CommandWait(IMemory m, byte com, uint param, uint result, uint arg)
    {
        if (param != 0 && com < NeedsLoc.Length && NeedsLoc[com])
            ExecCommand(m, Setloc, param, 0);
        int intr = ExecCommand(m, com, param, result);
        QueueSync(intr == 0 ? _lastIntr : intr);
        return intr;
    }

    static int ExecCommand(IMemory m, byte com, uint param, uint result)
    {
        _com = com;
        _lastIntr = Complete;
        Log.Sdk($"Cd cmd 0x{com:X2} param=0x{param:X8} pos={_pos[0]:X2}:{_pos[1]:X2}:{_pos[2]:X2}");

        switch (com)
        {
            case Setloc:
                if (param != 0)
                    lock (_posGate)
                        for (int i = 0; i < 4; i++) _pos[i] = m.ReadU8(param + (uint)i);
                break;
            case Setmode:
                if (param != 0) _mode = m.ReadU8(param);
                break;
            case Setfilter:
                if (param != 0) { _filterFile = m.ReadU8(param); _filterChannel = m.ReadU8(param + 1); }
                break;
            case ReadN:
                        if (IsAudioRegion(CurrentLba) && (_mode & 0x01) == 0)
                {
                    _readActive = false;
                    Log.Sdk($"readn out range lba={CurrentLba}");
                    SetError(0x40, 0x01);
                    if (result != 0) WriteResult(m, result);
                    return DiskError;
                }
                _readActive = true;
                _xaActive = true;
                _xaFirstSector = true;
                StartXaPacer();
                WakeXa();

                _status = (byte)(StatMotor | StatRead);
                Dispatcher.LoadByLba(CurrentLba);
                EnsureXaThread();
                break;
            case ReadS:
                        if (IsAudioRegion(CurrentLba) && (_mode & 0x01) == 0)
                {
                    _xaActive = false;
                    _readActive = false;
                    Log.Sdk($"ReadS out range lba={CurrentLba}");
                    SetError(0x40, 0x01);
                    if (result != 0) WriteResult(m, result);
                    return DiskError;
                }
                _xaActive = true;
                _xaFirstSector = true;
                _readActive = (_mode & 0x40) == 0;
                _status = (byte)(StatMotor | StatRead);
                LibCdStream.OnReadStream(CurrentLba);
                EnsureXaThread();
                break;
            case Play:
                _readActive = false;
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
                    Array.Copy(_locL, _lastResult, 8);
                if (result != 0) WriteResult(m, result);
                return 0;
            }
            case GetlocP:
            {
                int abs = CurrentLba + 150;
                int track = ResolveTrack(abs, out int rel);
                IntToPos(rel, out byte rmm, out byte rss, out byte rff);
                _lastResult[0] = ToBcd(track);
                _lastResult[1] = 0x01;
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
                for (int i = 3; i < _lastResult.Length; i++) _lastResult[i] = 0;
                if (result != 0) WriteResult(m, result);
                return 0;
            }
            case GetTD:
            {
                var fs = Runtime.Cd?.Fs;
                int track = param != 0 ? Bcd(m.ReadU8(param)) : 0;
                int lba = fs == null ? 0
                    : track == 0 || !fs.TrackStartLba(track, out int tl) ? fs.LeadoutLba : tl;
                MsfAbs(lba, out byte tmm, out byte tss);
                Log.Sdk($"Get-TD track={track} lba={lba} :: {tmm:D2}:{tss:D2}");
                _lastResult[0] = _status;
                _lastResult[1] = tmm;
                _lastResult[2] = tss;
                for (int i = 3; i < _lastResult.Length; i++) _lastResult[i] = 0;
                if (result != 0) WriteResult(m, result);
                return 0;
            }
            case Pause: case Stop: case Init:
                lock (_dataIrqQueue) _dataIrqQueue.Clear();
                _cddaPlaying = false;
                LibCdStream.OnStopStream();
                _readActive = false;
                _xaActive = false;
                _status = StatMotor;
                Dispatcher.ClearPending();
                break;
            case SeekL:
                if (IsAudioRegion(CurrentLba))
                {
                    Log.Sdk($"SeekL out range lba={CurrentLba}");
                    SetError(0x04, 0x04);
                    if (result != 0) WriteResult(m, result);
                    return 0;
                }
                break;
            case Nop: case Mute: case Demute:
            case Forward: case Backward: case Standby:
            case SeekP:
                break;
            default:
                break;
        }

        _lastResult[0] = _status;
        for (int i = 1; i < _lastResult.Length; i++) _lastResult[i] = 0;
        if (result != 0) WriteResult(m, result);
        return 0;
    }

    static int ResolveTrack(int abs, out int rel)
    {
        rel = abs - 150;
        var fs = Runtime.Cd?.Fs;
        int track = 1;
        if (fs is { HasTracks: true })
        {
            for (int t = fs.FirstTrack; t <= fs.LastTrack; t++)
            {
                if (fs.TrackStartLba(t, out int tl) && abs >= tl)
                {
                    track = t;
                    rel = abs - tl;
                }
            }
        } return track;
    }

    static void MsfAbs(int lba, out byte mm, out byte ss)
    {
        if (lba < 0) lba = 0;
        int abs = lba + 150;
        ss = ToBcd(abs / 75 % 60);
        mm = ToBcd(abs / 75 / 60);
    }

    static bool IsAudioRegion(int lba)
    {
        var fs = Runtime.Cd?.Fs;
        return fs != null && lba >= fs.DataSectors;
    }

    static void SetError(byte errByte, byte extraStat)
    {
        _lastIntr = DiskError;
        _lastResult[0] = (byte)(_status | extraStat);
        _lastResult[1] = errByte;
        for (int i = 2; i < _lastResult.Length; i++) _lastResult[i] = 0;
    }

    
    
    static int SyncResult(IMemory m, uint result)
    {
        if (result != 0) WriteResult(m, result);
        return _lastIntr;
    }

    static void WriteResult(IMemory m, uint addr)
    {
        for (int i = 0; i < _lastResult.Length; i++)
            m.WriteU8(addr + (uint)i, _lastResult[i]);
    }

    static int SectorSize(byte mode)
    {
        if ((mode & ModeSize1) != 0) return 2340;
        if ((mode & ModeSize0) != 0) return 2328;
        return 2048;
    }
    
    static string ReadCString(IMemory m, uint addr)
    {
        var sb = new System.Text.StringBuilder();
        for (uint i = 0; i < 128; i++)
        {
            byte b = m.ReadU8(addr + i);
            if (b == 0) break;
            sb.Append((char)b);
        }
        return sb.ToString();
    }

    static int Bcd(byte b) => (b >> 4) * 10 + (b & 0xF);
    static byte ToBcd(int n) => (byte)(((n / 10) << 4) + (n % 10));

    static int PosToInt(byte[] p) => (Bcd(p[0]) * 60 + Bcd(p[1])) * 75 + Bcd(p[2]) - 150;
    static void IntToPos(int i, out byte mm, out byte ss, out byte ff)
    {
        i += 150;
        ff = ToBcd(i % 75);
        ss = ToBcd(i / 75 % 60);
        mm = ToBcd(i / 75 / 60);
    }
    
    
}
