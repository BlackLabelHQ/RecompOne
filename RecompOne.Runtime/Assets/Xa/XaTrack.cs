namespace RecompOne.Runtime.Assets.Xa;

//try recover track by label not by listen adress
public static class XaTrack
{
    private const int ScanSectors = 512;
    private const int HashSectors = 8;
    private const int MaxGap = 64;
    private const int SoundGroups = 18;
    private const int GroupBytes = 128;
    private const int GroupHeader = 16;
    private const int PayloadBytes = SoundGroups * GroupBytes;

    private static readonly object _gate = new();
    private static readonly Dictionary<(int, byte, byte), ulong> _hashes = new();

    public static void Clear()
    {
        lock (_gate)
        {
            _hashes.Clear();
        }
    }

    public static ulong Hash(int lba, byte file, byte channel)
    {
        var key = (lba, file, channel);
        lock (_gate)
        {
            if (_hashes.TryGetValue(key, out var cached)) return cached;
        }

        var hash = Compute(lba, file, channel);
        lock (_gate)
        {
            _hashes[key] = hash;
        }

        return hash;
    }

    private static ulong Compute(int lba, byte file, byte channel)
    {
        var cd = Runtime.Cd;
        if (cd == null) return 0;

        var buffer = new byte[HashSectors * (PayloadBytes + 1)];
        var used = 0;
        var picked = 0;
        var last = lba;

        try
        {
            for (var at = lba; at < lba + ScanSectors && picked < HashSectors && at - last <= MaxGap; at++)
            {
                byte[] sec;
                lock (Sdk.LibCd.DiscLock)
                {
                    sec = cd.ReadSectorData(at, 2336);
                }

                if (sec.Length < 8 + PayloadBytes) return 0;
                var submode = sec[2];
                if ((submode & 0x24) != 0x24 || sec[0] != file || sec[1] != channel) continue;

                last = at;
                var payload = sec.AsSpan(8, PayloadBytes);
                if (Silent(payload)) continue;

                buffer[used++] = sec[3];
                payload.CopyTo(buffer.AsSpan(used));
                used += PayloadBytes;
                picked++;
                if ((submode & 0x80) != 0) break;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[assets] xa: canot read track f{file} c{channel} @{lba}: {ex.Message}");
            return 0;
        }

        return picked == 0 ? 0 : AssetHash.XaPayload(buffer.AsSpan(0, used));
    }

    private static bool Silent(ReadOnlySpan<byte> payload)
    {
        for (var g = 0; g < SoundGroups; g++)
        {
            var data = payload.Slice(g * GroupBytes + GroupHeader, GroupBytes - GroupHeader);
            if (data.IndexOfAnyExcept((byte)0) >= 0) return false;
        }

        return true;
    }
}
