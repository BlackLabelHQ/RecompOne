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
    private const long StarveMs = 500;
    private const int StarvePollMs = 2;

    public static bool InUse { get; private set; }
    internal static bool Streaming => InUse && _active;

    private static uint _statusBase;
    private static int _slots;
    private static uint _dataBase;

    private static volatile bool _active;
    private static volatile bool _reading;
    private static int _pendingLba = -1;
    private static int _streamLba = -1;
    private static int _streamStartLba;
    private static int _generation;
    private static long _starvedSince = -1;
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
        lock (_lock)
        {
            _active = false;
            _reading = false;
            _generation++;
            Monitor.PulseAll(_lock);
        }

        LibCd.ClearStreamCallbacks();
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
        while (true)
        {
            lock (_lock)
            {
                if (_prevStart >= 0)
                {
                    for (var i = 0; i < _prevN; i++) _busy[_prevStart + i] = false;
                    _prevStart = -1;
                }

                if (!_active)
                {
                    c.V0 = 1;
                    return;
                }

                if (_ready.Count > 0)
                {
                    _starvedSince = -1;
                    var (start, n) = _ready.Dequeue();
                    m.WriteU32(c.A0, _dataBase + (uint)(start * SlotData));
                    m.WriteU32(c.A1, _statusBase + (uint)(start * HeaderSize));
                    _prevStart = start;
                    _prevN = n;
                    c.V0 = 0;
                    return;
                }

                var now = Environment.TickCount64;
                if (_starvedSince < 0) _starvedSince = now;
                if (!_reading || now - _starvedSince >= StarveMs)
                {
                    c.V0 = 1;
                    return;
                }

                Monitor.Wait(_lock, StarvePollMs);
            }

            Interrupts.Poll(c, m);
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
            _starvedSince = -1;
            _generation++;
            _busy = Array.Empty<bool>();
            _ready.Clear();
            _clock.Reset();
            Monitor.PulseAll(_lock);
        }
    }

    private static void ResetRing(IMemory m)
    {
        _generation++;
        _primed = false;
        _writeIdx = 0;
        _prevStart = -1;
        _prevN = 0;
        _starvedSince = -1;
        _ready.Clear();
        _busy = _slots > 0 ? new bool[_slots] : Array.Empty<bool>();
        for (var i = 0; i < _slots; i++)
            m.WriteU16(_statusBase + (uint)(i * HeaderSize), 0);
        Monitor.PulseAll(_lock);
    }

    private static void EnsureThread()
    {
        if (_thread is { IsAlive: true }) return;
        _run = true;
        _thread = new Thread(StreamLoop) { IsBackground = true, Name = "CdStream" };
        _thread.Start();
    }

    private static bool Advance(int generation, int lba)
    {
        lock (_lock)
        {
            if (generation != _generation) return false;
            _streamLba = lba;
            return true;
        }
    }

    private static byte[]? ReadSector(Cdrom.CdController cd, int lba)
    {
        try
        {
            lock (LibCd.DiscLock)
            {
                return cd.ReadSectorData(lba, 2336);
            }
        }
        catch
        {
            return null;
        }
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

            int generation, lba;
            lock (_lock)
            {
                if (_streamLba < 0)
                {
                    _streamLba = _pendingLba >= 0 ? _pendingLba : LibCd.CurrentLba;
                    _streamStartLba = _streamLba;
                    _clock.Restart();
                }

                generation = _generation;
                lba = _streamLba;
            }

            if (lba >= cd.Fs.DataSectors)
            {
                _reading = false;
                continue;
            }

            var sec = ReadSector(cd, lba);
            if (sec == null)
            {
                Thread.Sleep(2);
                continue;
            }

            if ((sec[2] & 0x04) != 0)
            {
                if (Advance(generation, lba + 1)) Assets.Xa.XaRouter.Sector(lba, sec, true);
                continue;
            }

            int n = Read16(sec, 14);
            if (Read16(sec, 8) != VideoMagic || Read16(sec, 12) != 0 || n <= 0 || n > _slots)
            {
                Advance(generation, lba + 1);
                continue;
            }

            if (_primed && lba - _streamStartLba + n > _clock.Elapsed.TotalSeconds * LibCd.SectorsPerSecond)
            {
                Thread.Sleep(1);
                continue;
            }

            int start;
            bool free;
            lock (_lock)
            {
                if (generation != _generation) continue;
                if (_writeIdx + n > _slots) _writeIdx = 0;
                start = _writeIdx;
                free = true;
                for (var i = 0; i < n; i++)
                    if (_busy[start + i])
                    {
                        free = false;
                        break;
                    }
            }

            if (!free)
            {
                Thread.Sleep(1);
                continue;
            }

            var next = CollectFrame(cd, m, generation, lba, start, n);
            if (next < 0) continue;

            lock (_lock)
            {
                if (generation != _generation) continue;
                _streamLba = next;
                for (var i = 0; i < n; i++) _busy[start + i] = true;
                _ready.Enqueue((start, n));
                _writeIdx = start + n;

                if (!_primed && _ready.Count >= PrimeFrames)
                {
                    _primed = true;
                    _streamStartLba = _streamLba;
                    _clock.Restart();
                }

                Monitor.PulseAll(_lock);
            }
        }
    }

    private static int CollectFrame(Cdrom.CdController cd, IMemory m, int generation, int lba, int start, int n)
    {
        var collected = 0;
        while (collected < n)
        {
            var sec = ReadSector(cd, lba);
            if (sec == null) return -1;
            var current = lba++;

            if ((sec[2] & 0x04) != 0)
            {
                if (generation != _generation) return -1;
                Assets.Xa.XaRouter.Sector(current, sec, true);
                continue;
            }

            if (Read16(sec, 8) != VideoMagic) continue;

            lock (_lock)
            {
                if (generation != _generation) return -1;
                var hdr = _statusBase + (uint)((start + collected) * HeaderSize);
                var dat = _dataBase + (uint)((start + collected) * SlotData);
                for (var j = 0; j < HeaderSize; j++) m.WriteU8(hdr + (uint)j, sec[8 + j]);
                for (var j = 0; j < SlotData; j++) m.WriteU8(dat + (uint)j, sec[8 + HeaderSize + j]);
            }

            collected++;
        }

        return lba;
    }

    private static ushort Read16(byte[] b, int o)
    {
        return (ushort)(b[o] | (b[o + 1] << 8));
    }
}
