using System.Runtime.CompilerServices;

namespace RecompOne.Runtime;

public static class Gte
{
    private static readonly short[] V = new short[9];
    private static byte RGBC_R, RGBC_G, RGBC_B, RGBC_CODE;
    private static ushort OTZ;
    private static int IR0, IR1, IR2, IR3;
    private static readonly short[] SX = new short[3];
    private static readonly short[] SY = new short[3];
    private static readonly ushort[] SZ = new ushort[4];
    private static readonly uint[] RGB = new uint[3];
    private static uint RES1;
    private static int MAC0, MAC1, MAC2, MAC3;
    private static uint LZCS, LZCR;

    private static readonly short[] RT = new short[9];
    private static readonly short[] LLM = new short[9];
    private static readonly short[] LCM = new short[9];
    private static readonly int[] TR = new int[3];
    private static readonly int[] BK = new int[3];
    private static readonly int[] FC = new int[3];
    private static int OFX, OFY;
    private static ushort H;
    private static short DQA;
    private static int DQB;
    private static short ZSF3, ZSF4;
    private static uint FLAG;

    private static readonly byte[] Unr = BuildUnr();

    private static byte[] BuildUnr()
    {
        var t = new byte[0x101];
        for (var i = 0; i < 0x101; i++)
        {
            var v = (0x40000 / (i + 0x100) + 1) / 2 - 0x101;
            t[i] = (byte)(v < 0 ? 0 : v > 0xFF ? 0xFF : v);
        }

        return t;
    }

    private static void Flag(int bit)
    {
        FLAG |= 1u << bit;
    }

    private static int SatIR(int n, int v, bool lm)
    {
        var min = lm ? 0 : -0x8000;
        if (v < min)
        {
            v = min;
            Flag(25 - n);
        }
        else if (v > 0x7FFF)
        {
            v = 0x7FFF;
            Flag(25 - n);
        }

        return v;
    }

    private static int SatIR0(int v)
    {
        if (v < 0)
        {
            Flag(12);
            return 0;
        }

        if (v > 0x1000)
        {
            Flag(12);
            return 0x1000;
        }

        return v;
    }

    private static int SatColor(int n, int v)
    {
        if (v < 0)
        {
            Flag(21 - n);
            return 0;
        }

        if (v > 0xFF)
        {
            Flag(21 - n);
            return 0xFF;
        }

        return v;
    }

    private static int SatSZ(int v)
    {
        if (v < 0)
        {
            Flag(18);
            return 0;
        }

        if (v > 0xFFFF)
        {
            Flag(18);
            return 0xFFFF;
        }

        return v;
    }

    private static int SatX(int v)
    {
        if (v < -0x400)
        {
            Flag(14);
            return -0x400;
        }

        if (v > 0x3FF)
        {
            Flag(14);
            return 0x3FF;
        }

        return v;
    }

    private static int SatY(int v)
    {
        if (v < -0x400)
        {
            Flag(13);
            return -0x400;
        }

        if (v > 0x3FF)
        {
            Flag(13);
            return 0x3FF;
        }

        return v;
    }

    private static long CheckMac0(long v)
    {
        if (v > 0x7FFFFFFFL) Flag(16);
        else if (v < -0x80000000L) Flag(15);
        return v;
    }

    private static void CheckMac(int n, long v)
    {
        if (v >= 1L << 43) Flag(31 - n);
        else if (v < -(1L << 43)) Flag(28 - n);
    }

    private static void SetMac(int n, long v, int sf, bool lm)
    {
        CheckMac(n, v);
        var m = (int)(v >> sf);
        if (n == 1)
        {
            MAC1 = m;
            IR1 = SatIR(1, m, lm);
        }
        else if (n == 2)
        {
            MAC2 = m;
            IR2 = SatIR(2, m, lm);
        }
        else
        {
            MAC3 = m;
            IR3 = SatIR(3, m, lm);
        }
    }

    private static void MatVec(short[] mx, int t0, int t1, int t2, int vx, int vy, int vz, int sf, bool lm)
    {
        SetMac(1, ((long)t0 << 12) + (long)mx[0] * vx + (long)mx[1] * vy + (long)mx[2] * vz, sf, lm);
        SetMac(2, ((long)t1 << 12) + (long)mx[3] * vx + (long)mx[4] * vy + (long)mx[5] * vz, sf, lm);
        SetMac(3, ((long)t2 << 12) + (long)mx[6] * vx + (long)mx[7] * vy + (long)mx[8] * vz, sf, lm);
    }

    private static void PushColor()
    {
        var r = SatColor(0, MAC1 >> 4);
        var g = SatColor(1, MAC2 >> 4);
        var b = SatColor(2, MAC3 >> 4);
        RGB[0] = RGB[1];
        RGB[1] = RGB[2];
        RGB[2] = (uint)(r | (g << 8) | (b << 16) | (RGBC_CODE << 24));
    }

    private static void Interp(long in1, long in2, long in3, int sf, bool lm)
    {
        IR1 = SatIR(1, (int)((((long)FC[0] << 12) - in1) >> sf), false);
        IR2 = SatIR(2, (int)((((long)FC[1] << 12) - in2) >> sf), false);
        IR3 = SatIR(3, (int)((((long)FC[2] << 12) - in3) >> sf), false);
        SetMac(1, (long)IR1 * IR0 + in1, sf, lm);
        SetMac(2, (long)IR2 * IR0 + in2, sf, lm);
        SetMac(3, (long)IR3 * IR0 + in3, sf, lm);
        PushColor();
    }

    private static void Modulate(int sf, bool lm)
    {
        SetMac(1, ((long)RGBC_R * IR1) << 4, sf, lm);
        SetMac(2, ((long)RGBC_G * IR2) << 4, sf, lm);
        SetMac(3, ((long)RGBC_B * IR3) << 4, sf, lm);
        PushColor();
    }

    private static int Clz16(uint v)
    {
        return System.Numerics.BitOperations.LeadingZeroCount(v | 1u) - 16;
    }

    private static uint Divide(uint h, uint sz3)
    {
        if (h >= sz3 * 2)
        {
            Flag(17);
            return 0x1FFFF;
        }

        var z = Clz16(sz3);
        var n = (ulong)h << z;
        var d = (ulong)sz3 << z;
        var idx = (int)((d - 0x7FC0) >> 7);
        if (idx < 0) idx = 0;
        else if (idx > 0x100) idx = 0x100;
        var u = (ulong)Unr[idx] + 0x101;
        d = (0x2000080UL - d * u) >> 8;
        d = (0x0000080UL + d * u) >> 8;
        var res = (n * d + 0x8000) >> 16;
        return res > 0x1FFFF ? 0x1FFFFu : (uint)res;
    }

    private static void Rtp(int vx, int vy, int vz, int sf, bool lm, bool last)
    {
        var m1 = ((long)TR[0] << 12) + (long)RT[0] * vx + (long)RT[1] * vy + (long)RT[2] * vz;
        var m2 = ((long)TR[1] << 12) + (long)RT[3] * vx + (long)RT[4] * vy + (long)RT[5] * vz;
        var m3 = ((long)TR[2] << 12) + (long)RT[6] * vx + (long)RT[7] * vy + (long)RT[8] * vz;
        CheckMac(1, m1);
        CheckMac(2, m2);
        CheckMac(3, m3);
        MAC1 = (int)(m1 >> sf);
        MAC2 = (int)(m2 >> sf);
        MAC3 = (int)(m3 >> sf);
        IR1 = SatIR(1, MAC1, lm);
        IR2 = SatIR(2, MAC2, lm);
        var ir3flag = (int)(m3 >> 12);
        if (ir3flag < -0x8000 || ir3flag > 0x7FFF) Flag(22);
        IR3 = MAC3 < (lm ? 0 : -0x8000) ? lm ? 0 : -0x8000 : MAC3 > 0x7FFF ? 0x7FFF : MAC3;

        var sz = SatSZ((int)(m3 >> 12));
        SZ[0] = SZ[1];
        SZ[1] = SZ[2];
        SZ[2] = SZ[3];
        SZ[3] = (ushort)sz;

        var div = Divide(H, SZ[3]);
        var sx = CheckMac0((long)div * IR1 + OFX);
        MAC0 = (int)sx;
        var sy = CheckMac0((long)div * IR2 + OFY);
        MAC0 = (int)sy;
        var nx = SatX((int)(sx >> 16));
        var ny = SatY((int)(sy >> 16));
        SX[0] = SX[1];
        SX[1] = SX[2];
        SX[2] = (short)nx;
        SY[0] = SY[1];
        SY[1] = SY[2];
        SY[2] = (short)ny;

        if (last)
        {
            var dp = CheckMac0((long)div * DQA + DQB);
            MAC0 = (int)dp;
            IR0 = SatIR0((int)(dp >> 12));
        }
    }

    private static void EndFlag()
    {
        if ((FLAG & 0x7F87E000u) != 0) FLAG |= 0x80000000u;
    }

    public static void Rtps(int sf, bool lm)
    {
        FLAG = 0;
        Rtp(V[0], V[1], V[2], sf, lm, true);
        EndFlag();
    }

    public static void Rtpt(int sf, bool lm)
    {
        FLAG = 0;
        Rtp(V[0], V[1], V[2], sf, lm, false);
        Rtp(V[3], V[4], V[5], sf, lm, false);
        Rtp(V[6], V[7], V[8], sf, lm, true);
        EndFlag();
    }

    public static void Nclip()
    {
        FLAG = 0;
        MAC0 = (int)CheckMac0((long)SX[0] * (SY[1] - SY[2]) + (long)SX[1] * (SY[2] - SY[0]) +
                              (long)SX[2] * (SY[0] - SY[1]));
        EndFlag();
    }

    public static void Avsz3()
    {
        FLAG = 0;
        MAC0 = (int)CheckMac0((long)ZSF3 * (SZ[1] + SZ[2] + SZ[3]));
        OTZ = (ushort)SatSZ(MAC0 >> 12);
        EndFlag();
    }

    public static void Avsz4()
    {
        FLAG = 0;
        MAC0 = (int)CheckMac0((long)ZSF4 * (SZ[0] + SZ[1] + SZ[2] + SZ[3]));
        OTZ = (ushort)SatSZ(MAC0 >> 12);
        EndFlag();
    }

    public static void MvmvaOp(int sf, bool lm, int mx, int vn, int cv)
    {
        FLAG = 0;
        Mvmva(sf, lm, mx, vn, cv);
        EndFlag();
    }

    public static void Sqr(int sf, bool lm)
    {
        FLAG = 0;
        SetMac(1, (long)IR1 * IR1, sf, lm);
        SetMac(2, (long)IR2 * IR2, sf, lm);
        SetMac(3, (long)IR3 * IR3, sf, lm);
        EndFlag();
    }

    public static void Cross(int sf, bool lm)
    {
        FLAG = 0;
        int ir1 = IR1, ir2 = IR2, ir3 = IR3;
        SetMac(1, (long)RT[4] * ir3 - (long)RT[8] * ir2, sf, lm);
        SetMac(2, (long)RT[8] * ir1 - (long)RT[0] * ir3, sf, lm);
        SetMac(3, (long)RT[0] * ir2 - (long)RT[4] * ir1, sf, lm);
        EndFlag();
    }

    public static void Gpf(int sf, bool lm)
    {
        FLAG = 0;
        SetMac(1, (long)IR0 * IR1, sf, lm);
        SetMac(2, (long)IR0 * IR2, sf, lm);
        SetMac(3, (long)IR0 * IR3, sf, lm);
        PushColor();
        EndFlag();
    }

    public static void Gpl(int sf, bool lm)
    {
        FLAG = 0;
        SetMac(1, ((long)MAC1 << sf) + (long)IR0 * IR1, sf, lm);
        SetMac(2, ((long)MAC2 << sf) + (long)IR0 * IR2, sf, lm);
        SetMac(3, ((long)MAC3 << sf) + (long)IR0 * IR3, sf, lm);
        PushColor();
        EndFlag();
    }

    public static void Dpcs(int sf, bool lm)
    {
        FLAG = 0;
        Interp((long)RGBC_R << 16, (long)RGBC_G << 16, (long)RGBC_B << 16, sf, lm);
        EndFlag();
    }

    public static void Dpct(int sf, bool lm)
    {
        FLAG = 0;
        for (var i = 0; i < 3; i++)
            Interp((long)(RGB[0] & 0xFF) << 16, (long)((RGB[0] >> 8) & 0xFF) << 16, (long)((RGB[0] >> 16) & 0xFF) << 16,
                sf, lm);

        EndFlag();
    }

    public static void Intpl(int sf, bool lm)
    {
        FLAG = 0;
        Interp((long)IR1 << 12, (long)IR2 << 12, (long)IR3 << 12, sf, lm);
        EndFlag();
    }

    public static void Dcpl(int sf, bool lm)
    {
        FLAG = 0;
        Interp(((long)RGBC_R * IR1) << 4, ((long)RGBC_G * IR2) << 4, ((long)RGBC_B * IR3) << 4, sf, lm);
        EndFlag();
    }

    public static void NcsOp(int sf, bool lm)
    {
        FLAG = 0;
        Ncs(0, sf, lm);
        EndFlag();
    }

    public static void NctOp(int sf, bool lm)
    {
        FLAG = 0;
        Ncs(0, sf, lm);
        Ncs(1, sf, lm);
        Ncs(2, sf, lm);
        EndFlag();
    }

    public static void NcdsOp(int sf, bool lm)
    {
        FLAG = 0;
        Ncds(0, sf, lm);
        EndFlag();
    }

    public static void NcdtOp(int sf, bool lm)
    {
        FLAG = 0;
        Ncds(0, sf, lm);
        Ncds(1, sf, lm);
        Ncds(2, sf, lm);
        EndFlag();
    }

    public static void NccsOp(int sf, bool lm)
    {
        FLAG = 0;
        Nccs(0, sf, lm);
        EndFlag();
    }

    public static void NcctOp(int sf, bool lm)
    {
        FLAG = 0;
        Nccs(0, sf, lm);
        Nccs(1, sf, lm);
        Nccs(2, sf, lm);
        EndFlag();
    }

    public static void Cc(int sf, bool lm)
    {
        FLAG = 0;
        MatVec(LCM, BK[0], BK[1], BK[2], IR1, IR2, IR3, sf, lm);
        Modulate(sf, lm);
        EndFlag();
    }

    public static void Cdp(int sf, bool lm)
    {
        FLAG = 0;
        MatVec(LCM, BK[0], BK[1], BK[2], IR1, IR2, IR3, sf, lm);
        Interp(((long)RGBC_R * IR1) << 4, ((long)RGBC_G * IR2) << 4, ((long)RGBC_B * IR3) << 4, sf, lm);
        EndFlag();
    }

    public static void Execute(uint cmd)
    {
        FLAG = 0;
        var sf = (cmd & (1u << 19)) != 0 ? 12 : 0;
        var lm = (cmd & (1u << 10)) != 0;
        var mx = (int)((cmd >> 17) & 3);
        var vn = (int)((cmd >> 15) & 3);
        var cv = (int)((cmd >> 13) & 3);

        switch (cmd & 0x3F)
        {
            case 0x01:
                Rtps(sf, lm);
                return;
            case 0x06:
                Nclip();
                return;
            case 0x0C:
                Cross(sf, lm);
                return;
            case 0x10:
                Dpcs(sf, lm);
                return;
            case 0x11:
                Intpl(sf, lm);
                return;
            case 0x12:
                MvmvaOp(sf, lm, mx, vn, cv);
                return;
            case 0x13:
                NcdsOp(sf, lm);
                return;
            case 0x14:
                Cdp(sf, lm);
                return;
            case 0x16:
                NcdtOp(sf, lm);
                return;
            case 0x1B:
                NccsOp(sf, lm);
                return;
            case 0x1C:
                Cc(sf, lm);
                return;
            case 0x1E:
                NcsOp(sf, lm);
                return;
            case 0x20:
                NctOp(sf, lm);
                return;
            case 0x28:
                Sqr(sf, lm);
                return;
            case 0x29:
                Dcpl(sf, lm);
                return;
            case 0x2A:
                Dpct(sf, lm);
                return;
            case 0x2D:
                Avsz3();
                return;
            case 0x2E:
                Avsz4();
                return;
            case 0x30:
                Rtpt(sf, lm);
                return;
            case 0x3D:
                Gpf(sf, lm);
                return;
            case 0x3E:
                Gpl(sf, lm);
                return;
            case 0x3F:
                NcctOp(sf, lm);
                return;
        }

        if ((FLAG & 0x7F87E000u) != 0) FLAG |= 0x80000000u;
    }

    private static void Ncs(int vec, int sf, bool lm)
    {
        MatVec(LLM, 0, 0, 0, V[vec * 3], V[vec * 3 + 1], V[vec * 3 + 2], sf, lm);
        MatVec(LCM, BK[0], BK[1], BK[2], IR1, IR2, IR3, sf, lm);
        PushColor();
    }

    private static void Ncds(int vec, int sf, bool lm)
    {
        MatVec(LLM, 0, 0, 0, V[vec * 3], V[vec * 3 + 1], V[vec * 3 + 2], sf, lm);
        MatVec(LCM, BK[0], BK[1], BK[2], IR1, IR2, IR3, sf, lm);
        Interp(((long)RGBC_R * IR1) << 4, ((long)RGBC_G * IR2) << 4, ((long)RGBC_B * IR3) << 4, sf, lm);
    }

    private static void Nccs(int vec, int sf, bool lm)
    {
        MatVec(LLM, 0, 0, 0, V[vec * 3], V[vec * 3 + 1], V[vec * 3 + 2], sf, lm);
        MatVec(LCM, BK[0], BK[1], BK[2], IR1, IR2, IR3, sf, lm);
        Modulate(sf, lm);
    }

    private static void Mvmva(int sf, bool lm, int mx, int vn, int cv)
    {
        var mat = mx == 0 ? RT : mx == 1 ? LLM : mx == 2 ? LCM : RT;
        int vx, vy, vz;
        if (vn < 3)
        {
            vx = V[vn * 3];
            vy = V[vn * 3 + 1];
            vz = V[vn * 3 + 2];
        }
        else
        {
            vx = IR1;
            vy = IR2;
            vz = IR3;
        }

        if (cv == 2)
        {
            SatIR(1, (int)((((long)FC[0] << 12) + (long)mat[0] * vx) >> sf), lm);
            SatIR(2, (int)((((long)FC[1] << 12) + (long)mat[3] * vx) >> sf), lm);
            SatIR(3, (int)((((long)FC[2] << 12) + (long)mat[6] * vx) >> sf), lm);
            SetMac(1, (long)mat[1] * vy + (long)mat[2] * vz, sf, lm);
            SetMac(2, (long)mat[4] * vy + (long)mat[5] * vz, sf, lm);
            SetMac(3, (long)mat[7] * vy + (long)mat[8] * vz, sf, lm);
            return;
        }

        int t0 = 0, t1 = 0, t2 = 0;
        if (cv == 0)
        {
            t0 = TR[0];
            t1 = TR[1];
            t2 = TR[2];
        }
        else if (cv == 1)
        {
            t0 = BK[0];
            t1 = BK[1];
            t2 = BK[2];
        }

        MatVec(mat, t0, t1, t2, vx, vy, vz, sf, lm);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Read(int reg)
    {
        switch (reg)
        {
            case 0: return (uint)((ushort)V[0] | (V[1] << 16));
            case 1: return (uint)V[2];
            case 2: return (uint)((ushort)V[3] | (V[4] << 16));
            case 3: return (uint)V[5];
            case 4: return (uint)((ushort)V[6] | (V[7] << 16));
            case 5: return (uint)V[8];
            case 6: return (uint)(RGBC_R | (RGBC_G << 8) | (RGBC_B << 16) | (RGBC_CODE << 24));
            case 7: return OTZ;
            case 8: return (uint)IR0;
            case 9: return (uint)IR1;
            case 10: return (uint)IR2;
            case 11: return (uint)IR3;
            case 12: return (uint)((ushort)SX[0] | (SY[0] << 16));
            case 13: return (uint)((ushort)SX[1] | (SY[1] << 16));
            case 14:
            case 15: return (uint)((ushort)SX[2] | (SY[2] << 16));
            case 16: return SZ[0];
            case 17: return SZ[1];
            case 18: return SZ[2];
            case 19: return SZ[3];
            case 20: return RGB[0];
            case 21: return RGB[1];
            case 22: return RGB[2];
            case 23: return RES1;
            case 24: return (uint)MAC0;
            case 25: return (uint)MAC1;
            case 26: return (uint)MAC2;
            case 27: return (uint)MAC3;
            case 28:
            case 29:
                var r = Math.Clamp(IR1 >> 7, 0, 0x1F);
                var g = Math.Clamp(IR2 >> 7, 0, 0x1F);
                var b = Math.Clamp(IR3 >> 7, 0, 0x1F);
                return (uint)(r | (g << 5) | (b << 10));
            case 30: return LZCS;
            case 31: return LZCR;
            default: return 0;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write(int reg, uint val)
    {
        switch (reg)
        {
            case 0:
                V[0] = (short)val;
                V[1] = (short)(val >> 16);
                break;
            case 1: V[2] = (short)val; break;
            case 2:
                V[3] = (short)val;
                V[4] = (short)(val >> 16);
                break;
            case 3: V[5] = (short)val; break;
            case 4:
                V[6] = (short)val;
                V[7] = (short)(val >> 16);
                break;
            case 5: V[8] = (short)val; break;
            case 6:
                RGBC_R = (byte)val;
                RGBC_G = (byte)(val >> 8);
                RGBC_B = (byte)(val >> 16);
                RGBC_CODE = (byte)(val >> 24);
                break;
            case 7: OTZ = (ushort)val; break;
            case 8: IR0 = (short)val; break;
            case 9: IR1 = (short)val; break;
            case 10: IR2 = (short)val; break;
            case 11: IR3 = (short)val; break;
            case 12:
                SX[0] = (short)val;
                SY[0] = (short)(val >> 16);
                break;
            case 13:
                SX[1] = (short)val;
                SY[1] = (short)(val >> 16);
                break;
            case 14:
                SX[2] = (short)val;
                SY[2] = (short)(val >> 16);
                break;
            case 15:
                SX[0] = SX[1];
                SY[0] = SY[1];
                SX[1] = SX[2];
                SY[1] = SY[2];
                SX[2] = (short)val;
                SY[2] = (short)(val >> 16);
                break;
            case 16: SZ[0] = (ushort)val; break;
            case 17: SZ[1] = (ushort)val; break;
            case 18: SZ[2] = (ushort)val; break;
            case 19: SZ[3] = (ushort)val; break;
            case 20: RGB[0] = val; break;
            case 21: RGB[1] = val; break;
            case 22: RGB[2] = val; break;
            case 23: RES1 = val; break;
            case 24: MAC0 = (int)val; break;
            case 25: MAC1 = (int)val; break;
            case 26: MAC2 = (int)val; break;
            case 27: MAC3 = (int)val; break;
            case 28:
                IR1 = (int)((val & 0x1F) << 7);
                IR2 = (int)(((val >> 5) & 0x1F) << 7);
                IR3 = (int)(((val >> 10) & 0x1F) << 7);
                break;
            case 29: break;
            case 30:
                LZCS = val;
                var test = (val & 0x80000000u) != 0 ? ~val : val;
                LZCR = (uint)(test == 0 ? 32 : System.Numerics.BitOperations.LeadingZeroCount(test));
                break;
            case 31: break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ReadControl(int reg)
    {
        switch (reg)
        {
            case 0: return (uint)((ushort)RT[0] | (RT[1] << 16));
            case 1: return (uint)((ushort)RT[2] | (RT[3] << 16));
            case 2: return (uint)((ushort)RT[4] | (RT[5] << 16));
            case 3: return (uint)((ushort)RT[6] | (RT[7] << 16));
            case 4: return (uint)RT[8];
            case 5: return (uint)TR[0];
            case 6: return (uint)TR[1];
            case 7: return (uint)TR[2];
            case 8: return (uint)((ushort)LLM[0] | (LLM[1] << 16));
            case 9: return (uint)((ushort)LLM[2] | (LLM[3] << 16));
            case 10: return (uint)((ushort)LLM[4] | (LLM[5] << 16));
            case 11: return (uint)((ushort)LLM[6] | (LLM[7] << 16));
            case 12: return (uint)LLM[8];
            case 13: return (uint)BK[0];
            case 14: return (uint)BK[1];
            case 15: return (uint)BK[2];
            case 16: return (uint)((ushort)LCM[0] | (LCM[1] << 16));
            case 17: return (uint)((ushort)LCM[2] | (LCM[3] << 16));
            case 18: return (uint)((ushort)LCM[4] | (LCM[5] << 16));
            case 19: return (uint)((ushort)LCM[6] | (LCM[7] << 16));
            case 20: return (uint)LCM[8];
            case 21: return (uint)FC[0];
            case 22: return (uint)FC[1];
            case 23: return (uint)FC[2];
            case 24: return (uint)OFX;
            case 25: return (uint)OFY;
            case 26: return (uint)(short)H;
            case 27: return (uint)DQA;
            case 28: return (uint)DQB;
            case 29: return (uint)ZSF3;
            case 30: return (uint)ZSF4;
            case 31: return FLAG;
            default: return 0;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteControl(int reg, uint val)
    {
        switch (reg)
        {
            case 0:
                RT[0] = (short)val;
                RT[1] = (short)(val >> 16);
                break;
            case 1:
                RT[2] = (short)val;
                RT[3] = (short)(val >> 16);
                break;
            case 2:
                RT[4] = (short)val;
                RT[5] = (short)(val >> 16);
                break;
            case 3:
                RT[6] = (short)val;
                RT[7] = (short)(val >> 16);
                break;
            case 4: RT[8] = (short)val; break;
            case 5: TR[0] = (int)val; break;
            case 6: TR[1] = (int)val; break;
            case 7: TR[2] = (int)val; break;
            case 8:
                LLM[0] = (short)val;
                LLM[1] = (short)(val >> 16);
                break;
            case 9:
                LLM[2] = (short)val;
                LLM[3] = (short)(val >> 16);
                break;
            case 10:
                LLM[4] = (short)val;
                LLM[5] = (short)(val >> 16);
                break;
            case 11:
                LLM[6] = (short)val;
                LLM[7] = (short)(val >> 16);
                break;
            case 12: LLM[8] = (short)val; break;
            case 13: BK[0] = (int)val; break;
            case 14: BK[1] = (int)val; break;
            case 15: BK[2] = (int)val; break;
            case 16:
                LCM[0] = (short)val;
                LCM[1] = (short)(val >> 16);
                break;
            case 17:
                LCM[2] = (short)val;
                LCM[3] = (short)(val >> 16);
                break;
            case 18:
                LCM[4] = (short)val;
                LCM[5] = (short)(val >> 16);
                break;
            case 19:
                LCM[6] = (short)val;
                LCM[7] = (short)(val >> 16);
                break;
            case 20: LCM[8] = (short)val; break;
            case 21: FC[0] = (int)val; break;
            case 22: FC[1] = (int)val; break;
            case 23: FC[2] = (int)val; break;
            case 24: OFX = (int)val; break;
            case 25: OFY = (int)val; break;
            case 26: H = (ushort)val; break;
            case 27: DQA = (short)val; break;
            case 28: DQB = (int)val; break;
            case 29: ZSF3 = (short)val; break;
            case 30: ZSF4 = (short)val; break;
            case 31:
                FLAG = val & 0x7FFFF000u;
                if ((FLAG & 0x7F87E000u) != 0) FLAG |= 0x80000000u;
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void LoadWord(int reg, uint val)
    {
        Write(reg, val);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint StoreWord(int reg)
    {
        return Read(reg);
    }

    public static bool GetCondition()
    {
        return false;
    }
}