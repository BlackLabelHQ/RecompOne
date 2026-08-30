namespace RecompOne.Runtime.Context;

//todo: cleanup
//note i change this too use direct values instead of array, more performatic apparently, the getter/setters were causing overhead
public struct CpuSnapshot
{
    public uint At;
    public uint V0;
    public uint V1;
    public uint A0;
    public uint A1;
    public uint A2;
    public uint A3;
    public uint T0;
    public uint T1;
    public uint T2;
    public uint T3;
    public uint T4;
    public uint T5;
    public uint T6;
    public uint T7;
    public uint S0;
    public uint S1;
    public uint S2;
    public uint S3;
    public uint S4;
    public uint S5;
    public uint S6;
    public uint S7;
    public uint T8;
    public uint T9;
    public uint K0;
    public uint K1;
    public uint GP;
    public uint SP;
    public uint FP;
    public uint RA;
    public uint HI;
    public uint LO;
}

public sealed class CpuContext
{
    public uint At;
    public uint V0;
    public uint V1;
    public uint A0;
    public uint A1;
    public uint A2;
    public uint A3;
    public uint T0;
    public uint T1;
    public uint T2;
    public uint T3;
    public uint T4;
    public uint T5;
    public uint T6;
    public uint T7;
    public uint S0;
    public uint S1;
    public uint S2;
    public uint S3;
    public uint S4;
    public uint S5;
    public uint S6;
    public uint S7;
    public uint T8;
    public uint T9;
    public uint K0;
    public uint K1;
    public uint GP;
    public uint SP;
    public uint FP;
    public uint RA;

    public uint HI;
    public uint LO;

    public uint SR;
    public uint Cause;
    public uint EPC;
    public uint BadVAddr;
    public uint PRId;

    public uint this[int index]
    {
        get => index switch
        {
            1 => At,
            2 => V0,
            3 => V1,
            4 => A0,
            5 => A1,
            6 => A2,
            7 => A3,
            8 => T0,
            9 => T1,
            10 => T2,
            11 => T3,
            12 => T4,
            13 => T5,
            14 => T6,
            15 => T7,
            16 => S0,
            17 => S1,
            18 => S2,
            19 => S3,
            20 => S4,
            21 => S5,
            22 => S6,
            23 => S7,
            24 => T8,
            25 => T9,
            26 => K0,
            27 => K1,
            28 => GP,
            29 => SP,
            30 => FP,
            31 => RA,
            _ => 0u
        };
        set
        {
            switch (index)
            {
                case 1: At = value; break;
                case 2: V0 = value; break;
                case 3: V1 = value; break;
                case 4: A0 = value; break;
                case 5: A1 = value; break;
                case 6: A2 = value; break;
                case 7: A3 = value; break;
                case 8: T0 = value; break;
                case 9: T1 = value; break;
                case 10: T2 = value; break;
                case 11: T3 = value; break;
                case 12: T4 = value; break;
                case 13: T5 = value; break;
                case 14: T6 = value; break;
                case 15: T7 = value; break;
                case 16: S0 = value; break;
                case 17: S1 = value; break;
                case 18: S2 = value; break;
                case 19: S3 = value; break;
                case 20: S4 = value; break;
                case 21: S5 = value; break;
                case 22: S6 = value; break;
                case 23: S7 = value; break;
                case 24: T8 = value; break;
                case 25: T9 = value; break;
                case 26: K0 = value; break;
                case 27: K1 = value; break;
                case 28: GP = value; break;
                case 29: SP = value; break;
                case 30: FP = value; break;
                case 31: RA = value; break;
            }
        }
    }

    public CpuSnapshot Snapshot()
    {
        CpuSnapshot s = default;
        s.At = At;
        s.V0 = V0;
        s.V1 = V1;
        s.A0 = A0;
        s.A1 = A1;
        s.A2 = A2;
        s.A3 = A3;
        s.T0 = T0;
        s.T1 = T1;
        s.T2 = T2;
        s.T3 = T3;
        s.T4 = T4;
        s.T5 = T5;
        s.T6 = T6;
        s.T7 = T7;
        s.S0 = S0;
        s.S1 = S1;
        s.S2 = S2;
        s.S3 = S3;
        s.S4 = S4;
        s.S5 = S5;
        s.S6 = S6;
        s.S7 = S7;
        s.T8 = T8;
        s.T9 = T9;
        s.K0 = K0;
        s.K1 = K1;
        s.GP = GP;
        s.SP = SP;
        s.FP = FP;
        s.RA = RA;
        s.HI = HI;
        s.LO = LO;
        return s;
    }

    public void Restore(in CpuSnapshot s)
    {
        At = s.At;
        V0 = s.V0;
        V1 = s.V1;
        A0 = s.A0;
        A1 = s.A1;
        A2 = s.A2;
        A3 = s.A3;
        T0 = s.T0;
        T1 = s.T1;
        T2 = s.T2;
        T3 = s.T3;
        T4 = s.T4;
        T5 = s.T5;
        T6 = s.T6;
        T7 = s.T7;
        S0 = s.S0;
        S1 = s.S1;
        S2 = s.S2;
        S3 = s.S3;
        S4 = s.S4;
        S5 = s.S5;
        S6 = s.S6;
        S7 = s.S7;
        T8 = s.T8;
        T9 = s.T9;
        K0 = s.K0;
        K1 = s.K1;
        GP = s.GP;
        SP = s.SP;
        FP = s.FP;
        RA = s.RA;
        HI = s.HI;
        LO = s.LO;
    }
}