using RecompOne.Runtime.Cdrom;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Dispatch;
using RecompOne.Runtime.Hardware;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Bios;

public static class BiosA
{
    static uint _heapBase, _heapEnd;
    static readonly SortedDictionary<uint, uint> _free = new();
    static readonly Dictionary<uint, uint> _busy = new();
    static uint _randSeed = 1;
    static uint _strtokPtr = 0;

    static DiscFs? _fs;
    static CdController? _cd;
    static readonly Dictionary<uint, (string name, byte[] data, int offset)> _openFiles = new();
    static readonly Dictionary<uint, (MemoryCard card, int[] chain, int size, int pos)> _cardFiles = new();
    static uint _nextHandle = 2u;

    static List<(string name, int size)> _ff = new();
    static int _ffIdx;

    public static MemoryCard? CardFor(string path)
    {
        if (path.StartsWith("bu00:", StringComparison.OrdinalIgnoreCase)) return Runtime.CardA.Enabled ? Runtime.CardA : null;
        if (path.StartsWith("bu10:", StringComparison.OrdinalIgnoreCase)) return Runtime.CardB.Enabled ? Runtime.CardB : null;
        return null;
    }
    static string CardName(string path) { int i = path.IndexOf(':'); return i >= 0 ? path[(i + 1)..] : path; }

    static void WriteDirEntry(IMemory m, uint ptr, string name, int size)
    {
        for (int i = 0; i < 20; i++) m.WriteU8(ptr + (uint)i, i < name.Length ? (byte)name[i] : (byte)0);
        m.WriteU32(ptr + 0x14u, 0x50u);
        m.WriteU32(ptr + 0x18u, (uint)size);
        m.WriteU32(ptr + 0x1Cu, 0u);
        m.WriteU32(ptr + 0x20u, 0u);
        m.WriteU32(ptr + 0x24u, 0u);
    }

    public static uint FirstFile(IMemory m, uint wildPtr, uint dirPtr)
    {
        string wild = Bios.ReadString(m, wildPtr);
        var card = CardFor(wild);
        if (card == null) return 0u;
        _ff = card.Match(CardName(wild));
        _ffIdx = 0;
        return NextFileEntry(m, dirPtr);
    }

    public static uint NextFile(IMemory m, uint dirPtr) => NextFileEntry(m, dirPtr);

    static uint NextFileEntry(IMemory m, uint dirPtr)
    {
        if (_ffIdx >= _ff.Count) return 0u;
        var e = _ff[_ffIdx++];
        WriteDirEntry(m, dirPtr, e.name, e.size);
        return dirPtr;
    }

    public static uint CardDelete(IMemory m, uint pathPtr)
    {
        string path = Bios.ReadString(m, pathPtr);
        var card = CardFor(path);
        if (card == null) return 0u;
        card.Delete(CardName(path));
        return 1u;
    }

    public static uint CardFormat(IMemory m, uint pathPtr)
    {
        var card = CardFor(Bios.ReadString(m, pathPtr));
        if (card == null) { BiosB.DeliverEvent(0xF0000011u, 0x8000u); return 0u; }
        card.Format();
        BiosB.DeliverEvent(0xF0000011u, 0x0004u);
        return 1u;
    }

    public static uint TestDevice(IMemory m, uint pathPtr) => CardFor(Bios.ReadString(m, pathPtr)) != null ? 1u : 0u;

    static void CardEvent(uint port)
    {
        var card = (port & 0x10) != 0 ? Runtime.CardB : Runtime.CardA;
        BiosB.DeliverEvent(0xF4000001u, card.Enabled ? 0x0004u : 0x8000u);
    }

    static uint _confNumEvCB = 16, _confNumTCB = 4, _confStack = 0;
    public static uint LastErrno = 0;

    public static void SetFs(DiscFs fs) => _fs = fs;
    public static void SetCd(CdController cd) => _cd = cd;

    //as in https://problemkaputt.de/psxspx-kernel-bios.htm
    public static void Dispatch(CpuContext c, IMemory m, uint fn)
    {
        Log.Bios($"A({fn:X2}) {BiosNames.A(fn)}");
        switch (fn)
        {
            case 0x00:
            {
                string rawPath = Bios.ReadString(m, c.A0);
                var card = CardFor(rawPath);
                if (card != null)
                {
                    string cn = CardName(rawPath);
                    int first = card.Find(cn);
                    if (first == 0 && (c.A1 & 0x200u) != 0) first = card.Create(cn, (int)(c.A1 >> 16));
                    if (first == 0) { c.V0 = 0xFFFFFFFFu; LastErrno = 2; break; }
                    uint cfd = _nextHandle++;
                    _cardFiles[cfd] = (card, card.Chain(first), card.FileSize(first), 0);
                    c.V0 = cfd; LastErrno = 0;
                    break;
                }
                if (_fs == null) { c.V0 = 0xFFFFFFFFu; LastErrno = 13; break; }
                if (Runtime.Mode == RunMode.Retail && rawPath.StartsWith("sim:", StringComparison.OrdinalIgnoreCase)) { c.V0 = 0xFFFFFFFFu; LastErrno = 2; break; }
                string fileName = CdUtils.ExtractFileName(rawPath);
                try
                {
                    string? found = _fs.FindFile(fileName);
                    if (found == null) { c.V0 = 0xFFFFFFFFu; LastErrno = 2; break; }
                    byte[] data = _fs.ReadFile(found);
                    uint fd = _nextHandle++;
                    _openFiles[fd] = (CdUtils.OverlayName(fileName), data, 0);
                    c.V0 = fd;
                    LastErrno = 0;
                }
                catch { c.V0 = 0xFFFFFFFFu; LastErrno = 16; }
                break;
            }
            case 0x01:
            {
                uint fd = c.A0;
                if (_cardFiles.TryGetValue(fd, out var cse))
                {
                    int no = (int)c.A2 switch { 0 => (int)c.A1, 1 => cse.pos + (int)c.A1, 2 => cse.size + (int)c.A1, _ => cse.pos };
                    no = Math.Max(0, Math.Min(no, cse.size));
                    _cardFiles[fd] = (cse.card, cse.chain, cse.size, no);
                    c.V0 = (uint)no; LastErrno = 0; break;
                }
                if (!_openFiles.TryGetValue(fd, out var se)) { c.V0 = 0xFFFFFFFFu; LastErrno = 9; break; }
                int newOff = (int)c.A2 switch
                {
                    0 => (int)c.A1,
                    1 => se.offset + (int)c.A1,
                    2 => se.data.Length + (int)c.A1,
                    _ => se.offset
                };
                newOff = Math.Max(0, Math.Min(newOff, se.data.Length));
                _openFiles[fd] = (se.name, se.data, newOff);
                c.V0 = (uint)newOff;
                LastErrno = 0;
                break;
            }
            case 0x02:
            {
                uint fd = c.A0;
                if (_cardFiles.TryGetValue(fd, out var cre))
                {
                    int n = (int)Math.Min(c.A2, (uint)(cre.size - cre.pos));
                    for (int i = 0; i < n; i++) m.WriteU8(c.A1 + (uint)i, cre.card.ReadByte(cre.chain, cre.pos + i));
                    _cardFiles[fd] = (cre.card, cre.chain, cre.size, cre.pos + n);
                    BiosB.DeliverEvent(0xF4000001u, 0x0004u);
                    c.V0 = (uint)n; LastErrno = 0; break;
                }
                if (!_openFiles.TryGetValue(fd, out var re)) { c.V0 = 0xFFFFFFFFu; LastErrno = 9; break; }
                int count = (int)Math.Min(c.A2, (uint)(re.data.Length - re.offset));
                for (int i = 0; i < count; i++)
                    m.WriteU8(c.A1 + (uint)i, re.data[re.offset + i]);
                int newOffset = re.offset + count;
                _openFiles[fd] = (re.name, re.data, newOffset);
                c.V0 = (uint)count;
                LastErrno = 0;
                if (newOffset >= re.data.Length)
                    Dispatcher.TryLoad(re.name);
                break;
            }
            case 0x03:
            {
                uint fd = c.A0;
                if (fd <= 2u && !_cardFiles.ContainsKey(fd))
                {
                    var w = fd == 2u ? Console.Error : Console.Out;
                    for (uint i = 0; i < c.A2; i++) w.Write((char)m.ReadU8(c.A1 + i));
                    c.V0 = c.A2; LastErrno = 0; break;
                }
                if (_cardFiles.TryGetValue(fd, out var cwe))
                {
                    int n = (int)Math.Min(c.A2, (uint)(cwe.size - cwe.pos));
                    for (int i = 0; i < n; i++) cwe.card.WriteByte(cwe.chain, cwe.pos + i, m.ReadU8(c.A1 + (uint)i));
                    cwe.card.Flush();
                    _cardFiles[fd] = (cwe.card, cwe.chain, cwe.size, cwe.pos + n);
                    BiosB.DeliverEvent(0xF4000001u, 0x0004u);
                    c.V0 = (uint)n; LastErrno = 0; break;
                }
                c.V0 = 0xFFFFFFFFu; LastErrno = 9; break;
            }
            case 0x04:
            {
                _openFiles.Remove(c.A0);
                _cardFiles.Remove(c.A0);
                c.V0 = c.A0;
                LastErrno = 0;
                break;
            }
            case 0x05: c.V0 = 0xFFFFFFFFu; break;
            case 0x06: Environment.Exit((int)c.A0); break;
            case 0x07: c.V0 = c.A0 <= 2u ? 2u : 0u; break;
            case 0x08: c.V0 = 0xFFFFFFFFu; break;
            case 0x09: Console.Write((char)(c.A0 & 0xFF)); c.V0 = c.A0; break;
            case 0x0A: c.V0 = char.IsDigit((char)(c.A0 & 0xFF)) ? (c.A0 & 0xFFu) - '0' : 0xFFFFFFFFu; break;
            case 0x0B: c.V0 = 0u; break;
            case 0x0C: c.V0 = BStrtoul(m, c.A0, c.A1, c.A2); break;
            case 0x0D: c.V0 = (uint)BStrtol(m, c.A0, c.A1, c.A2); break;
            case 0x0E: c.V0 = (uint)Math.Abs((int)c.A0); break;
            case 0x0F: c.V0 = (uint)Math.Abs((int)c.A0); break;
            case 0x10: c.V0 = (uint)BAtoi(Bios.ReadString(m, c.A0)); break;
            case 0x11: c.V0 = (uint)BAtoi(Bios.ReadString(m, c.A0)); break;
            case 0x12:
            {
                string src = Bios.ReadString(m, c.A0).TrimStart();
                int sign = 1, idx = 0, result = 0;
                if (idx < src.Length && src[idx] == '-') { sign = -1; idx++; }
                bool ok = false;
                while (idx < src.Length && char.IsDigit(src[idx])) { result = result * 10 + (src[idx++] - '0'); ok = true; }
                if (!ok) { c.V0 = 0u; break; }
                m.WriteU32(c.A1, (uint)(sign * result));
                c.V0 = 1u;
                break;
            }
            case 0x13:
            {
                uint buf = c.A0;
                m.WriteU32(buf + 0x00u, c.RA); m.WriteU32(buf + 0x04u, c.SP);
                m.WriteU32(buf + 0x08u, c.FP); m.WriteU32(buf + 0x0Cu, c.GP);
                m.WriteU32(buf + 0x10u, c.S0); m.WriteU32(buf + 0x14u, c.S1);
                m.WriteU32(buf + 0x18u, c.S2); m.WriteU32(buf + 0x1Cu, c.S3);
                m.WriteU32(buf + 0x20u, c.S4); m.WriteU32(buf + 0x24u, c.S5);
                m.WriteU32(buf + 0x28u, c.S6); m.WriteU32(buf + 0x2Cu, c.S7);
                c.V0 = 0u;
                break;
            }
            case 0x14:
            {
                uint buf = c.A0;
                c.RA = m.ReadU32(buf + 0x00u); c.SP = m.ReadU32(buf + 0x04u);
                c.FP = m.ReadU32(buf + 0x08u); c.GP = m.ReadU32(buf + 0x0Cu);
                c.S0 = m.ReadU32(buf + 0x10u); c.S1 = m.ReadU32(buf + 0x14u);
                c.S2 = m.ReadU32(buf + 0x18u); c.S3 = m.ReadU32(buf + 0x1Cu);
                c.S4 = m.ReadU32(buf + 0x20u); c.S5 = m.ReadU32(buf + 0x24u);
                c.S6 = m.ReadU32(buf + 0x28u); c.S7 = m.ReadU32(buf + 0x2Cu);
                c.V0 = c.A1 != 0u ? c.A1 : 1u;
                break;
            }
            case 0x15: c.V0 = BStrcat(m, c.A0, c.A1); break;
            case 0x16: c.V0 = BStrncat(m, c.A0, c.A1, c.A2); break;
            case 0x17: c.V0 = BStrcmp(m, c.A0, c.A1); break;
            case 0x18: c.V0 = BStrncmp(m, c.A0, c.A1, c.A2); break;
            case 0x19: c.V0 = BStrcpy(m, c.A0, c.A1); break;
            case 0x1A: c.V0 = BStrncpy(m, c.A0, c.A1, c.A2); break;
            case 0x1B: c.V0 = BStrlen(m, c.A0); break;
            case 0x1C: c.V0 = BStrchr(m, c.A0, c.A1); break;
            case 0x1D: c.V0 = BStrrchr(m, c.A0, c.A1); break;
            case 0x1E: c.V0 = BStrchr(m, c.A0, c.A1); break;
            case 0x1F: c.V0 = BStrrchr(m, c.A0, c.A1); break;
            case 0x20: c.V0 = BStrpbrk(m, c.A0, c.A1); break;
            case 0x21: c.V0 = BStrspn(m, c.A0, c.A1); break;
            case 0x22: c.V0 = BStrcspn(m, c.A0, c.A1); break;
            case 0x23: c.V0 = BStrtok(m, c.A0, c.A1); break;
            case 0x24: c.V0 = BStrstr(m, c.A0, c.A1); break;
            case 0x25: c.V0 = (byte)char.ToUpperInvariant((char)(c.A0 & 0xFF)); break;
            case 0x26: c.V0 = (byte)char.ToLowerInvariant((char)(c.A0 & 0xFF)); break;
            case 0x27: BMemcpy(m, c.A1, c.A0, c.A2); c.V0 = c.A1; break;
            case 0x28: BMemset(m, c.A0, 0, c.A1); break;
            case 0x29: c.V0 = BMemcmp(m, c.A0, c.A1, c.A2); break;
            case 0x2A: c.V0 = BMemcpy(m, c.A0, c.A1, c.A2); break;
            case 0x2B: c.V0 = BMemset(m, c.A0, (byte)c.A1, c.A2); break;
            case 0x2C: c.V0 = BMemmove(m, c.A0, c.A1, c.A2); break;
            case 0x2D: c.V0 = BMemcmp(m, c.A0, c.A1, c.A2); break;
            case 0x2E: c.V0 = BMemchr(m, c.A0, (byte)c.A1, c.A2); break;
            case 0x2F: c.V0 = BRand(); break;
            case 0x30: _randSeed = c.A0; break;
            case 0x31:
            {
                uint qb = c.A0, qn = c.A1, qs = c.A2, qc = c.A3;
                for (uint i = 1; i < qn; i++)
                {
                    uint j = i;
                    while (j > 0)
                    {
                        uint pa = qb + (j - 1) * qs, pb = qb + j * qs;
                        c.A0 = pa; c.A1 = pb;
                        Dispatcher.Call(c, m, qc);
                        if ((int)c.V0 <= 0) break;
                        for (uint k = 0; k < qs; k++) { byte t = m.ReadU8(pa + k); m.WriteU8(pa + k, m.ReadU8(pb + k)); m.WriteU8(pb + k, t); }
                        j--;
                    }
                }
                break;
            }
            case 0x32: c.V0 = 0u; break;
            case 0x33: c.V0 = Malloc(c.A0); break;
            case 0x34: Free(c.A0); break;
            case 0x35:
            {
                uint key = c.A0, ubase = c.A1, nel = c.A2, width = c.A3;
                uint cmp = m.ReadU32(c.SP + 0x10u);
                c.V0 = 0u;
                for (uint i = 0; i < nel; i++)
                {
                    uint elem = ubase + i * width;
                    c.A0 = key; c.A1 = elem;
                    Dispatcher.Call(c, m, cmp);
                    if (c.V0 == 0) { c.V0 = elem; break; }
                }
                if (c.V0 == 0)
                {
                    uint newElem = ubase + nel * width;
                    BMemcpy(m, newElem, key, width);
                    c.V0 = newElem;
                }
                break;
            }
            case 0x36:
            {
                uint key = c.A0, ubase = c.A1, nel = c.A2, width = c.A3;
                uint cmp = m.ReadU32(c.SP + 0x10u);
                int lo = 0, hi = (int)nel - 1;
                c.V0 = 0u;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    uint elem = ubase + (uint)mid * width;
                    c.A0 = key; c.A1 = elem;
                    Dispatcher.Call(c, m, cmp);
                    int diff = (int)c.V0;
                    if (diff == 0) { c.V0 = elem; break; }
                    if (diff < 0) hi = mid - 1; else lo = mid + 1;
                }
                break;
            }
            case 0x37: c.V0 = Calloc(m, c.A0, c.A1); break;
            case 0x38: c.V0 = Realloc(m, c.A0, c.A1); break;
            case 0x39: InitHeap(c.A0, c.A1); break;
            case 0x3A: Environment.Exit(1); break;
            case 0x3B: c.V0 = 0xFFFFFFFFu; break;
            case 0x3C: Console.Write((char)(c.A0 & 0xFF)); c.V0 = c.A0; break;
            case 0x3D: c.V0 = 0u; break;
            case 0x3E: Console.Write(Bios.ReadString(m, c.A0)); c.V0 = c.A0; break;
            case 0x3F: Console.Write(Bios.FormatString(m, c, Bios.ReadString(m, c.A0))); c.V0 = 0u; break;
            case 0x40: throw new Exception("BIoS A(40h) SystemErrorUnresolvedException");
            case 0x41: c.V0 = DoLoad(m, c.A0, c.A1, false); break;
            case 0x42: c.V0 = DoLoad(m, c.A0, c.A1, true); break;
            case 0x43: c.V0 = DoExec(c, m, c.A0, c.A1, c.A2); break;
            case 0x44: break;
            case 0x45: break;
            case 0x46: case 0x47: case 0x48: case 0x49:
            case 0x4A: case 0x4B: case 0x4C: break;
            case 0x4D: c.V0 = 0u; break;
            case 0x4E: break;
            case 0x51:
            {
                if (DoLoad(m, c.A0, ExecHeaderScratch, true) == 0u) { c.V0 = 0u; break; }
                if (c.A1 != 0u) { m.WriteU32(ExecHeaderScratch + 0x20u, c.A1); m.WriteU32(ExecHeaderScratch + 0x24u, c.A2); }
                c.V0 = DoExec(c, m, ExecHeaderScratch, 0u, 0u);
                break;
            }
            case 0x53: case 0x54: case 0x55: case 0x56:
            case 0x5C: case 0x67: case 0x68:
            case 0x70: case 0x71: case 0x72: break;
            case 0x78:
            {
                if (_cd == null) { c.V0 = 0u; break; }
                uint src = c.A0;
                _cd.QueueAsyncSeekL(m.ReadU8(src), m.ReadU8(src + 1), m.ReadU8(src + 2));
                c.V0 = 1u;
                break;
            }
            case 0x7C:
            {
                if (_cd == null) { c.V0 = 0u; break; }
                _cd.QueueAsyncGetStatus();
                c.V0 = 1u;
                break;
            }
            case 0x7E:
            {
                if (_cd == null) { c.V0 = 0u; break; }
                _cd.QueueAsyncReadSector(c.A0, c.A1, c.A2);
                c.V0 = 1u;
                break;
            }
            case 0x81:
            {
                if (_cd == null) { c.V0 = 0u; break; }
                _cd.QueueAsyncSetMode((byte)c.A0);
                c.V0 = 1u;
                break;
            }
            case 0x9C:
                _confNumEvCB = c.A0;
                _confNumTCB = c.A1;
                _confStack = c.A2;
                break;
            case 0x9D:
                if (c.A0 != 0) m.WriteU32(c.A0, _confNumEvCB);
                if (c.A1 != 0) m.WriteU32(c.A1, _confNumTCB);
                if (c.A2 != 0) m.WriteU32(c.A2, _confStack);
                break;
            case 0xA0: break;
            case 0xA1: break;
            case 0xA2: case 0xA3: break;
            case 0xA4: c.V0 = 0xFFFFFFFFu; break;
            case 0xA5:
            {
                if (_cd == null) { c.V0 = 0xFFFFFFFFu; break; }
                uint count = c.A0, lba = c.A1, buffer = c.A2;
                for (uint i = 0; i < count; i++)
                {
                    byte[] data = _cd.ReadSectorData((int)(lba + i));
                    for (int j = 0; j < Math.Min(data.Length, 2048); j++)
                        m.WriteU8(buffer + i * 2048u + (uint)j, data[j]);
                }
                c.V0 = count;
                break;
            }
            case 0xA6: c.V0 = _cd != null ? _cd.DriveStatusByte() : 0x02u; break;
            case 0xAB: BiosB.CardComplete(c, m, c.A0); c.V0 = 1u; break;
            case 0xAC: BiosB.CardComplete(c, m, c.A0); c.V0 = 1u; break;
            case 0xA7: case 0xA8: case 0xA9: case 0xAA:
            case 0xAD: case 0xAE: case 0xAF: break;
            case 0xB4:
                c.V0 = c.A0 switch
                {
                    0 => 0xFFFFFFFFu,
                    1 => 0x00000001u,
                    2 => 0x00000001u,
                    _ => 0u
                };
                break;
            default: 
                
                
                break;
        }
    }

    const uint ExecHeaderScratch = 0x1F800340u;

    static uint DoLoad(IMemory m, uint filenamePtr, uint hdr, bool loadBody)
    {
        if (_fs == null) return 0u;
        string name = CdUtils.ExtractFileName(Bios.ReadString(m, filenamePtr));
        byte[] data;
        try
        {
            string? found = _fs.FindFile(name);
            if (found == null) return 0u;
            data = _fs.ReadFile(found);
        }
        catch { return 0u; }
        if (data.Length < 0x800 || data[0] != (byte)'P' || data[1] != (byte)'S') return 0u;
        for (uint i = 0; i < 40u; i++) m.WriteU8(hdr + i, data[0x10 + (int)i]);
        for (uint i = 40u; i < 60u; i++) m.WriteU8(hdr + i, 0);
        if (loadBody)
        {
            uint tAddr = BitConverter.ToUInt32(data, 0x18);
            uint tSize = BitConverter.ToUInt32(data, 0x1C);
            for (uint i = 0; i < tSize && 0x800 + i < data.Length; i++)
                m.WriteU8(tAddr + i, data[0x800 + (int)i]);
            Dispatcher.TryLoad(CdUtils.OverlayName(name));
        }
        return hdr;
    }

    static uint DoExec(CpuContext c, IMemory m, uint hdr, uint argc, uint argv)
    {
        uint pc0 = m.ReadU32(hdr + 0x00u);
        if (pc0 == 0u) return 0u;
        uint gp0 = m.ReadU32(hdr + 0x04u);
        uint bAddr = m.ReadU32(hdr + 0x18u);
        uint bSize = m.ReadU32(hdr + 0x1Cu);
        uint sAddr = m.ReadU32(hdr + 0x20u);
        uint sSize = m.ReadU32(hdr + 0x24u);

        for (uint i = 0; i < bSize; i++) m.WriteU8(bAddr + i, 0);

        uint savSp = c.SP, savFp = c.FP, savGp = c.GP, savRa = c.RA;
        uint savA0 = c.A0, savA1 = c.A1, savA2 = c.A2, savA3 = c.A3;

        m.WriteU32(hdr + 0x28u, c.SP);
        m.WriteU32(hdr + 0x2Cu, c.FP);
        m.WriteU32(hdr + 0x30u, c.GP);
        m.WriteU32(hdr + 0x34u, c.RA);

        c.GP = gp0;
        if (sAddr != 0u) { c.SP = sAddr + sSize; c.FP = c.SP; }
        c.A0 = argc;
        c.A1 = argv;
        Dispatcher.Call(c, m, pc0);

        c.SP = savSp; c.FP = savFp; c.GP = savGp; c.RA = savRa;
        c.A0 = savA0; c.A1 = savA1; c.A2 = savA2; c.A3 = savA3;
        return 1u;
    }

    static void InitHeap(uint ubase, uint size) //why is base a reserved keyword?
    {
        _heapBase = ubase;
        _heapEnd = ubase + size;
        _free.Clear();
        _busy.Clear();
        _free[ubase] =ubase + size;
    }
    static uint Malloc(uint size)
    {
        if (size == 0) size = 4;
        size = (size + 3) & ~3u;
        foreach (var kv in _free)
        {
            if (kv.Value - kv.Key >= size)
            {
                uint addr = kv.Key, end = kv.Value;
                _free.Remove(addr);
                if (end - addr > size) _free[addr + size] = end;
                _busy[addr] = size;
                return addr;
            }
        }
        return 0u;
    }

    static void Free(uint addr)
    {
        if (addr == 0 || !_busy.TryGetValue(addr, out uint size)) return;
        _busy.Remove(addr);
        uint end = addr + size;
        if (_free.TryGetValue(end, out uint nextEnd)) { _free.Remove(end); end = nextEnd; }
        _free[addr] = end;
    }

    static uint Calloc(IMemory m, uint count, uint size)
    {
        uint total = count * size;
        uint ptr = Malloc(total);
        if (ptr != 0) BMemset(m, ptr, 0, total);
        return ptr;
    }

    static uint Realloc(IMemory m, uint ptr, uint size)
    {
        if (ptr == 0) return Malloc(size);
        if (size == 0) { Free(ptr); return 0u; }
        uint oldSize = _busy.TryGetValue(ptr, out uint s) ? s : 0u;
        uint newPtr = Malloc(size);
        if (newPtr == 0) return 0u;
        BMemcpy(m, newPtr, ptr, Math.Min(oldSize, size));
        Free(ptr);
        return newPtr;
    }

    static uint BRand()
    {
        _randSeed = _randSeed * 1103515245u + 12345u;
        return (_randSeed >> 16) & 0x7FFFu;
    }

    static uint BStrlen(IMemory m, uint addr)
    {
        uint n = 0;
        while (m.ReadU8(addr++) != 0) n++;
        return n;
    }
    static uint BStrcmp(IMemory m, uint a, uint b)
    {
        while (true)
        {
            byte ba = m.ReadU8(a++), bb = m.ReadU8(b++);
            if (ba != bb) return ba < bb ? unchecked((uint)-1) : 1u;
            if (ba == 0) return 0u;
        }
    }

    static uint BStrncmp(IMemory m, uint a, uint b, uint n)
    {
        for (uint i = 0; i < n; i++)
        {
            byte ba = m.ReadU8(a++), bb = m.ReadU8(b++);
            if (ba != bb) return ba < bb ? unchecked((uint)-1) : 1u;
            if (ba == 0) return 0u;
        }
        return 0u;
    }
    static uint BStrcpy(IMemory m, uint dst, uint src)
    {
        uint d = dst; byte b;
        do
        {
            b = m.ReadU8(src++); m.WriteU8(d++, b);
        } while (b != 0);
        return dst;
    }

    static uint BStrncpy(IMemory m, uint dst, uint src, uint n)
    {
        uint d = dst;
        for (uint i = 0; i < n; i++)
        {
            byte b = m.ReadU8(src++);
            m.WriteU8(d++, b);
            if (b == 0) { while (++i < n) m.WriteU8(d++, 0); break; }
        }
        return dst;
    }

    static uint BStrcat(IMemory m, uint dst, uint src)
    {
        uint d = dst;
        while (m.ReadU8(d) != 0) d++;
        byte b;
        do
        {
            b = m.ReadU8(src++); m.WriteU8(d++, b);
        } while (b != 0);
        return dst;
    }

    static uint BStrncat(IMemory m, uint dst, uint src, uint n)
    {
        uint d = dst;
        while (m.ReadU8(d) != 0) d++;
        for (uint i = 0; i < n; i++)
        {
            byte b = m.ReadU8(src++);
            if (b == 0) break;
            m.WriteU8(d++, b);
        }
        m.WriteU8(d, 0);
        return dst;
    }

    static uint BStrchr(IMemory m, uint str, uint ch)
    {
        byte target = (byte)(ch & 0xFF);
        uint addr = str;
        while (true)
        {
            byte b = m.ReadU8(addr);
            if (b == target) return addr;
            if (b == 0) return 0u;
            addr++;
        }
    }
    static uint BStrrchr(IMemory m, uint str, uint ch)
    {
        byte target = (byte)(ch & 0xFF);
        uint addr = str, last = 0u;
        while (true)
        {
            byte b = m.ReadU8(addr);
            if (b == target) last = addr;
            if (b == 0) return last;
            addr++;
        }
    }

    
    static uint BStrpbrk(IMemory m, uint str, uint accept)
    {
        uint addr = str;
        while (true)
        {
            byte b = m.ReadU8(addr);
            if (b == 0) return 0u;
            if (BStrchr(m, accept, b) != 0) return addr;
            addr++;
        }
    }

    static uint BStrspn(IMemory m, uint str, uint accept)
    {
        uint n = 0;
        while (true)
        {
            byte b = m.ReadU8(str + n);
            if (b == 0 || BStrchr(m, accept, b) == 0) return n;
            n++;
        }
    }
    static uint BStrcspn(IMemory m, uint str, uint reject)
    {
        uint n = 0;
        while (true)
        {
            byte b = m.ReadU8(str + n);
            if (b == 0 || BStrchr(m, reject, b) != 0) return n;
            n++;
        }
    }

    
    static uint BStrstr(IMemory m, uint str, uint sub)
    {
        uint subLen = BStrlen(m, sub);
        if (subLen == 0) return str;
        uint len = BStrlen(m, str);
        for (uint i = 0; i + subLen <= len; i++)
            if (BStrncmp(m, str + i, sub, subLen) == 0) return str + i;
        return 0u;
    }
    static uint BStrtok(IMemory m, uint str, uint delim)
    {
        if (str != 0) _strtokPtr = str;
        while (_strtokPtr != 0 && m.ReadU8(_strtokPtr) != 0 && BStrchr(m, delim, m.ReadU8(_strtokPtr)) != 0) _strtokPtr++;
        if (m.ReadU8(_strtokPtr) == 0) return 0u;
        uint start = _strtokPtr;
        while (m.ReadU8(_strtokPtr) != 0 && BStrchr(m, delim, m.ReadU8(_strtokPtr)) == 0) _strtokPtr++;
        if (m.ReadU8(_strtokPtr) != 0) { m.WriteU8(_strtokPtr, 0); _strtokPtr++; }
        return start;
    }

    static uint BMemcpy(IMemory m, uint dst, uint src, uint n)
    {
        for (uint i = 0; i < n; i++) m.WriteU8(dst + i, m.ReadU8(src + i));
        return dst;
    }

    static uint BMemmove(IMemory m, uint dst, uint src, uint n)
    {
        if (dst <= src || dst >= src + n) return BMemcpy(m, dst, src, n);
        for (uint i = n; i > 0; i--) m.WriteU8(dst + i - 1, m.ReadU8(src + i - 1));
        return dst;
    }

    static uint BMemset(IMemory m, uint ptr, byte val, uint n)
    {
        for (uint i = 0; i < n; i++) m.WriteU8(ptr + i, val);
        return ptr;
    }

    static uint BMemcmp(IMemory m, uint a, uint b, uint n)
    {
        for (uint i = 0; i < n; i++)
        {
            byte ba = m.ReadU8(a + i), bb = m.ReadU8(b + i);
            if (ba != bb) return ba < bb ? unchecked((uint)-1) : 1u;
        }
        return 0u;
    }

    static uint BMemchr(IMemory m, uint ptr, byte val, uint n)
    {
        for (uint i = 0; i < n; i++)
            if (m.ReadU8(ptr + i) == val) return ptr + i;
        return 0u;
    }

    static int BAtoi(string s)
    {
        s = s.TrimStart();
        int sign = 1, i = 0, result = 0;
        if (i < s.Length && s[i] == '-') { sign = -1; i++; }
        else if (i < s.Length && s[i] == '+') i++;
        while (i < s.Length && char.IsDigit(s[i])) result = result * 10 + (s[i++] - '0');
        return sign * result;
    }
    static uint BStrtoul(IMemory m, uint str, uint endptrPtr, uint ubase)
    {
        string s = Bios.ReadString(m, str).TrimStart();
        int i = 0;
        if (s.Length > 1 && s[0] == '0' && (s[1] == 'x' || s[1] == 'X') && (ubase == 0 || ubase == 16))
        { i = 2; ubase = 16; }
        else if (ubase == 0) ubase = s.Length > 0 && s[0] == '0' ? 8u : 10u;
        uint result = 0;
        while (i < s.Length)
        {
            int d = s[i] >= '0' && s[i] <= '9' ? s[i] - '0' : s[i] >= 'a' && s[i] <= 'f' ? s[i] - 'a' + 10 : s[i] >= 'A' && s[i] <= 'F' ? s[i] - 'A' + 10 : -1;
            if (d < 0 || d >= (int)ubase) break;
            result = result * ubase + (uint)d; i++;
        }
        if (endptrPtr != 0) m.WriteU32(endptrPtr, str + (uint)i);
        return result;
    }
    static int BStrtol(IMemory m, uint str, uint endptrPtr, uint ubase)
    {
        string s = Bios.ReadString(m, str).TrimStart();
        int i = 0, sign = 1;
        if (i < s.Length && s[i] == '-') { sign = -1; i++; }
        else if (i < s.Length && s[i] == '+') i++;
        uint strOff = (uint)i;
        uint tmp = BStrtoul(m, str + strOff, endptrPtr, ubase);
        return sign * (int)tmp;
    }
}
