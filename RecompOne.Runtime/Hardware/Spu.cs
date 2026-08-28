using System.Runtime.CompilerServices;

namespace RecompOne.Runtime;

public sealed class Spu
{
    public const int RamSize = 512 * 1024;
    public readonly byte[] Ram = new byte[RamSize];

    const uint Base = 0x1F801C00u;

    static readonly int[] K0 = { 0,  60, 115,  98, 122 };
    static readonly int[] K1 = { 0,   0, -52, -55, -60 };

    static readonly int[] AdsrStepDown = { -8, -7, -6, -5 };
    static readonly int[] AdsrStepUp = { 7, 6, 5, 4 };


    static readonly short[] Gauss = {
        -0x001, -0x001, -0x001, -0x001, -0x001, -0x001, -0x001, -0x001, //
        -0x001, -0x001, -0x001, -0x001, -0x001, -0x001, -0x001, -0x001, //
        0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0001, //
        0x0001, 0x0001, 0x0001, 0x0002, 0x0002, 0x0002, 0x0003, 0x0003, //
        0x0003, 0x0004, 0x0004, 0x0005, 0x0005, 0x0006, 0x0007, 0x0007, //
        0x0008, 0x0009, 0x0009, 0x000A, 0x000B, 0x000C, 0x000D, 0x000E, //
        0x000F, 0x0010, 0x0011, 0x0012, 0x0013, 0x0015, 0x0016, 0x0018, // entry
        0x0019, 0x001B, 0x001C, 0x001E, 0x0020, 0x0021, 0x0023, 0x0025, // 000..07F
        0x0027, 0x0029, 0x002C, 0x002E, 0x0030, 0x0033, 0x0035, 0x0038, //
        0x003A, 0x003D, 0x0040, 0x0043, 0x0046, 0x0049, 0x004D, 0x0050, //
        0x0054, 0x0057, 0x005B, 0x005F, 0x0063, 0x0067, 0x006B, 0x006F, //
        0x0074, 0x0078, 0x007D, 0x0082, 0x0087, 0x008C, 0x0091, 0x0096, //
        0x009C, 0x00A1, 0x00A7, 0x00AD, 0x00B3, 0x00BA, 0x00C0, 0x00C7, //
        0x00CD, 0x00D4, 0x00DB, 0x00E3, 0x00EA, 0x00F2, 0x00FA, 0x0101, //
        0x010A, 0x0112, 0x011B, 0x0123, 0x012C, 0x0135, 0x013F, 0x0148, //
        0x0152, 0x015C, 0x0166, 0x0171, 0x017B, 0x0186, 0x0191, 0x019C, //
        0x01A8, 0x01B4, 0x01C0, 0x01CC, 0x01D9, 0x01E5, 0x01F2, 0x0200, //
        0x020D, 0x021B, 0x0229, 0x0237, 0x0246, 0x0255, 0x0264, 0x0273, //
        0x0283, 0x0293, 0x02A3, 0x02B4, 0x02C4, 0x02D6, 0x02E7, 0x02F9, //
        0x030B, 0x031D, 0x0330, 0x0343, 0x0356, 0x036A, 0x037E, 0x0392, //
        0x03A7, 0x03BC, 0x03D1, 0x03E7, 0x03FC, 0x0413, 0x042A, 0x0441, //
        0x0458, 0x0470, 0x0488, 0x04A0, 0x04B9, 0x04D2, 0x04EC, 0x0506, //
        0x0520, 0x053B, 0x0556, 0x0572, 0x058E, 0x05AA, 0x05C7, 0x05E4, // entry
        0x0601, 0x061F, 0x063E, 0x065C, 0x067C, 0x069B, 0x06BB, 0x06DC, // 080..0FF
        0x06FD, 0x071E, 0x0740, 0x0762, 0x0784, 0x07A7, 0x07CB, 0x07EF, //
        0x0813, 0x0838, 0x085D, 0x0883, 0x08A9, 0x08D0, 0x08F7, 0x091E, //
        0x0946, 0x096F, 0x0998, 0x09C1, 0x09EB, 0x0A16, 0x0A40, 0x0A6C, //
        0x0A98, 0x0AC4, 0x0AF1, 0x0B1E, 0x0B4C, 0x0B7A, 0x0BA9, 0x0BD8, //
        0x0C07, 0x0C38, 0x0C68, 0x0C99, 0x0CCB, 0x0CFD, 0x0D30, 0x0D63, //
        0x0D97, 0x0DCB, 0x0E00, 0x0E35, 0x0E6B, 0x0EA1, 0x0ED7, 0x0F0F, //
        0x0F46, 0x0F7F, 0x0FB7, 0x0FF1, 0x102A, 0x1065, 0x109F, 0x10DB, //
        0x1116, 0x1153, 0x118F, 0x11CD, 0x120B, 0x1249, 0x1288, 0x12C7, //
        0x1307, 0x1347, 0x1388, 0x13C9, 0x140B, 0x144D, 0x1490, 0x14D4, //
        0x1517, 0x155C, 0x15A0, 0x15E6, 0x162C, 0x1672, 0x16B9, 0x1700, //
        0x1747, 0x1790, 0x17D8, 0x1821, 0x186B, 0x18B5, 0x1900, 0x194B, //
        0x1996, 0x19E2, 0x1A2E, 0x1A7B, 0x1AC8, 0x1B16, 0x1B64, 0x1BB3, //
        0x1C02, 0x1C51, 0x1CA1, 0x1CF1, 0x1D42, 0x1D93, 0x1DE5, 0x1E37, //
        0x1E89, 0x1EDC, 0x1F2F, 0x1F82, 0x1FD6, 0x202A, 0x207F, 0x20D4, //
        0x2129, 0x217F, 0x21D5, 0x222C, 0x2282, 0x22DA, 0x2331, 0x2389, // entry
        0x23E1, 0x2439, 0x2492, 0x24EB, 0x2545, 0x259E, 0x25F8, 0x2653, // 100..17F
        0x26AD, 0x2708, 0x2763, 0x27BE, 0x281A, 0x2876, 0x28D2, 0x292E, //
        0x298B, 0x29E7, 0x2A44, 0x2AA1, 0x2AFF, 0x2B5C, 0x2BBA, 0x2C18, //
        0x2C76, 0x2CD4, 0x2D33, 0x2D91, 0x2DF0, 0x2E4F, 0x2EAE, 0x2F0D, //
        0x2F6C, 0x2FCC, 0x302B, 0x308B, 0x30EA, 0x314A, 0x31AA, 0x3209, //
        0x3269, 0x32C9, 0x3329, 0x3389, 0x33E9, 0x3449, 0x34A9, 0x3509, //
        0x3569, 0x35C9, 0x3629, 0x3689, 0x36E8, 0x3748, 0x37A8, 0x3807, //
        0x3867, 0x38C6, 0x3926, 0x3985, 0x39E4, 0x3A43, 0x3AA2, 0x3B00, //
        0x3B5F, 0x3BBD, 0x3C1B, 0x3C79, 0x3CD7, 0x3D35, 0x3D92, 0x3DEF, //
        0x3E4C, 0x3EA9, 0x3F05, 0x3F62, 0x3FBD, 0x4019, 0x4074, 0x40D0, //
        0x412A, 0x4185, 0x41DF, 0x4239, 0x4292, 0x42EB, 0x4344, 0x439C, //
        0x43F4, 0x444C, 0x44A3, 0x44FA, 0x4550, 0x45A6, 0x45FC, 0x4651, //
        0x46A6, 0x46FA, 0x474E, 0x47A1, 0x47F4, 0x4846, 0x4898, 0x48E9, //
        0x493A, 0x498A, 0x49D9, 0x4A29, 0x4A77, 0x4AC5, 0x4B13, 0x4B5F, //
        0x4BAC, 0x4BF7, 0x4C42, 0x4C8D, 0x4CD7, 0x4D20, 0x4D68, 0x4DB0, //
        0x4DF7, 0x4E3E, 0x4E84, 0x4EC9, 0x4F0E, 0x4F52, 0x4F95, 0x4FD7, // entry
        0x5019, 0x505A, 0x509A, 0x50DA, 0x5118, 0x5156, 0x5194, 0x51D0, // 180..1FF
        0x520C, 0x5247, 0x5281, 0x52BA, 0x52F3, 0x532A, 0x5361, 0x5397, //
        0x53CC, 0x5401, 0x5434, 0x5467, 0x5499, 0x54CA, 0x54FA, 0x5529, //
        0x5558, 0x5585, 0x55B2, 0x55DE, 0x5609, 0x5632, 0x565B, 0x5684, //
        0x56AB, 0x56D1, 0x56F6, 0x571B, 0x573E, 0x5761, 0x5782, 0x57A3, //
        0x57C3, 0x57E2, 0x57FF, 0x581C, 0x5838, 0x5853, 0x586D, 0x5886, //
        0x589E, 0x58B5, 0x58CB, 0x58E0, 0x58F4, 0x5907, 0x5919, 0x592A, //
        0x593A, 0x5949, 0x5958, 0x5965, 0x5971, 0x597C, 0x5986, 0x598F, //
        0x5997, 0x599E, 0x59A4, 0x59A9, 0x59AD, 0x59B0, 0x59B2, 0x59B3  //
    };

    public enum AdsrPhase { Off, Attack, Decay, Sustain, Release }

    public struct VoiceDebug
    {
        public AdsrPhase Phase;
        public short AdsrVol;
        public ushort VolL, VolR, Pitch, StartAddr, RepeatAddr;
        public uint CurAddr;
        public bool EndX, Noise, Pmod, Reverb;
    }

    public struct SpuDebug
    {
        public ushort MainVolL, MainVolR, ReverbVolL, ReverbVolR;
        public ushort CdVolL, CdVolR, ExtVolL, ExtVolR;
        public ushort Spucnt;
        public uint TransferAddr, ReverbStartAddr, Endx;
    }

    sealed class Voice
    {
        public ushort VolL, VolR;
        public ushort Pitch;
        public ushort StartAddr;
        public ushort RepeatAddr;
        public ushort AdsrLo, AdsrHi;
        public short AdsrVol;

        public uint CurAddr;
        public uint PitchCounter;
        public AdsrPhase Phase = AdsrPhase.Off;
        public int AdsrCycleCount;
        public bool EndX;
        public bool IgnoreLoop;
        public bool HasBlock;

        public int Old, Older;

        public short CurVolL, CurVolR;
        public int VolCycL, VolCycR;

        public readonly short[] Buf = new short[31];
    }

    readonly Voice[] _v = new Voice[24];
    readonly object _sync = new();

    ushort _mainVolL, _mainVolR;
    short _mainCurL, _mainCurR;
    int _mainCycL, _mainCycR;
    ushort _reverbVolL, _reverbVolR;

    readonly ushort[] _rev = new ushort[0x20]; //revcopied from duckstation
    uint _revCur;
    bool _revRight;
    int _revInL, _revInR, _revOutL, _revOutR;

    const int dAPF1 = 0, dAPF2 = 1, vIIR = 2, vCOMB1 = 3, vCOMB2 = 4, vCOMB3 = 5, vCOMB4 = 6,
              vWALL = 7, vAPF1 = 8, vAPF2 = 9, mLSAME = 10, mRSAME = 11, mLCOMB1 = 12,
              mRCOMB1 = 13, mLCOMB2 = 14, mRCOMB2 = 15, dLSAME = 16, dRSAME = 17,
              mLDIFF = 18, mRDIFF = 19, mLCOMB3 = 20, mRCOMB3 = 21, mLCOMB4 = 22,
              mRCOMB4 = 23, dLDIFF = 24, dRDIFF = 25, mLAPF1 = 26, mRAPF1 = 27,
              mLAPF2 = 28, mRAPF2 = 29, vLIN = 30, vRIN = 31;

    short R(int i) => (short)_rev[i];

    uint RevBase => (uint)_reverbStartAddr << 3;

    uint RevAddr(long byteOffset)
    {
        int b = (int)RevBase;
        int size = RamSize - b;
        if (size <= 0) return 0;
        
        int a = (int)_revCur - b + (int)byteOffset;
        if ((uint)a >= (uint)size)
        {
            a %= size;
            if (a < 0) a += size;
        }
        return (uint)(b + a) & (uint)(RamSize - 2);
    }

    short RevReadAt(int reg, long extra = 0)
    {
        uint a = RevAddr(((long)_rev[reg] << 3) + extra);
        return (short)(Ram[a] | (Ram[a + 1] << 8));
    }

    void RevWriteAt(int reg, int value)
    {
        if ((_spucnt & 0x80) == 0) return;
        uint a = RevAddr((long)_rev[reg] << 3);
        short v = (short)Math.Clamp(value, -0x8000, 0x7FFF);
        Ram[a] = (byte)v;
        Ram[a + 1] = (byte)(v >> 8);
    }

    static int RevMul(int sample, short vol) => sample * vol >> 15;

    void ReverbStep(int lin, int rin)
    {
        lin = RevMul(lin, R(vLIN));
        rin = RevMul(rin, R(vRIN));

        int sameL = RevMul(lin + RevMul(RevReadAt(dLSAME), R(vWALL)) - RevReadAt(mLSAME, -2), R(vIIR)) + RevReadAt(mLSAME, -2);
        int sameR = RevMul(rin + RevMul(RevReadAt(dRSAME), R(vWALL)) - RevReadAt(mRSAME, -2), R(vIIR)) + RevReadAt(mRSAME, -2);
        RevWriteAt(mLSAME, sameL);
        RevWriteAt(mRSAME, sameR);

        int diffL = RevMul(lin + RevMul(RevReadAt(dRDIFF), R(vWALL)) - RevReadAt(mLDIFF, -2), R(vIIR)) + RevReadAt(mLDIFF, -2);
        int diffR = RevMul(rin + RevMul(RevReadAt(dLDIFF), R(vWALL)) - RevReadAt(mRDIFF, -2), R(vIIR)) + RevReadAt(mRDIFF, -2);
        RevWriteAt(mLDIFF, diffL);
        RevWriteAt(mRDIFF, diffR);

        int outL = RevMul(RevReadAt(mLCOMB1), R(vCOMB1)) + RevMul(RevReadAt(mLCOMB2), R(vCOMB2)) + RevMul(RevReadAt(mLCOMB3), R(vCOMB3)) + RevMul(RevReadAt(mLCOMB4), R(vCOMB4));
        int outR = RevMul(RevReadAt(mRCOMB1), R(vCOMB1)) + RevMul(RevReadAt(mRCOMB2), R(vCOMB2)) + RevMul(RevReadAt(mRCOMB3), R(vCOMB3)) + RevMul(RevReadAt(mRCOMB4), R(vCOMB4));

        int tapL = RevReadAt(mLAPF1, -((long)_rev[dAPF1] << 3));
        int tapR = RevReadAt(mRAPF1, -((long)_rev[dAPF1] << 3));
        outL -= RevMul(tapL, R(vAPF1));
        outR -= RevMul(tapR, R(vAPF1));
        RevWriteAt(mLAPF1, outL);
        RevWriteAt(mRAPF1, outR);
        outL = RevMul(outL, R(vAPF1)) + tapL;
        outR = RevMul(outR, R(vAPF1)) + tapR;

        tapL = RevReadAt(mLAPF2, -((long)_rev[dAPF2] << 3));
        tapR = RevReadAt(mRAPF2, -((long)_rev[dAPF2] << 3));
        outL -= RevMul(tapL, R(vAPF2));
        outR -= RevMul(tapR, R(vAPF2));
        RevWriteAt(mLAPF2, outL);
        RevWriteAt(mRAPF2, outR);
        outL = RevMul(outL, R(vAPF2)) + tapL;
        outR = RevMul(outR, R(vAPF2)) + tapR;

        _revOutL = Math.Clamp(outL, -0x8000, 0x7FFF);
        _revOutR = Math.Clamp(outR, -0x8000, 0x7FFF);

        uint b = RevBase;
        long size = RamSize - b;
        if (size <= 0) { _revCur = 0; return; }
        _revCur = _revCur + 2 >= RamSize ? b : Math.Max(b, _revCur + 2);
    }
    ushort _kon, _konHi;
    ushort _koff, _koffHi;
    ushort _pmon, _pmonHi;
    ushort _non, _nonHi;
    ushort _eon, _eonHi;
    uint _endx;
    ushort _spucnt;
    ushort _transferAddr;
    ushort _transferCtrl = 4;
    ushort _cdVolL, _cdVolR;
    ushort _extVolL, _extVolR;
    int _cdMixLL = 0x80, _cdMixLR, _cdMixRL, _cdMixRR = 0x80;

  //vol cont
    int _voiceGain = 0x8000, _xaGain = 0x8000;

    public float VoiceGain
    {
        get => _voiceGain / 32768f;
        set => _voiceGain = (int)(Math.Clamp(value, 0f, 1f) * 32768f);
    }

    public float XaGain
    {
        get => _xaGain / 32768f;
        set => _xaGain = (int)(Math.Clamp(value, 0f, 1f) * 32768f);
    }
    ushort _reverbStartAddr;

    int _noiseLevel = 1;
    int _noiseTimer;

    public Spu()
    {
        for (int i = 0; i < 24; i++) _v[i] = new Voice();
    }

    public ushort ReadReg16(uint phys)
    {
        lock (_sync) return ReadReg(phys);
    }

    ushort ReadReg(uint phys)
    {
        uint off = phys - Base;

        if (off < 0x180u)
        {
            int n = (int)(off >> 4);
            int r = (int)(off & 0xFu);
            var v = _v[n];
            return r switch {
                0x0 => v.VolL,
                0x2 => v.VolR,
                0x4 => v.Pitch,
                0x6 => v.StartAddr,
                0x8 => v.AdsrLo,
                0xA => v.AdsrHi,
                0xC => (_konPending & (1u << n)) != 0 || (v.Phase == AdsrPhase.Attack && v.AdsrVol == 0)
                    ? (ushort)1
                    : (ushort)v.AdsrVol,
                0xE => v.RepeatAddr,
                _ => 0
            };
        }

        return off switch {
            0x180 => _mainVolL,
            0x182 => _mainVolR,
            0x184 => _reverbVolL,
            0x186 => _reverbVolR,
            0x188 => _kon,
            0x18A => _konHi,
            0x18C => _koff,
            0x18E => _koffHi,
            0x190 => _pmon,
            0x192 => _pmonHi,
            0x194 => _non,
            0x196 => _nonHi,
            0x198 => _eon,
            0x19A => _eonHi,
            0x19C => (ushort)(_endx & 0xFFFF),
            0x19E => (ushort)(_endx >> 16),
            0x1A2 => _reverbStartAddr,
            >= 0x1C0 and <= 0x1FE => _rev[(off - 0x1C0) >> 1],
            0x1A6 => _transferAddr,
            0x1AA => _spucnt,
            0x1AC => _transferCtrl,
            0x1AE => (ushort)(_spucnt & 0x3F),
            0x1B0 => _cdVolL,
            0x1B2 => _cdVolR,
            0x1B4 => _extVolL,
            0x1B6 => _extVolR,
            _ => 0
        };
    }

    public void WriteReg16(uint phys, ushort val)
    {
        lock (_sync) WriteReg(phys, val);
    }

    void WriteReg(uint phys, ushort val)
    {
        uint off = phys - Base;

        if (off < 0x180u)
        {
            int n = (int)(off >> 4);
            int r = (int)(off & 0xFu);
            var v = _v[n];
            switch (r)
            {
                case 0x0: v.VolL = val; break;
                case 0x2: v.VolR = val; break;
                case 0x4: v.Pitch = val; break;
                case 0x6: v.StartAddr = val; break;
                case 0x8: v.AdsrLo = val; break;
                case 0xA: v.AdsrHi = val; break;
                case 0xC: v.AdsrVol = (short)val; break;
                case 0xE: v.RepeatAddr = val; v.IgnoreLoop = true; break;
            }
            return;
        }

        switch (off)
        {
            case 0x180: _mainVolL = val; break;
            case 0x182: _mainVolR = val; break;
            case 0x184: _reverbVolL = val; break;
            case 0x186: _reverbVolR = val; break;
            case 0x188: KeyOn(val, false);  _kon = val; break;
            case 0x18A: KeyOn(val, true);   _konHi = val; break;
            case 0x18C: KeyOff(val, false); _koff = val; break;
            case 0x18E: KeyOff(val, true);  _koffHi = val; break;
            case 0x190: _pmon = val; break;
            case 0x192: _pmonHi = val; break;
            case 0x194: _non = val; break;
            case 0x196: _nonHi = val; break;
            case 0x198: _eon = val; break;
            case 0x19A: _eonHi = val; break;
            case 0x1A2: _reverbStartAddr = val; break;
            case 0x1A6: _transferAddr = val; break;
            case 0x1AA: _spucnt = val; break;
            case 0x1AC: _transferCtrl = val; break;
            case 0x1B0: _cdVolL = val; break;
            case 0x1B2: _cdVolR = val; break;
            case 0x1B4: _extVolL = val; break;
            case 0x1B6: _extVolR = val; break;
            default:
                if (off >= 0x1C0 && off <= 0x1FE) _rev[(off - 0x1C0) >> 1] = val;
                break;
        }
    }

    uint _konPending, _koffPending;

    void KeyOn(ushort mask, bool hi)
    {
        uint bits = (uint)mask << (hi ? 16 : 0);
        _konPending |= bits;
        _koffPending &= ~bits;
        _endx &= ~bits;
    }

    void KeyOff(ushort mask, bool hi)
    {
        uint bits = (uint)mask << (hi ? 16 : 0);
        _koffPending |= bits & ~_konPending;
    }

    void ResolveKeys()
    {
        if (_konPending == 0 && _koffPending == 0) return;

        for (int i = 0; i < 24; i++)
        {
            uint bit = 1u << i;
            if ((_konPending & bit) != 0)
            {
                var v = _v[i];
                v.Phase = AdsrPhase.Attack;
                v.AdsrVol = 0;
                v.AdsrCycleCount = 0;
                v.CurAddr = (uint)v.StartAddr << 3;
                v.IgnoreLoop = false;
                v.PitchCounter = 0;
                v.Old = v.Older = 0;
                v.HasBlock = false;
                v.EndX = false;
                _endx &= ~bit;
            }
            else if ((_koffPending & bit) != 0)
            {
                var v = _v[i];
                if (v.Phase != AdsrPhase.Off) v.Phase = AdsrPhase.Release;
            }
        }

        _konPending = 0;
        _koffPending = 0;
    }

    public uint TransferAddrBytes() => (uint)_transferAddr << 3;

    public void CaptureDebug(VoiceDebug[] voices, out SpuDebug state)
    {
        lock (_sync)
        {
            uint nonMask = (uint)(_non | (_nonHi << 16));
            uint pmonMask = (uint)(_pmon | (_pmonHi << 16));
            uint eonMask = (uint)(_eon | (_eonHi << 16));

            int n = Math.Min(voices.Length, 24);
            for (int i = 0; i < n; i++)
            {
                var v = _v[i];
                voices[i] = new VoiceDebug {
                    Phase = v.Phase,
                    AdsrVol = v.AdsrVol,
                    VolL = v.VolL,
                    VolR = v.VolR,
                    Pitch = v.Pitch,
                    StartAddr = v.StartAddr,
                    RepeatAddr = v.RepeatAddr,
                    CurAddr = v.CurAddr,
                    EndX = v.EndX,
                    Noise = (nonMask & (1u << i)) != 0,
                    Pmod = (pmonMask & (1u << i)) != 0,
                    Reverb = (eonMask & (1u << i)) != 0
                };
            }

            state = new SpuDebug {
                MainVolL = _mainVolL,
                MainVolR = _mainVolR,
                ReverbVolL = _reverbVolL,
                ReverbVolR = _reverbVolR,
                CdVolL = _cdVolL,
                CdVolR = _cdVolR,
                ExtVolL = _extVolL,
                ExtVolR = _extVolR,
                Spucnt = _spucnt,
                TransferAddr = (uint)_transferAddr << 3,
                ReverbStartAddr = (uint)_reverbStartAddr << 3,
                Endx = _endx
            };
        }
    }

    public void DmaWrite(uint spuByteAddr, ReadOnlySpan<byte> data)
    {
        lock (_sync)
        {
            for (int i = 0; i < data.Length; i++)
                Ram[(spuByteAddr + (uint)i) & (RamSize - 1)] = data[i];
        }
    }

    public void CdInitVolume()
    {
        lock (_sync)
        {
            if (_mainCurL == 0 && _mainCurR == 0)
                _mainVolL = _mainVolR = 0x3FFF;
            _cdVolL = _cdVolR = 0x3FFF;
            _spucnt = 0xC001;
        }
    }

    //CdlATV order: val0 = L to L, val1 = L to R, val2 = R to R, val3 = R to L
    public void SetCdMix(byte ltol, byte ltor, byte rtor, byte rtol)
    {
        lock (_sync)
        {
            _cdMixLL = ltol;
            _cdMixLR = ltor;
            _cdMixRL = rtol;
            _cdMixRR = rtor;
        }
    }

    short[] _xaL = [], _xaR = [];

    public void Mix(short[] dst, int frames)
    {
        if (_xaL.Length < frames) { _xaL = new short[frames]; _xaR = new short[frames]; }
        int xaCount = XaAudio.NextBlock(_xaL, _xaR, frames);

        lock (_sync)
        {
            for (int n = 0; n < frames; n++)
            {
                SweepTick(_mainVolL, ref _mainCurL, ref _mainCycL);
                SweepTick(_mainVolR, ref _mainCurR, ref _mainCycR);

                var (l, r) = Tick();
                int mixL = l * _voiceGain >> 15, mixR = r * _voiceGain >> 15;
                if (n < xaCount)
                {
                    short xl = _xaL[n], xr = _xaR[n];
                    int aL = Math.Clamp((xl * _cdMixLL + xr * _cdMixRL) >> 7, -32768, 32767);
                    int aR = Math.Clamp((xl * _cdMixLR + xr * _cdMixRR) >> 7, -32768, 32767);
                    int cL = (aL * (short)_cdVolL >> 15) * _xaGain >> 15;
                    int cR = (aR * (short)_cdVolR >> 15) * _xaGain >> 15;
                    mixL += cL;
                    mixR += cR;
                    if ((_spucnt & 0x04) != 0) { _revInL += cL; _revInR += cR; }
                }

                _revRight = !_revRight;
                if (_revRight) ReverbStep(_revInL, _revInR);
                mixL += RevMul(_revOutL, (short)_reverbVolL);
                mixR += RevMul(_revOutR, (short)_reverbVolR);
                mixL = Math.Clamp(mixL, -32768, 32767) * _mainCurL >> 15;
                mixR = Math.Clamp(mixR, -32768, 32767) * _mainCurR >> 15;
                dst[n * 2] = (short)mixL;
                dst[n * 2 + 1] = (short)mixR;
            }
        }
    }

    (short L, short R) Tick()
    {
        ResolveKeys();
        TickNoise();
        int sumL = 0, sumR = 0;
        _revInL = 0;
        _revInR = 0;
        uint eonMask = (uint)(_eon | (_eonHi << 16));

        uint nonMask = (uint)(_non  | (_nonHi  << 16));
        uint pmonMask = (uint)(_pmon | (_pmonHi << 16));
        int prevOutx = 0;

        for (int i = 0; i < 24; i++)
        {
            var v = _v[i];
            if (v.Phase == AdsrPhase.Off) { prevOutx = 0; continue; }

            TickAdsr(v);
            SweepTick(v.VolL, ref v.CurVolL, ref v.VolCycL);
            SweepTick(v.VolR, ref v.CurVolR, ref v.VolCycR);

            if (!v.HasBlock)
            {
                Array.Clear(v.Buf);
                DecodeBlock(v, i);
                v.HasBlock = true;
            }

            int sample;
            if ((nonMask & (1u << i)) != 0)
            {
                sample = (short)_noiseLevel;
            }
            else
            {
                int idx = (int)(v.PitchCounter >> 12);
                int fi = (int)((v.PitchCounter >> 4) & 0xFF);
                sample = ((Gauss[0x0FF - fi] * v.Buf[idx])     >> 15)
                       + ((Gauss[0x1FF - fi] * v.Buf[idx + 1]) >> 15)
                       + ((Gauss[0x100 + fi] * v.Buf[idx + 2]) >> 15)
                       + ((Gauss[0x000 + fi] * v.Buf[idx + 3]) >> 15);
            }

            int amp = (sample * v.AdsrVol) >> 15;

            int step = v.Pitch;
            if (i > 0 && (pmonMask & (1u << i)) != 0)
            {
                int factor = Math.Clamp(prevOutx, -0x8000, 0x7FFF) + 0x8000;
                step = ((short)(ushort)step * factor >> 15) & 0xFFFF;
            }
            if (step > 0x3FFF) step = 0x4000;

            v.PitchCounter += (uint)step;
            if ((v.PitchCounter >> 12) >= 28)
            {
                v.PitchCounter -= 28u << 12;
                DecodeBlock(v, i);
            }

            prevOutx = amp;

            int vl = (amp * v.CurVolL) >> 15;
            int vr = (amp * v.CurVolR) >> 15;
            sumL += vl;
            sumR += vr;
            if ((eonMask & (1u << i)) != 0) { _revInL += vl; _revInR += vr; }
        }

        sumL = Math.Clamp(sumL, -32768, 32767);
        sumR = Math.Clamp(sumR, -32768, 32767);
        return ((short)sumL, (short)sumR);
    }

    static void SweepTick(ushort reg, ref short level, ref int cycleCount)
    {
        if ((reg & 0x8000) == 0)
        {
            level = (short)(reg << 1);
            return;
        }

        bool exp = (reg & 0x4000) != 0;
        bool dec = (reg & 0x2000) != 0;
        bool neg = (reg & 0x1000) != 0;
        int shift = (reg >> 2) & 0x1F;
        int stepIdx = reg & 0x3;
        EnvTick(ref level, ref cycleCount, shift, stepIdx, exp, dec, neg);
    }

    static void EnvTick(ref short level, ref int cycleCount, int shift, int stepIdx, bool exp, bool decrease, bool negPhase)
    {
        int mag = negPhase ? -level : level;
        int cycles = 1 << Math.Max(0, shift - 11);
        int step = (decrease ? AdsrStepDown : AdsrStepUp)[stepIdx] << Math.Max(0, 11 - shift);

        if (exp && !decrease && mag > 0x6000) cycles *= 4;
        if (exp && decrease) { step = step * mag / 0x8000; if (step == 0) step = -1; }

        cycleCount++;
        if (cycleCount < cycles) return;
        cycleCount = 0;

        int next = Math.Clamp(mag + step, 0, 0x7FFF);
        level = (short)(negPhase ? -next : next);
    }

    void DecodeBlock(Voice v, int index)
    {
        v.Buf[0] = v.Buf[28];
        v.Buf[1] = v.Buf[29];
        v.Buf[2] = v.Buf[30];

        uint addr = v.CurAddr & (uint)(RamSize - 1);
        byte hdr = Ram[addr];
        byte flags = Ram[(addr + 1) & (RamSize - 1)];

        int shift = hdr & 0xF;
        if (shift > 12) shift = 9;
        int filter = (hdr >> 4) & 0x7;
        if (filter > 4) filter = 4;

        int k0 = K0[filter];
        int k1 = K1[filter];

        int pos = 3;
        for (int i = 0; i < 14; i++)
        {
            byte b = Ram[(addr + 2 + (uint)i) & (RamSize - 1)];
            v.Buf[pos++] = DecodeSample(v, b & 0xF, shift, k0, k1);
            v.Buf[pos++] = DecodeSample(v, b >> 4,  shift, k0, k1);
        }

        if ((flags & 4) != 0 && !v.IgnoreLoop)
            v.RepeatAddr = (ushort)(addr >> 3);

        v.CurAddr += 16;

        if ((flags & 1) != 0)
        {
            v.EndX = true;
            _endx  |= 1u << index;
            v.CurAddr = (uint)v.RepeatAddr << 3;

            if ((flags & 2) == 0)
            {
                v.AdsrVol = 0;
                v.Phase = AdsrPhase.Release;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static short DecodeSample(Voice v, int nibble, int shift, int k0, int k1)
    {
        int s = (nibble << 28) >> 28;
        s = (s << 12) >> shift;
        s += (v.Old * k0 + v.Older * k1) >> 6;
        s = Math.Clamp(s, -32768, 32767);
        v.Older = v.Old;
        v.Old = s;
        return (short)s;
    }

    void TickAdsr(Voice v)
    {
        if (v.Phase == AdsrPhase.Off) return;

        int lo = v.AdsrLo, hi = v.AdsrHi;

        bool atkExp = (lo >> 15 & 1) != 0;
        int  atkShift = (lo >> 10) & 0x1F;
        int  atkStep = (lo >>  8) & 0x3;
        int  decShift = (lo >>  4) & 0xF;
        int  susLvl = ((lo & 0xF) + 1) << 11;

        bool susExp = (hi >> 15 & 1) != 0;
        bool susDec = (hi >> 14 & 1) != 0;
        int  susShift = (hi >>  8) & 0x1F;
        int  susStep = (hi >>  6) & 0x3;
        bool relExp = (hi >>  5 & 1) != 0;
        int  relShift =  hi & 0x1F;

        if (v.Phase == AdsrPhase.Attack && v.AdsrVol >= 0x7FFF)
        {
            v.Phase = AdsrPhase.Decay;
            v.AdsrCycleCount = 0;
        }

        if (v.Phase == AdsrPhase.Decay && v.AdsrVol <= susLvl)
        {
            v.Phase = AdsrPhase.Sustain;
            v.AdsrCycleCount = 0;
        }

        (bool decrease, int shift, int stepIdx, bool exp) = v.Phase switch {
            AdsrPhase.Attack => (false,  atkShift, atkStep, atkExp),
            AdsrPhase.Decay => (true,   decShift, 0,       true),
            AdsrPhase.Sustain => (susDec, susShift, susStep, susExp),
            AdsrPhase.Release => (true,   relShift, 0,       relExp),
            _ => (false, 0, 0, false)
        };

        EnvTick(ref v.AdsrVol, ref v.AdsrCycleCount, shift, stepIdx, exp, decrease, false);

        if (v.Phase == AdsrPhase.Release && v.AdsrVol == 0)
            v.Phase = AdsrPhase.Off;
    }

    void TickNoise()
    {
        int shift = (_spucnt >> 10) & 0xF;
        int step = ((_spucnt >>  8) & 0x3) + 4;

        _noiseTimer -= step;
        int parity = ((_noiseLevel >> 15) ^ (_noiseLevel >> 12) ^
                      (_noiseLevel >> 11) ^ (_noiseLevel >> 10) ^ 1) & 1;
        if (_noiseTimer < 0)
        {
            _noiseLevel = ((_noiseLevel << 1) | parity) & 0xFFFF;
            _noiseTimer += 0x20000 >> shift;
            if (_noiseTimer < 0) _noiseTimer += 0x20000 >> shift;
        }
    }
}
