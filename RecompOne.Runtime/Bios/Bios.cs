using System.Text;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Bios;

public static class Bios
{
    const uint DcbBase = 0x0000E200u;
    const uint DcbCount = 0x0Au;
    const uint DcbSize = 0x50u;
    const uint DcbStrings = DcbBase + DcbCount * DcbSize;

    public static void Init(IMemory m)
    {
        InstallDevices(m);
    }
    
    //not fully sure
    static void InstallDevices(IMemory m)
    {
        for (uint i = 0; i < DcbCount * DcbSize; i += 4) m.WriteU32(DcbBase + i, 0u);

        uint shortName = WriteDeviceName(m, DcbStrings, "bu");
        uint longName = WriteDeviceName(m, shortName, "MEMORY CARD");

        m.WriteU32(DcbBase + 0x00u, DcbStrings);
        m.WriteU32(DcbBase + 0x04u, 0x14u);
        m.WriteU32(DcbBase + 0x08u, 0x80u);
        m.WriteU32(DcbBase + 0x0Cu, shortName);
        m.WriteU32(0x00000150u, DcbBase);
        m.WriteU32(0x00000154u, DcbCount * DcbSize);
    }

    static uint WriteDeviceName(IMemory m, uint addr, string text)
    {
        foreach (char ch in text) m.WriteU8(addr++, (byte)ch);
        m.WriteU8(addr++, 0);
        return addr;
    }

    public static bool TryDispatch(CpuContext c, IMemory m, uint addr)
    {
        switch (addr)
        {
            case 0xA0u: BiosA.Dispatch(c, m, c.T1); return true;
            case 0xB0u: BiosB.Dispatch(c, m, c.T1); return true;
            case 0xC0u: BiosC.Dispatch(c, m, c.T1); return true;
        }
        if ((addr & 0xFF000000u) == 0xBFC00000u) return true;
        return false;
    }

    internal static string ReadString(IMemory m, uint addr)
    {
        var sb = new StringBuilder();
        byte b;
        while ((b = m.ReadU8(addr++)) != 0) sb.Append((char)b);
        return sb.ToString();
    }
    internal static void WriteString(IMemory m, uint addr, string s)
    {
        foreach (char ch in s) m.WriteU8(addr++, (byte)ch);
        m.WriteU8(addr, 0);
    }

    //util
    internal static string FormatString(IMemory m, CpuContext c, string fmt)
    {
        var sb = new StringBuilder();
        int ai = 0;
        int i = 0;
        while (i < fmt.Length)
        {
            if (fmt[i] != '%') { sb.Append(fmt[i++]); continue; }
            if (++i >= fmt.Length) break;
            if (fmt[i] == '%') { sb.Append('%'); i++; continue; }
            if (i < fmt.Length && fmt[i] == '-') i++;
            bool zeroPad = i < fmt.Length && fmt[i] == '0';
            if (zeroPad) i++;
            int width = 0;
            while (i < fmt.Length && char.IsDigit(fmt[i])) { width = width * 10 + (fmt[i++] - '0'); }
            if (i < fmt.Length && fmt[i] == '.') { i++; while (i < fmt.Length && char.IsDigit(fmt[i])) i++; }
            if (i >= fmt.Length) break;

            char conv = fmt[i++];
            int argIdx = ai++;
            uint arg = argIdx switch
            {
                0 => c.A1, 1 => c.A2, 2 => c.A3,
                _ => m.ReadU32(c.SP + 16u + (uint)((argIdx - 3) * 4))
            };

            string s;
            switch (conv)
            {
                case 'd': case 'i': s = ((int)arg).ToString(); break;
                case 'u': s = arg.ToString(); break;
                case 'x': s = arg.ToString("x"); break;
                case 'X': s = arg.ToString("X"); break;
                case 'o': s = Convert.ToString(arg, 8); break;
                case 'c': s = ((char)(arg & 0xFF)).ToString(); break;
                case 's': s = arg != 0 ? ReadString(m, arg) : "(null)"; break;
                default: s = $"%{conv}"; ai--; break;
            }
            if (width > 0) s = zeroPad ? s.PadLeft(width, '0') : s.PadLeft(width);
            sb.Append(s);
        }
        return sb.ToString();
    }
}
