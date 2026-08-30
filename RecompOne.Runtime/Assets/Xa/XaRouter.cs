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
            _stream?.Dispose();
            _stream = null;
            _entry = null;
            _lastLba = int.MinValue;
            _accepted = 0;
            _exhausted = false;
            _tailDeadline = 0;
        }
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

            if (newRun) BeginRun(lba, file, channel, sec, fromStr);

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

            if (_stream == null || _exhausted)
            {
                XaAudio.DecodeSector(sec, 8, coding);
                AssetReplacerManager.Instance.Stats.XaPassthrough++;
                return;
            }

            var got = _stream.ReadPacked(_frames, want, rate);
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

            XaAudio.PushFrames(_frames, want, rate);
            AssetReplacerManager.Instance.Stats.XaReplaced++;

            if (_stream.Options.TailMs > 0)
                _tailDeadline = Environment.TickCount64 + _stream.Options.TailMs;
        }
    }

    private static void BeginRun(int lba, byte file, byte channel, byte[] sec, bool fromStr)
    {
        var mgr = AssetReplacerManager.Instance;
        mgr.Stats.XaRuns++;

        var entry = mgr.ResolveXa(file, channel, lba);
        if (entry == null)
        {
            var probe = AssetHash.XaPayload(sec.AsSpan(8, Math.Min(2304, Math.Max(0, sec.Length - 8))));
            entry = mgr.ResolveXaByPayload(probe);
        }

        if (_stream != null && !_exhausted && ReferenceEquals(entry, _entry) && !_stream.Ended &&
            Environment.TickCount64 - _lastSectorTick <= ResumeWindowMs)
        {
            _accepted = 0;
            _startLba = lba;
            _file = file;
            _channel = channel;
            return;
        }

        _stream?.Dispose();
        _stream = null;
        _entry = null;
        _exhausted = false;
        _accepted = 0;
        _startLba = lba;
        _file = file;
        _channel = channel;

        if (entry == null) return;

        if (fromStr && !entry.Options.AllowStr)
        {
            Log.Sdk($"[assets] xa: '{entry.AudioName}' skip in STR context (allowStr is false)");
            return;
        }

        var stream = mgr.OpenStream(entry);
        if (stream == null) return;

        double seek = 0;
        var delta = lba - entry.StartLba;
        if (delta > NewRunLbaSlack)
        {
            double framesPerSector = 2016;
            var interleave = entry.Interleave <= 0 ? 1 : entry.Interleave;
            seek = delta / interleave * framesPerSector / 37800.0;
        }

        if (seek > 0) stream.SeekSeconds(seek);

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

    public static bool PumpTail()
    {
        lock (_gate)
        {
            if (_stream == null || _exhausted) return false;
            if (_tailDeadline == 0 || Environment.TickCount64 > _tailDeadline) return false;
            if (XaAudio.BufferedSamples > 4096) return true;

            var want = _outStereo ? 2016 : 4032;
            var got = _stream.ReadPacked(_frames, want, _outRate);
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