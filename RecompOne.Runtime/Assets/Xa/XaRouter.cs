namespace RecompOne.Runtime.Assets.Xa;

public static class XaRouter
{
    private const int MaxGap = 512;
    private const int NewRunLbaSlack = 64;
    private const int ResumeWindowMs = 2000;

    private static readonly object _gate = new();
    private static readonly int[] _frames = new int[4032];

    private static XaEntry? _entry;
    private static ReplacementStream? _stream;
    private static byte _file, _channel;
    private static int _lastLba = int.MinValue;
    private static int _runLba;
    private static double _runSeconds;
    private static bool _rebind;
    private static int _startLba;
    private static int _accepted;
    private static int _outRate = 37800;
    private static bool _outStereo = true;
    private static bool _exhausted;
    private static long _tailDeadline;
    private static long _lastSectorTick;

    public static bool Active
    {
        get
        {
            lock (_gate)
            {
                return _stream != null;
            }
        }
    }

    public static string? ActiveName
    {
        get
        {
            lock (_gate)
            {
                return _stream?.Name;
            }
        }
    }

    public static string? ActiveEntry
    {
        get
        {
            lock (_gate)
            {
                return _entry?.ToString();
            }
        }
    }

    public static void Reset()
    {
        lock (_gate)
        {
            DropStream();
            XaTrack.Clear();
            _lastLba = int.MinValue;
            _accepted = 0;
            _runSeconds = 0;
            _rebind = false;
        }
    }

    public static void Rebind()
    {
        lock (_gate)
        {
            DropStream();
            _rebind = _lastLba != int.MinValue;
        }
    }

    private static void DropStream()
    {
        try
        {
            _stream?.Dispose();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[assets] xa: dispose failed: {ex.Message}");
        }

        _stream = null;
        _entry = null;
        _exhausted = false;
        _tailDeadline = 0;
    }

    private static void Fail(Exception ex)
    {
        Console.Error.WriteLine($"[assets] xa: '{_stream?.Name}' failed, falling back to the original audio: {ex.Message}");
        AssetReplacerManager.Instance.Stats.XaOpenFailures++;
        DropStream();
    }

    public static void Sector(int lba, byte[] sec, bool fromStr)
    {
        var file = sec[0];
        var channel = sec[1];
        var coding = sec[3];

        lock (_gate)
        {
            var newRun = _lastLba == int.MinValue || file != _file || channel != _channel ||
                         lba < _lastLba || lba > _lastLba + MaxGap;

            if (newRun)
            {
                _rebind = false;
                BeginRun(lba, file, channel, sec, fromStr);
            }
            else if (_rebind)
            {
                _rebind = false;
                RebindRun(fromStr);
            }

            XaRegistry.Note(_runLba, lba, file, channel, coding, newRun);

            _lastLba = lba;
            _lastSectorTick = Environment.TickCount64;
            _accepted++;

            if (_entry != null && _accepted > 8 && lba > _startLba)
                _entry.Interleave = (lba - _startLba) / (double)(_accepted - 1);

            var stereo = (coding & 0x01) != 0;
            var rate = (coding & 0x04) != 0 ? 18900 : 37800;
            var want = stereo ? 2016 : 4032;
            _outRate = rate;
            _outStereo = stereo;
            _runSeconds += want / (double)rate;

            if (PumpDirect()) return;

            if (_stream == null || _exhausted || !Fill(want, rate))
            {
                XaAudio.DecodeSector(sec, 8, coding);
                AssetReplacerManager.Instance.Stats.XaPassthrough++;
                return;
            }

            XaAudio.PushFrames(_frames, want, rate);
            AssetReplacerManager.Instance.Stats.XaReplaced++;

            if (_stream.Options.TailMs > 0)
                _tailDeadline = Environment.TickCount64 + _stream.Options.TailMs;
        }
    }

    private static bool Fill(int want, int rate)
    {
        try
        {
            var got = _stream!.ReadPacked(_frames, want, rate);
            if (got < want)
                switch (_stream.Options.OnShorter)
                {
                    case ShortPolicy.Loop:
                        _stream.SeekSeconds(_stream.Options.LoopStart);
                        got += _stream.ReadPacked(_frames, got, want - got, rate);
                        if (got < want) Array.Clear(_frames, got, want - got);
                        break;
                    case ShortPolicy.EndStream:
                        Array.Clear(_frames, got, want - got);
                        _exhausted = true;
                        break;
                    default:
                        Array.Clear(_frames, got, want - got);
                        break;
                }

            return true;
        }
        catch (Exception ex)
        {
            Fail(ex);
            return false;
        }
    }

    private static void BeginRun(int lba, byte file, byte channel, byte[] sec, bool fromStr)
    {
        var mgr = AssetReplacerManager.Instance;
        mgr.Stats.XaRuns++;

        var entry = Resolve(lba, file, channel, sec, out var exact);

        if (_stream != null && !_exhausted && ReferenceEquals(entry, _entry) && !_stream.Ended &&
            Environment.TickCount64 - _lastSectorTick <= ResumeWindowMs)
        {
            _accepted = 0;
            _startLba = lba;
            _file = file;
            _channel = channel;
            return;
        }

        DropStream();
        _accepted = 0;
        _startLba = lba;
        _runLba = lba;
        _runSeconds = 0;
        _file = file;
        _channel = channel;

        if (entry == null) return;
        Open(entry, fromStr, exact ? 0 : OffsetSeconds(entry, lba));
    }

    private static void RebindRun(bool fromStr)
    {
        var entry = Resolve(_runLba, _file, _channel, null, out var exact);
        if (entry == null) return;
        Open(entry, fromStr, (exact ? 0 : OffsetSeconds(entry, _runLba)) + _runSeconds);
    }

    private static XaEntry? Resolve(int runLba, byte file, byte channel, byte[]? first, out bool exact)
    {
        var mgr = AssetReplacerManager.Instance;
        exact = false;
        if (!mgr.Enabled) return null;

        if (mgr.HasXaHashes)
        {
            var byTrack = mgr.ResolveXaByPayload(XaTrack.Hash(runLba, file, channel));
            if (byTrack != null)
            {
                exact = true;
                return byTrack;
            }
        }

        var byLba = mgr.ResolveXa(file, channel, runLba);
        if (byLba != null || first == null) return byLba;

        var probe = AssetHash.XaPayload(first.AsSpan(8, Math.Min(2304, Math.Max(0, first.Length - 8))));
        var bySector = mgr.ResolveXaByPayload(probe);
        exact = bySector != null;
        return bySector;
    }

    private static double OffsetSeconds(XaEntry entry, int lba)
    {
        var delta = lba - entry.StartLba;
        if (delta <= NewRunLbaSlack) return 0;
        var interleave = entry.Interleave <= 0 ? 1 : entry.Interleave;
        return delta / interleave * 2016 / 37800.0;
    }

    private static void Open(XaEntry entry, bool fromStr, double seek)
    {
        if (fromStr && !entry.Options.AllowStr)
        {
            Log.Sdk($"[assets] xa: '{entry.AudioName}' skip in STR context (allowStr is false)");
            return;
        }

        var stream = AssetReplacerManager.Instance.OpenStream(entry);
        if (stream == null) return;

        try
        {
            var duration = stream.DurationSeconds;
            if (duration > 0 && seek >= duration)
            {
                var loopStart = Math.Clamp(entry.Options.LoopStart, 0, duration);
                var loopEnd = entry.Options.LoopEnd > loopStart ? Math.Min(entry.Options.LoopEnd, duration) : duration;
                var span = loopEnd - loopStart;
                if (!entry.Options.Loop || span <= 0)
                {
                    stream.Dispose();
                    return;
                }

                seek = loopStart + (seek - loopStart) % span;
            }

            if (seek > 0) stream.SeekSeconds(seek);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[assets] xa: cannot seek '{entry.AudioName}' to {seek:0.00}s: {ex.Message}");
            stream.Dispose();
            return;
        }

        _entry = entry;
        _stream = stream;
        entry.TimesPlayed++;

        //Console.WriteLine($"[assets] xa: f{file} c{channel} lba={lba} -> '{entry.AudioName}' ({entry.PackId})" +(seek > 0 ? $" seek={seek:0.00}s" : ""));
    }

    public static bool WantsCarrier(out int rewindLba)
    {
        rewindLba = 0;
        lock (_gate)
        {
            if (_stream == null || _exhausted) return false;
            if (!_stream.Options.Extend) return false;
            if (_stream.Ended) return false;
            if (_stream.PositionSeconds * 1000 > _stream.Options.ExtendMaxMs) return false;
            rewindLba = _startLba;
            return true;
        }
    }

    public static bool PumpDirect()
    {
        lock (_gate)
        {
            if (_stream == null || !_stream.Options.Direct) return false;
            int buffered = XaAudio.BufferedSamples;
            if (buffered >= 4096) return true;
            int want = Math.Min(_frames.Length, 4096 - buffered);
            try
            {
                int got = _stream.ReadPacked(_frames, want, _outRate);
                if (got < want) Array.Clear(_frames, got, want - got);
            }
            catch (Exception ex)
            {
                Fail(ex);
                return false;
            }

            XaAudio.PushFrames(_frames, want, _outRate);
            return true;
        }
    }

    public static void StopDirect()
    {
        lock (_gate)
        {
            if (_stream?.Options.Direct == true) Reset();
        }
    }

    public static bool PumpTail()
    {
        lock (_gate)
        {
            if (_stream == null || _exhausted) return false;
            if (_tailDeadline == 0 || Environment.TickCount64 > _tailDeadline) return false;
            if (XaAudio.BufferedSamples > 4096) return true;

            var want = _outStereo ? 2016 : 4032;
            int got;
            try
            {
                got = _stream.ReadPacked(_frames, want, _outRate);
            }
            catch (Exception ex)
            {
                Fail(ex);
                return false;
            }

            if (got <= 0)
            {
                _tailDeadline = 0;
                return false;
            }

            if (got < want) Array.Clear(_frames, got, want - got);
            XaAudio.PushFrames(_frames, want, _outRate);
            return true;
        }
    }
}
