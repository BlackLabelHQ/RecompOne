using System.Diagnostics;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Sdk;

public static class LibCdStream
{
    private const int HeaderSize = 32;
    private const int SlotData = 2016;
    private const ushort VideoMagic = 0x0160;
    private const int PrimeFrames = 2;

    public static bool InUse { get; private set; }
    private static uint _statusBase;
    private static int _slots;
    private static uint _dataBase;

    private static volatile bool _active;
    private static volatile bool _reading;
    private static int _pendingLba = -1;
    private static int _streamLba = -1;
    private static int _streamStartLba;
    private static readonly Stopwatch _clock = new();

    private static int _writeIdx;
    private static bool _primed;
    private static bool[] _busy = Array.Empty<bool>();
    private static readonly Queue<(int start, int n)> _ready = new();
    private static int _prevStart = -1, _prevN;

    private static readonly ManualResetEventSlim _wake = new(false);
    private static Thread? _thread;
    private static volatile bool _run;
    private static readonly object _lock = new();

    public static void StSetRing(CpuContext c, IMemory m)
    {
        InUse = true;
        lock (_lock)
        {
            _statusBase = c.A0;
            _slots = (int)c.A1;
            _dataBase = _statusBase + (uint)(_slots * HeaderSize);
            ResetRing(m);
        }

        EnsureThread();
        Log.Sdk($"StSetRing base=0x{_statusBase:X8} slots={_slots} data=0x{_dataBase:X8}");
    }

    public static void StClearRing(CpuContext c, IMemory m)
    {
        lock (_lock)
        {
            ResetRing(m);
        }

        c.V0 = 0;
        Log.Sdk("StClearRing");
    }

    public static void StUnSetRing(CpuContext c, IMemory m)
    {
        _active = false;
        _reading = false;
        Log.Sdk("StUnSetRing");
    }

    public static void StSetStream(CpuContext c, IMemory m)
    {
        lock (_lock)
        {
            _streamLba = -1;
            ResetRing(m);
            XaAudio.Reset();
        }

        _active = true;
        EnsureThread();
        _wake.Set();
        Log.Sdk("StSetStream");
    }

    public static void StSetMask(CpuContext c, IMemory m)
    {
        c.V0 = 0;
        Log.Sdk("StSetMask");
    }

    public static void StGetNext(CpuContext c, IMemory m)
    {
        if (!_active)
        {
            c.V0 = 1;
            return;
        }

        lock (_lock)
        {
            if (_prevStart >= 0)
            {
                for (var i = 0; i < _prevN; i++) _busy[_prevStart + i] = false;
                _prevStart = -1;
            }

            if (_ready.Count == 0)
            {
                c.V0 = 1;
                return;
            }

            var (start, n) = _ready.Dequeue();
            var dataPtr = _dataBase + (uint)(start * SlotData);
            var hdrPtr = _statusBase + (uint)(start * HeaderSize);
            m.WriteU32(c.A0, dataPtr);
            m.WriteU32(c.A1, hdrPtr);
            _prevStart = start;
            _prevN = n;
            c.V0 = 0;
        }
    }

    public static void StFreeRing(CpuContext c, IMemory m)
    {
        c.V0 = 0;
        Log.Sdk("StFreeRing");
    }

    public static void StGetBackloc(CpuContext c, IMemory m)
    {
        c.V0 = 0xFFFFFFFFu;
        Log.Sdk("StGetBackloc");
    }


    internal static void OnReadStream(int lba)
    {
        if (!InUse) return;
        _pendingLba = lba;
        _reading = true;
        EnsureThread();
        _wake.Set();
    }

    internal static void OnStopStream()
    {
        _reading = false;
    }

    internal static void Reset()
    {
        _run = false;
        _thread = null;

        lock (_lock)
        {
            InUse = false;
            _active = false;
            _reading = false;
            _statusBase = 0;
            _dataBase = 0;
            _slots = 0;
            _pendingLba = -1;
            _streamLba = -1;
            _streamStartLba = 0;
            _writeIdx = 0;
            _prevStart = -1;
            _prevN = 0;
            _busy = Array.Empty<bool>();
            _ready.Clear();
            _clock.Reset();
        }
    }

    private static void ResetRing(IMemory m)
    {
        _primed = false;
        _writeIdx = 0;
        _prevStart = -1;
        _prevN = 0;
        _ready.Clear();
        _busy = _slots > 0 ? new bool[_slots] : Array.Empty<bool>();
        for (var i = 0; i < _slots; i++)
            m.WriteU16(_statusBase + (uint)(i * HeaderSize), 0);
    }

    private static void EnsureThread()
    {
        if (_thread is { IsAlive: true }) return;
        _run = true;
        _thread = new Thread(StreamLoop) { IsBackground = true, Name = "CdStream" };
        _thread.Start();
    }

    private static void StreamLoop()
    {
        while (_run)
        {
            var cd = Runtime.Cd;
            var m = Runtime.Mem;
            if (cd == null || m == null || !_active || !_reading || _slots <= 0)
            {
                _wake.Reset();
                _wake.Wait(50);
                continue;
            }

            if (_streamLba < 0)
            {
                _streamLba = _pendingLba >= 0 ? _pendingLba : LibCd.CurrentLba;
                _streamStartLba = _streamLba;
                _clock.Restart();
            }

            if (_streamLba >= cd.Fs.DataSectors)
            {
                _reading = false;
                continue;
            }

            byte[] sec;
            try
            {
                lock (LibCd.DiscLock)
                {
                    sec = cd.ReadSectorData(_streamLba, 2336);
                }
            }
            catch
            {
                Thread.Sleep(2);
                continue;
            }

            if ((sec[2] & 0x04) != 0)
            {
                Assets.Xa.XaRouter.Sector(_streamLba, sec, true);
                _streamLba++;
                continue;
            }

            if (Read16(sec, 8) != VideoMagic || Read16(sec, 12) != 0)
            {
                _streamLba++;
                continue;
            }

            int n = Read16(sec, 14);
            if (n <= 0 || n > _slots)
            {
                _streamLba++;
                continue;
            }

            if (_primed)
            {
                var delivered = _clock.Elapsed.TotalSeconds * LibCd.SectorsPerSecond;
                if (_streamLba - _streamStartLba + n > delivered)
                {
                    Thread.Sleep(1);
                    continue;
                }
            }

            int start;
            lock (_lock)
            {
                if (_writeIdx + n > _slots) _writeIdx = 0;
                start = _writeIdx;
                var free = true;
                for (var i = 0; i < n; i++)
                    if (_busy[start + i])
                    {
                        free = false;
                        break;
                    }

                if (!free)
                {
                    Thread.Sleep(1);
                    continue;
                }
            }

            if (!CollectFrame(cd, m, start, n)) continue;

            lock (_lock)
            {
                for (var i = 0; i < n; i++) _busy[start + i] = true;
                _ready.Enqueue((start, n));
                _writeIdx = start + n;

                if (!_primed && _ready.Count >= PrimeFrames)
                {
                    _primed = true;
                    _streamStartLba = _streamLba;
                    _clock.Restart();
                }
            }
        }
    }

    private static bool CollectFrame(Cdrom.CdController cd, IMemory m, int start, int n)
    {
        var collected = 0;
        var lba = _streamLba;
        while (collected < n)
        {
            byte[] sec;
            try
            {
                lock (LibCd.DiscLock)
                {
                    sec = cd.ReadSectorData(lba, 2336);
                }
            }
            catch
            {
                return false;
            }

            lba++;

            if ((sec[2] & 0x04) != 0)
            {
                Assets.Xa.XaRouter.Sector(lba - 1, sec, true);
                continue;
            }

            if (Read16(sec, 8) != VideoMagic) continue;

            var hdr = _statusBase + (uint)((start + collected) * HeaderSize);
            var dat = _dataBase + (uint)((start + collected) * SlotData);
            for (var j = 0; j < HeaderSize; j++) m.WriteU8(hdr + (uint)j, sec[8 + j]);
            for (var j = 0; j < SlotData; j++) m.WriteU8(dat + (uint)j, sec[8 + HeaderSize + j]);
            collected++;
        }

        _streamLba = lba;
        Thread.MemoryBarrier();
        return true;
    }

    private static ushort Read16(byte[] b, int o)
    {
        return (ushort)(b[o] | (b[o + 1] << 8));
    }
}