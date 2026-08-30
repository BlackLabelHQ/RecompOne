using RecompOne.Runtime.Bios;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Hardware;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Sdk;

//mem card impl
public static class LibMcrd
{
    private const uint McErrNone = 0;
    private const uint McErrCardNotExist = 1;
    private const uint McErrCardInvalid = 2;
    private const uint McErrNewCard = 3;
    private const uint McErrFileNotExist = 5;
    private const uint McErrAlreadyExist = 6;
    private const uint McErrBlockFull = 7;

    private const uint McFuncExist = 1;
    private const uint McFuncAccept = 2;
    private const uint McFuncReadFile = 3;
    private const uint McFuncWriteFile = 4;
    private const uint McFuncReadData = 5;
    private const uint McFuncWriteData = 6;

    private const int DirEntrySize = 0x28;
    private const int BlockSize = 8192;

    private static uint _callback;

    private static bool _pending;
    private static uint _pendingCmd;
    private static uint _pendingResult;

    private static readonly bool[] _seen = new bool[2];

    private static MemoryCard? _openCard;
    private static string _openName = "";
    private static uint _openFlag;

    public static void Reset()
    {
        _callback = 0u;
        _pending = false;
        _pendingCmd = _pendingResult = 0u;
        _seen[0] = _seen[1] = false;
        _openCard = null;
        _openName = "";
        _openFlag = 0u;
    }

    private static int Slot(uint chan)
    {
        return (chan & 0x10u) != 0 ? 1 : 0;
    }

    private static MemoryCard Card(uint chan)
    {
        return Slot(chan) == 1 ? Runtime.CardB : Runtime.CardA;
    }

    private static uint Post(uint cmd, uint result)
    {
        _pendingCmd = cmd;
        _pendingResult = result;
        _pending = true;
        return 1u;
    }

    private static uint Probe(uint chan, bool accept)
    {
        var card = Card(chan);
        if (!card.Enabled) return McErrCardNotExist;

        var slot = Slot(chan);
        if (_seen[slot]) return McErrNone;
        if (accept) _seen[slot] = true;
        return McErrNewCard;
    }

    public static void MemCardInit(CpuContext c, IMemory m)
    {
        Reset();
        c.V0 = 1u;
    }

    public static void MemCardEnd(CpuContext c, IMemory m)
    {
        Reset();
        c.V0 = 0u;
    }

    public static void MemCardStart(CpuContext c, IMemory m)
    {
        c.V0 = 0u;
    }

    public static void MemCardStop(CpuContext c, IMemory m)
    {
        c.V0 = 0u;
    }

    public static void MemCardCallback(CpuContext c, IMemory m)
    {
        var prev = _callback;
        _callback = c.A0;
        c.V0 = prev;
    }

    public static void MemCardExist(CpuContext c, IMemory m)
    {
        c.V0 = Post(McFuncExist, Probe(c.A0, false));
    }

    public static void MemCardAccept(CpuContext c, IMemory m)
    {
        c.V0 = Post(McFuncAccept, Probe(c.A0, true));
    }

    public static void MemCardOpen(CpuContext c, IMemory m)
    {
        if (_pending)
        {
            c.V0 = 0xFFFFFFFFu;
            return;
        }

        var card = Card(c.A0);
        var name = Bios.Bios.ReadString(m, c.A1);
        if (!card.Enabled)
        {
            c.V0 = McErrCardNotExist;
            return;
        }

        if (card.Find(name) == 0)
        {
            c.V0 = McErrFileNotExist;
            return;
        }

        _openCard = card;
        _openName = name;
        _openFlag = c.A2;
        c.V0 = McErrNone;
    }

    public static void MemCardClose(CpuContext c, IMemory m)
    {
        _openCard = null;
        _openName = "";
        c.V0 = 0u;
    }

    public static void MemCardReadData(CpuContext c, IMemory m)
    {
        if (_openCard == null)
        {
            c.V0 = 0u;
            return;
        }

        c.V0 = Post(McFuncReadData, Transfer(m, _openCard, _openName, c.A0, (int)c.A1, (int)c.A2, false));
    }

    public static void MemCardWriteData(CpuContext c, IMemory m)
    {
        if (_openCard == null)
        {
            c.V0 = 0u;
            return;
        }

        c.V0 = Post(McFuncWriteData, Transfer(m, _openCard, _openName, c.A0, (int)c.A1, (int)c.A2, true));
    }

    public static void MemCardReadFile(CpuContext c, IMemory m)
    {
        if (_openCard != null || _pending)
        {
            c.V0 = 0u;
            return;
        }

        var bytes = (int)m.ReadU32(c.SP + 0x10u);
        var name = Bios.Bios.ReadString(m, c.A1);
        if ((bytes & 0x7F) != 0)
        {
            c.V0 = 0u;
            return;
        }

        c.V0 = Post(McFuncReadFile, Transfer(m, Card(c.A0), name, c.A2, (int)c.A3, bytes, false));
    }

    public static void MemCardWriteFile(CpuContext c, IMemory m)
    {
        if (_openCard != null || _pending)
        {
            c.V0 = 0u;
            return;
        }

        var bytes = (int)m.ReadU32(c.SP + 0x10u);
        var name = Bios.Bios.ReadString(m, c.A1);
        if ((bytes & 0x7F) != 0)
        {
            c.V0 = 0u;
            return;
        }

        c.V0 = Post(McFuncWriteFile, Transfer(m, Card(c.A0), name, c.A2, (int)c.A3, bytes, true));
    }

    private static uint Transfer(IMemory m, MemoryCard card, string name, uint addr, int offset, int bytes, bool write)
    {
        if (!card.Enabled) return McErrCardNotExist;

        var first = card.Find(name);
        if (first == 0) return McErrFileNotExist;

        var chain = card.Chain(first);
        var size = card.FileSize(first);
        if (offset < 0 || bytes < 0 || offset + bytes > size) return McErrCardInvalid;

        for (var i = 0; i < bytes; i++)
            if (write) card.WriteByte(chain, offset + i, m.ReadU8(addr + (uint)i));
            else m.WriteU8(addr + (uint)i, card.ReadByte(chain, offset + i));

        if (write) card.Flush();
        return McErrNone;
    }

    public static void MemCardCreateFile(CpuContext c, IMemory m)
    {
        if (_pending)
        {
            c.V0 = 0xFFFFFFFFu;
            return;
        }

        var card = Card(c.A0);
        if (!card.Enabled)
        {
            c.V0 = McErrCardNotExist;
            return;
        }

        var name = Bios.Bios.ReadString(m, c.A1);
        if (card.Find(name) != 0)
        {
            c.V0 = McErrAlreadyExist;
            return;
        }

        if (card.Create(name, (int)c.A2) == 0)
        {
            c.V0 = McErrBlockFull;
            return;
        }

        card.Flush();
        c.V0 = McErrNone;
    }

    public static void MemCardDeleteFile(CpuContext c, IMemory m)
    {
        if (_pending)
        {
            c.V0 = 0xFFFFFFFFu;
            return;
        }

        var card = Card(c.A0);
        if (!card.Enabled)
        {
            c.V0 = McErrCardNotExist;
            return;
        }

        var name = Bios.Bios.ReadString(m, c.A1);
        if (card.Find(name) == 0)
        {
            c.V0 = McErrFileNotExist;
            return;
        }

        card.Delete(name);
        card.Flush();
        c.V0 = McErrNone;
    }

    public static void MemCardFormat(CpuContext c, IMemory m)
    {
        if (_pending)
        {
            c.V0 = 0xFFFFFFFFu;
            return;
        }

        var card = Card(c.A0);
        if (!card.Enabled)
        {
            c.V0 = McErrCardNotExist;
            return;
        }

        card.Format();
        card.Flush();
        _seen[Slot(c.A0)] = true;
        c.V0 = McErrNone;
    }

    public static void MemCardUnformat(CpuContext c, IMemory m)
    {
        if (_pending)
        {
            c.V0 = 0xFFFFFFFFu;
            return;
        }

        _seen[Slot(c.A0)] = false;
        c.V0 = 1u;
    }

    public static void MemCardGetDirentry(CpuContext c, IMemory m)
    {
        if (_pending)
        {
            c.V0 = 0xFFFFFFFFu;
            return;
        }

        var card = Card(c.A0);
        if (!card.Enabled)
        {
            c.V0 = McErrCardNotExist;
            return;
        }

        var dir = c.A2;
        var filesPtr = c.A3;
        var skip = (int)m.ReadU32(c.SP + 0x10u);
        var max = (int)m.ReadU32(c.SP + 0x14u);

        var hits = card.Match(Bios.Bios.ReadString(m, c.A1));
        if (filesPtr != 0u) m.WriteU32(filesPtr, (uint)hits.Count);

        var stored = 0;
        for (var i = skip; i < hits.Count && stored < max; i++, stored++)
            WriteDirEntry(m, dir + (uint)(stored * DirEntrySize), hits[i].name, hits[i].size);

        c.V0 = McErrNone;
    }

    private static void WriteDirEntry(IMemory m, uint ptr, string name, int size)
    {
        for (var i = 0; i < 20; i++) m.WriteU8(ptr + (uint)i, i < name.Length ? (byte)name[i] : (byte)0);
        m.WriteU32(ptr + 0x14u, 0x50u);
        m.WriteU32(ptr + 0x18u, (uint)size);
        m.WriteU32(ptr + 0x1Cu, 0u);
        m.WriteU32(ptr + 0x20u, 0u);
        m.WriteU32(ptr + 0x24u, 0u);
    }

    public static void MemCardSync(CpuContext c, IMemory m)
    {
        if (!_pending)
        {
            c.V0 = 0xFFFFFFFFu;
            return;
        }

        if (c.A1 != 0u) m.WriteU32(c.A1, _pendingCmd);
        if (c.A2 != 0u) m.WriteU32(c.A2, _pendingResult);

        uint cmd = _pendingCmd, result = _pendingResult;
        _pending = false;
        Fire(c, m, cmd, result);
        c.V0 = 1u;
    }

    private static void Fire(CpuContext c, IMemory m, uint cmd, uint result)
    {
        if (_callback == 0u) return;

        var snap = c.Snapshot();
        c.A0 = cmd;
        c.A1 = result;
        Dispatch.Dispatcher.Call(c, m, _callback);
        c.Restore(snap);
    }

    public static void Tick(CpuContext c, IMemory m)
    {
        if (!_pending || _callback == 0u) return;

        uint cmd = _pendingCmd, result = _pendingResult;
        _pending = false;
        Fire(c, m, cmd, result);
    }
}