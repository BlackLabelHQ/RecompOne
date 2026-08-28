using RecompOne.Runtime.Hardware;

namespace RecompOne.Runtime.Host;

//todo: by frame or by time, frame times are weird rn, later move frame
public static class InputRecorder
{
    public enum Mode { Off, Recording, Playing }

    public const string Directory = "recordings";
    public const string Extension = ".rec";

    const uint Magic = 0x4E494F52;
    const int HeaderBytes = 8;
    const int FrameBytes = 17;

    public static Mode State { get; private set; }
    public static string Name { get; private set; } = "";
    public static int Frame { get; private set; }
    public static int Length { get; private set; }

    static readonly List<byte> _record = [];
    static byte[] _playback = [];
    static readonly System.Diagnostics.Stopwatch _clock = new();
    static bool _exitHooked;

    public static bool Active => State != Mode.Off;

    public static string DirectoryPath => Path.GetFullPath(Directory);

    public static List<string> Available()
    {
        var names = new List<string>();
        if (!System.IO.Directory.Exists(Directory)) return names;

        var files = new List<(string Name, DateTime Time)>();
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*" + Extension))
            files.Add((Path.GetFileNameWithoutExtension(path), File.GetLastWriteTimeUtc(path)));

        files.Sort((a, b) => b.Time.CompareTo(a.Time));
        foreach (var f in files) names.Add(f.Name);
        return names;
    }

    public static void StartRecording(string? name = null)
    {
        Stop();
        Name = string.IsNullOrWhiteSpace(name) ? DateTime.Now.ToString("yyyyMMdd-HHmmss") : name!; //date-hour is better
        _record.Clear();
        Frame = 0;
        Length = 0;
        State = Mode.Recording;
        _clock.Restart();
        HookExit();
        Console.WriteLine($"[input] Recording to {PathOf(Name)}");
    }

    public static bool StartPlayback(string nameOrPath)
    {
        string path = ResolvePath(nameOrPath);
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"[input] not recording at {path}");
            return false;
        }

        byte[] data;
        try { data = File.ReadAllBytes(path); }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[input] couldnt read {path}: {e.Message}");
            return false;
        }

        if (data.Length < HeaderBytes || BitConverter.ToUInt32(data, 0) != Magic ||
            (data.Length - HeaderBytes) % FrameBytes != 0)
        {
            Console.Error.WriteLine($"[input] {path} is invalid");
            return false;
        }

        Stop();
        _playback = data;
        Length = BitConverter.ToInt32(data, 4);
        if (Length > (data.Length - HeaderBytes) / FrameBytes)
            Length = (data.Length - HeaderBytes) / FrameBytes;
        Frame = 0;
        Name = Path.GetFileNameWithoutExtension(path);
        State = Mode.Playing;
        _clock.Restart();
        Console.WriteLine($"[input] playing {Name} ({Length} frames)");
        return true;
    }

    public static void Stop()
    {
        if (State == Mode.Recording) Save();
        else if (State == Mode.Playing) //Console.WriteLine($"[input] playback of {Name} stopped at frame {Frame}");

        State = Mode.Off;
        _playback = [];
        _record.Clear();
        Frame = 0;
        Length = 0;
    }

    public static void Tick()
    {
        switch (State)
        {
            case Mode.Recording:
                Capture();
                Frame++;
                Length = Frame;
                if ((Frame & 0xFF) == 0) Save();
                break;

            case Mode.Playing:
            {
                uint now = (uint)_clock.Elapsed.TotalMilliseconds;
                while (Frame + 1 < Length && StampAt(Frame + 1) <= now) Frame++;
                if (Frame >= Length) { Stop(); return; }
                Apply(HeaderBytes + Frame * FrameBytes);
                if (Frame + 1 >= Length && StampAt(Frame) + 200u < now) { Stop(); return; }
                break;
            }
        }
    }

    public static bool TryGetFrame(int index, out ushort buttons, out ushort buttons2)
    {
        buttons = buttons2 = 0xFFFF;
        if (index < 0 || index >= Length) return false;

        switch (State)
        {
            case Mode.Recording:
            {
                int o = index * FrameBytes + 4;
                if (o + 4 > _record.Count) return false;
                buttons = (ushort)(_record[o] | (_record[o + 1] << 8));
                buttons2 = (ushort)(_record[o + 2] | (_record[o + 3] << 8));
                return true;
            }
            case Mode.Playing:
            {
                int o = HeaderBytes + index * FrameBytes + 4;
                if (o + 4 > _playback.Length) return false;
                buttons = (ushort)(_playback[o] | (_playback[o + 1] << 8));
                buttons2 = (ushort)(_playback[o + 2] | (_playback[o + 3] << 8));
                return true;
            }
        }
        return false;
    }

    public static bool TryGetAxes(int index, bool pad2, out byte lx, out byte ly, out byte rx, out byte ry)
    {
        lx = ly = rx = ry = 0x80;
        if (index < 0 || index >= Length) return false;

        int b = pad2 ? 8 : 4;
        switch (State)
        {
            case Mode.Recording:
            {
                int o = index * FrameBytes + 4 + b;
                if (o + 4 > _record.Count) return false;
                lx = _record[o]; ly = _record[o + 1]; rx = _record[o + 2]; ry = _record[o + 3];
                return true;
            }
            case Mode.Playing:
            {
                int o = HeaderBytes + index * FrameBytes + 4 + b;
                if (o + 4 > _playback.Length) return false;
                lx = _playback[o]; ly = _playback[o + 1]; rx = _playback[o + 2];
                ry = _playback[o + 3];
                return true;
            }
        }
        return false;
    }

    static void HookExit()
    {
        if (_exitHooked) return;
        _exitHooked = true;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { if (State == Mode.Recording) Save(); };
        Console.CancelKeyPress += (_, _) => { if (State == Mode.Recording) Save(); };
    }

    static void Capture()
    {
        uint ms = (uint)_clock.Elapsed.TotalMilliseconds;
        _record.Add((byte)ms);
        _record.Add((byte)(ms >> 8));
        _record.Add((byte)(ms >> 16));
        _record.Add((byte)(ms >> 24));
        Add16(Controller.State);
        Add16(Controller.State2);
        _record.Add(Controller.LeftX);
        _record.Add(Controller.LeftY);
        _record.Add(Controller.RightX);
        _record.Add(Controller.RightY);
        _record.Add(Controller.LeftX2);
        _record.Add(Controller.LeftY2);
        _record.Add(Controller.RightX2);
        _record.Add(Controller.RightY2);
        _record.Add((byte)(Controller.Connected2 ? 1 : 0));
    }

    static void Add16(ushort v)
    {
        _record.Add((byte)v);
        _record.Add((byte)(v >> 8));
    }

    static uint StampAt(int index)
    {
        int o = HeaderBytes + index * FrameBytes;
        if (o + 4 > _playback.Length) return uint.MaxValue;
        return (uint)(_playback[o] | (_playback[o + 1] << 8) | (_playback[o + 2] << 16) | (_playback[o + 3] << 24));
    }

    static void Apply(int o)
    {
        var d = _playback;
        o += 4;
        Controller.State = (ushort)(d[o] | (d[o + 1] << 8));
        Controller.State2 = (ushort)(d[o + 2] | (d[o + 3] << 8));
        Controller.LeftX = d[o + 4];
        Controller.LeftY = d[o + 5];
        Controller.RightX = d[o + 6];
        Controller.RightY = d[o + 7];
        Controller.LeftX2 = d[o + 8];
        Controller.LeftY2 = d[o + 9];
        Controller.RightX2 = d[o + 10];

        Controller.RightY2 = d[o + 11];
        Controller.Connected2 = d[o + 12] != 0;
    }

    static void Save()
    {
        if (_record.Count == 0)
        {
            Console.WriteLine("[input] nothing recorded, no file written");
            return;
        }

        string path = PathOf(Name);
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            using var f = File.Create(path);
            f.Write(BitConverter.GetBytes(Magic));
            f.Write(BitConverter.GetBytes(_record.Count / FrameBytes));
            f.Write(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_record));
            Console.WriteLine($"[input] wrote {_record.Count / FrameBytes} frames to {path}");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[input] could not write {path}: {e.Message}");
        }
    }

    static string PathOf(string name) => Path.Combine(Directory, name + Extension);

    static string ResolvePath(string nameOrPath)
    {
        if (nameOrPath.Contains(Path.DirectorySeparatorChar) || File.Exists(nameOrPath))
            return Path.GetFullPath(nameOrPath);
        if (!nameOrPath.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            nameOrPath += Extension;
        return Path.GetFullPath(Path.Combine(Directory, nameOrPath));
    }

    public static void ParseCommandLine(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--record":
                    StartRecording(i + 1 < args.Length && !args[i + 1].StartsWith('-') ? args[++i] : null);
                    break;
                case "--play":
                    if (i + 1 < args.Length) StartPlayback(args[++i]);
                    else Console.Error.WriteLine("[input] --play needs the name of a recording");
                    break;
            }
        }
    }
}
