namespace RecompOne.Runtime.Assets.Xa;

public static class CddaRouter
{
    private const int OutRate = 44100;

    private static readonly object _gate = new();
    private static ReplacementStream? _stream;
    private static DiscAudioTrack? _track;
    private static int _lastLba = int.MinValue;
    private static bool _exhausted;

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

    public static void Stop()
    {
        lock (_gate)
        {
            Drop();
            _track = null;
            _lastLba = int.MinValue;
        }
    }

    public static void Rebind()
    {
        lock (_gate)
        {
            Drop();
            _track = null;
        }
    }

    public static bool Fill(int lba, int[] frames, int count)
    {
        lock (_gate)
        {
            if (lba != _lastLba + 1 || _track == null || !_track.Contains(lba)) Begin(lba, lba != _lastLba + 1);
            _lastLba = lba;
            if (_stream == null) return false;
            if (!_exhausted) return Read(frames, count);
            Array.Clear(frames, 0, count);
            return true;
        }
    }

    private static void Begin(int lba, bool jumped)
    {
        var track = DiscAudio.FindCdda(lba);
        var same = ReferenceEquals(track, _track) && _stream != null && !jumped;
        if (same) return;

        Drop();
        _track = track;
        if (track == null) return;
        DiscAudio.Played(track);

        var entry = AssetReplacerManager.Instance.ResolveCdda(track.Number);
        if (entry == null) return;
        var stream = AssetReplacerManager.Instance.OpenStream(entry);
        if (stream == null) return;

        try
        {
            var seek = track.SecondsAt(lba);
            var duration = stream.DurationSeconds;
            if (duration > 0 && seek >= duration)
            {
                var loopStart = Math.Clamp(entry.Options.LoopStart, 0, duration);
                var span = duration - loopStart;
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
            Console.Error.WriteLine($"[assets] cdda: cannot seek '{entry.AudioName}': {ex.Message}");
            stream.Dispose();
            return;
        }

        _stream = stream;
        entry.TimesPlayed++;
    }

    private static bool Read(int[] frames, int count)
    {
        try
        {
            var got = _stream!.ReadPacked(frames, count, OutRate);
            if (got >= count) return true;
            Array.Clear(frames, got, count - got);
            if (_stream.Ended) _exhausted = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[assets] cdda: '{_stream?.Name}' failed, falling back to the disc audio: {ex.Message}");
            AssetReplacerManager.Instance.Stats.XaOpenFailures++;
            Drop();
            return false;
        }
    }

    private static void Drop()
    {
        try
        {
            _stream?.Dispose();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[assets] cdda: dispose failed: {ex.Message}");
        }

        _stream = null;
        _exhausted = false;
    }
}
