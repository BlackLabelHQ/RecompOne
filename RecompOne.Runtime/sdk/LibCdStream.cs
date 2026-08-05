using System.Diagnostics;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Sdk;

public static class LibCdStream
{
    const int HeaderSize = 32;
    const int SlotData = 2016;
    const ushort VideoMagic = 0x0160;

    public static bool InUse { get; private set; }
    static uint _statusBase;
    static int _slots;
    static uint _dataBase;

    static volatile bool _active;
    static volatile bool _reading;
    static int _pendingLba = -1;
    static int _streamLba = -1;
    static int _streamStartLba;
    static readonly Stopwatch _clock = new();

    static int _writeIdx;
    static bool[] _busy = System.Array.Empty<bool>();
    static readonly Queue<(int start, int n)> _ready = new();
    static int _prevStart = -1, _prevN;

    static Thread? _thread;
    static volatile bool _run;
    static readonly object _lock = new();

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
        lock (_lock) ResetRing(m);
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
        Log.Sdk("StSetStream");
    }

    public static void StSetMask(CpuContext c, IMemory m) { c.V0 = 0; Log.Sdk("StSetMask"); }

    public static void StGetNext(CpuContext c, IMemory m)
    {
        if (!_active) { c.V0 = 1; return; }

        lock (_lock)
        {
            if (_prevStart >= 0)
            {
                for (int i = 0; i < _prevN; i++) _busy[_prevStart + i] = false;
                _prevStart = -1;
            }

            if (_ready.Count == 0) { c.V0 = 1; return; }

            var (start, n) = _ready.Dequeue();
            uint dataPtr = _dataBase + (uint)(start * SlotData);
            uint hdrPtr = _statusBase + (uint)(start * HeaderSize);
            m.WriteU32(c.A0, dataPtr);
            m.WriteU32(c.A1, hdrPtr);
            _prevStart = start;
            _prevN = n;
            c.V0 = 0;
        }
    }

    public static void StFreeRing(CpuContext c, IMemory m) { c.V0 = 0; Log.Sdk("StFreeRing"); }

    public static void StGetBackloc(CpuContext c, IMemory m) { c.V0 = 0xFFFFFFFFu; Log.Sdk("StGetBackloc"); }


    internal static void OnReadStream(int lba)
    {
        if (!InUse) return;
        _pendingLba = lba;
        _streamLba = -1;
        _reading = true;
        EnsureThread();
    }

    internal static void OnStopStream()
    {
        _reading = false;
    }

    static void ResetRing(IMemory m)
    {
        _writeIdx = 0;
        _prevStart = -1;
        _prevN = 0;
        _ready.Clear();
        _busy = _slots > 0 ? new bool[_slots] : System.Array.Empty<bool>();
        for (int i = 0; i < _slots; i++)
            m.WriteU16(_statusBase + (uint)(i * HeaderSize), 0);
    }

    static void EnsureThread()
    {
        if (_thread is { IsAlive: true }) return;
        _run = true;
        _thread = new Thread(StreamLoop) { IsBackground = true, Name = "CdStream" };
        _thread.Start();
    }

    static void StreamLoop()
    {
        while (_run)
        {
            var cd = Runtime.Cd;
            var m = Runtime.Mem;
            if (cd == null || m == null || !_active || !_reading || _slots <= 0)
            {
                Thread.Sleep(2);
                continue;
            }

            if (_streamLba < 0)
            {
                _streamLba = _pendingLba >= 0 ? _pendingLba : LibCd.CurrentLba;
                _streamStartLba = _streamLba;
                LibCd.SetStreamLba(_streamLba);
                _clock.Restart();
            }

            // ReadS advances at the physical drive rate.  In particular, XA
            // sectors must not be decoded as quickly as the host can read them:
            // the game polls GetlocP to decide when a stream has ended.
            double delivered = _clock.Elapsed.TotalSeconds * LibCd.SectorsPerSecond;
            if ((_streamLba - _streamStartLba) + 1 > delivered)
            {
                Thread.Sleep(1);
                continue;
            }

            byte[] sec;
            try { lock (LibCd.DiscLock) sec = cd.ReadSectorData(_streamLba, 2336); }
            catch { Thread.Sleep(2); continue; }

            if ((sec[2] & 0x04) != 0)
            {
                XaAudio.DecodeSector(sec, 8, sec[3]);
                _streamLba++;
                LibCd.SetStreamLba(_streamLba);
                continue;
            }
            if (Read16(sec, 8) != VideoMagic || Read16(sec, 12) != 0)
            {
                _streamLba++;
                LibCd.SetStreamLba(_streamLba);
                continue;
            }

            int n = Read16(sec, 14);
            if (n <= 0 || n > _slots) { _streamLba++; continue; }

            if ((_streamLba - _streamStartLba) + n > delivered) { Thread.Sleep(1); continue; }

            int start;
            lock (_lock)
            {
                if (_writeIdx + n > _slots) _writeIdx = 0;
                start = _writeIdx;
                bool free = true;
                for (int i = 0; i < n; i++) if (_busy[start + i]) { free = false; break; }
                if (!free) { Thread.Sleep(1); continue; }
            }

            if (!CollectFrame(cd, m, start, n)) continue;

            lock (_lock)
            {
                for (int i = 0; i < n; i++) _busy[start + i] = true;
                _ready.Enqueue((start, n));
                _writeIdx = start + n;
            }
        }
    }

    static bool CollectFrame(Cdrom.CdController cd, IMemory m, int start, int n)
    {
        int collected = 0;
        int lba = _streamLba;
        while (collected < n)
        {
            byte[] sec;
            try { lock (LibCd.DiscLock) sec = cd.ReadSectorData(lba, 2336); }
            catch { return false; }
            lba++;

            if ((sec[2] & 0x04) != 0) { XaAudio.DecodeSector(sec, 8, sec[3]); continue; }
            if (Read16(sec, 8) != VideoMagic) continue;

            uint hdr = _statusBase + (uint)((start + collected) * HeaderSize);
            uint dat = _dataBase + (uint)((start + collected) * SlotData);
            for (int j = 0; j < HeaderSize; j++) m.WriteU8(hdr + (uint)j, sec[8 + j]);
            for (int j = 0; j < SlotData; j++) m.WriteU8(dat + (uint)j, sec[8 + HeaderSize + j]);
            collected++;
        }
        _streamLba = lba;
        LibCd.SetStreamLba(_streamLba);
        Thread.MemoryBarrier();
        return true;
    }

    static ushort Read16(byte[] b, int o) => (ushort)(b[o] | (b[o + 1] << 8));
}
