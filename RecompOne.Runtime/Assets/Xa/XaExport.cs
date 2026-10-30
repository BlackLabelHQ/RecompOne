namespace RecompOne.Runtime.Assets.Xa;

public static class XaExport
{
    private const int SectorBytes = 2336;
    private const int MaxMissRun = 256;
    private const int MaxSectors = 200000;

    private static readonly int[] Pos = [0, 60, 115, 98];
    private static readonly int[] Neg = [0, 0, -52, -55];

    public static AudioClip? Decode(int startLba, byte file, byte channel, double maxSeconds = 900)
    {
        var cd = Runtime.Cd;
        if (cd == null) return null;

        var pcm = new List<short>();
        int oldL = 0, olderL = 0, oldR = 0, olderR = 0;
        var stereo = true;
        var rate = 37800;
        var found = false;
        var misses = 0;
        var l = new int[28];
        var r = new int[28];

        for (var lba = startLba; lba < startLba + MaxSectors; lba++)
        {
            byte[] sec;
            try
            {
                lock (Sdk.LibCd.DiscLock)
                {
                    sec = cd.ReadSectorData(lba, SectorBytes);
                }
            }
            catch
            {
                break;
            }

            if (sec.Length < SectorBytes) break;

            var submode = sec[2];
            var audio = (submode & 0x04) != 0 && (submode & 0x20) != 0;
            if (!audio || sec[0] != file || sec[1] != channel)
            {
                if (found && ++misses > MaxMissRun) break;
                continue;
            }

            misses = 0;
            var coding = sec[3];
            if (!found)
            {
                stereo = (coding & 0x01) != 0;
                rate = (coding & 0x04) != 0 ? 18900 : 37800;
                found = true;
            }

            for (var p = 0; p < 18; p++)
            {
                var b = 8 + p * 128;
                if (stereo)
                    for (var tb = 0; tb < 4; tb++)
                    {
                        DecodeBlock(sec, b, tb * 2, ref oldL, ref olderL, l);
                        DecodeBlock(sec, b, tb * 2 + 1, ref oldR, ref olderR, r);
                        for (var j = 0; j < 28; j++)
                        {
                            pcm.Add((short)l[j]);
                            pcm.Add((short)r[j]);
                        }
                    }
                else
                    for (var blk = 0; blk < 8; blk++)
                    {
                        DecodeBlock(sec, b, blk, ref oldL, ref olderL, l);
                        for (var j = 0; j < 28; j++) pcm.Add((short)l[j]);
                    }
            }

            if ((submode & 0x80) != 0) break;
            if (pcm.Count / (stereo ? 2 : 1) > maxSeconds * rate) break;
        }

        return found ? new AudioClip { Pcm = pcm.ToArray(), Channels = stereo ? 2 : 1, Rate = rate } : null;
    }

    private static void DecodeBlock(byte[] sec, int b, int blk, ref int old, ref int older, int[] dst)
    {
        var hdr = sec[b + 4 + blk];
        var sv = hdr & 0xF;
        if (sv > 12) sv = 9;
        var filter = (hdr >> 4) & 0x3;
        int f0 = Pos[filter], f1 = Neg[filter];
        int col = blk >> 1, nshift = (blk & 1) * 4;
        for (var j = 0; j < 28; j++)
        {
            var nib = (sec[b + 16 + 4 * j + col] >> nshift) & 0xF;
            var t = nib >= 8 ? nib - 16 : nib;
            var s = Math.Clamp(((t << 12) >> sv) + ((old * f0 + older * f1 + 32) >> 6), -32768, 32767);
            older = old;
            old = s;
            dst[j] = s;
        }
    }
}
