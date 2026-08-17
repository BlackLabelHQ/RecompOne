using System.Text;
using RecompOne.Runtime.Cdrom;

namespace RecompOne.Recompiler.Psx;

public sealed class SystemCfg
{
    public string BootExe = "PSX.EXE";
    public int Tcb = 4;
    public int Event = 16;
    public uint Stack = 0x801FFF00;

    public static SystemCfg Parse(DiscFs fs)
    {
        var cfg = new SystemCfg();
        var text = Encoding.ASCII.GetString(fs.ReadFile("SYSTEM.CNF"));

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Contains(';') ? raw[..raw.IndexOf(';')] : raw;
            var parts = line.Split('=', 2);
            if (parts.Length != 2) continue;

            var key = parts[0].Trim();
            var val = parts[1].Trim();

            switch (key)
            {
                case "BOOT":
                    cfg.BootExe = val
                        .Replace("cdrom:\\", "", StringComparison.OrdinalIgnoreCase)
                        .Replace("cdrom:/", "", StringComparison.OrdinalIgnoreCase)
                        .Replace("cdrom:", "", StringComparison.OrdinalIgnoreCase)
                        .Split(';')[0].Trim();
                    break;
                case "TCB": cfg.Tcb = int.Parse(val); break;
                case "EVENT": cfg.Event = int.Parse(val); break;
                case "STACK": cfg.Stack = Convert.ToUInt32(val, 16); break;
            }
        }

        return cfg;
    }
}
