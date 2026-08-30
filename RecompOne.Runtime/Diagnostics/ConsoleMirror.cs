using System.Text;

namespace RecompOne.Runtime.Diagnostics;

public static class ConsoleMirror
{
    private const int MaxLines = 4000;

    private static readonly object _gate = new();
    private static readonly List<string> _lines = new();
    private static readonly StringBuilder _pending = new();
    private static int _version;
    private static bool _installed;

    public static int Version
    {
        get
        {
            lock (_gate)
            {
                return _version;
            }
        }
    }

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        Console.SetOut(new Tee(Console.Out));
        Console.SetError(new Tee(Console.Error));
    }

    public static void Clear()
    {
        lock (_gate)
        {
            _lines.Clear();
            _pending.Clear();
            _version++;
        }
    }

    public static int SnapshotInto(List<string> dst)
    {
        lock (_gate)
        {
            dst.Clear();
            dst.AddRange(_lines);
            if (_pending.Length > 0) dst.Add(_pending.ToString());
            return _version;
        }
    }

    private static void Append(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        lock (_gate)
        {
            foreach (var c in text)
                if (c == '\n') FlushPendingLocked();
                else if (c != '\r') _pending.Append(c);
            _version++;
        }
    }

    private static void AppendChar(char c)
    {
        lock (_gate)
        {
            if (c == '\n') FlushPendingLocked();
            else if (c != '\r') _pending.Append(c);
            _version++;
        }
    }

    private static void FlushPendingLocked()
    {
        _lines.Add(_pending.ToString());
        _pending.Clear();
        if (_lines.Count > MaxLines) _lines.RemoveRange(0, _lines.Count - MaxLines);
    }

    private sealed class Tee : TextWriter
    {
        private readonly TextWriter _inner;

        public Tee(TextWriter inner)
        {
            _inner = inner;
        }

        public override Encoding Encoding => _inner.Encoding;

        public override void Write(char value)
        {
            _inner.Write(value);
            AppendChar(value);
        }

        public override void Write(string? value)
        {
            _inner.Write(value);
            Append(value);
        }

        public override void WriteLine(string? value)
        {
            _inner.WriteLine(value);
            Append(value);
            AppendChar('\n');
        }

        public override void Flush()
        {
            _inner.Flush();
        }
    }
}