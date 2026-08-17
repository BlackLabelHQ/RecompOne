using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Host;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime;

public enum RunMode { Retail, Devkit }

public sealed class HardResetSignal : Exception;

public static class Runtime
{
    public static CpuContext? Cpu { get; private set; }
    public static IMemory? Mem { get; private set; }
    public static Gpu? Gpu;
    public static Spu? Spu;
    public static Cdrom.CdController? Cd;

    public static RunMode Mode { get; private set; } = RunMode.Retail;
    public static void SetMode(RunMode mode) => Mode = mode; //devkit vs retail, devkits reads from sim and has more ram

    public static uint RamSize { get; internal set; } = MemoryMap.RetailRamSize;
    public static uint RamWordMask => (RamSize - 1) & ~3u;
    public static string CdPath => Config.ConfigManager.Game.CdPath;

    public static Func<string, string?>? DiscValidator;
    public static string? ValidateDisc(string path)
    {
        try { return DiscValidator?.Invoke(path); }
        catch (Exception e) { return e.Message; }
    }
    
    public static Config.ViewConfig View => Config.ConfigManager.View;
    public static void SaveView() => Config.ConfigManager.SaveView(Host.Window.PanelManager.Panels);
    
    public static Hardware.MemoryCard CardA = new("carda.sav") { Enabled = true };
    public static Hardware.MemoryCard CardB = new("cardb.sav") { Enabled = true };

    static void LoadMemoryCards()
    {
        var g = Config.ConfigManager.Game;
        CardA = new(Fallback(g.CardAPath, "carda.sav")) { Enabled = g.CardAEnabled };
        CardB = new(Fallback(g.CardBPath, "cardb.sav")) { Enabled = g.CardBEnabled };

        static string Fallback(string path, string def) => string.IsNullOrWhiteSpace(path) ? def : path;
    }
    public static readonly Memory.RamLogger RamLog = new();
    public static readonly Dispatch.OverlayEventLog OverlayLog = new();

    static bool _hostReady;

    public static void Initialize(string title)
    {
        if (!_hostReady)
        {
            _hostReady = true;
            Diagnostics.ConsoleMirror.Install();
            HostWindow.Initialize(title);
            Audio.Initialize();
        }

        LoadMemoryCards();
        Audio.SetMasterVolume(Config.ConfigManager.Game.Muted ? 0f : Config.ConfigManager.Game.MasterVolume);
        if (Event.HasAnyListeners<RuntimeReadyEvent>())
        {
            Event.Dispatch(new RuntimeReadyEvent());
        }
    }

    public static void WaitForValidDisc() => HostWindow.WaitForValidDisc();

    public static string Title
    {
        get => HostWindow.Title;
        set => HostWindow.Title = value;
    }

    public static void SetTitle(string title) => HostWindow.SetTitle(title);

    public static void SetIcon(byte[] data) => HostWindow.SetIcon(data);

    public static void SetIcon(byte[] rgba, int width, int height) => HostWindow.SetIcon(rgba, width, height);

    public static void ClearIcon() => HostWindow.ClearIcon();
    
    public static void ShowNotice(string message) => Host.Window.NoticePopup.Show(message);
    public static void SetStartupNotice(string message, string title = "common.notice", string ackKey = "StartupNoticeAck") => Host.Window.StartupNoticePopup.Set(message, title, ackKey);

    public static void AddLanguages(string json) => Host.Window.Localization.Merge(json);
    public static bool AddLanguages(System.Reflection.Assembly assembly, string resourceName)
        => Host.Window.Localization.MergeEmbedded(assembly, resourceName);

    public static void SetContext(CpuContext c, IMemory m)
    {
        Cpu = c;
        Mem = m;
    }

    static volatile bool _hardResetPending;

    public static bool HardResetPending => _hardResetPending;

    public static void HardReset() => _hardResetPending = true;

    public static void Run(Action boot)
    {
        while (true)
        {
            try
            {
                boot();
                return;
            }
            catch (HardResetSignal)
            {
                Console.WriteLine("[Runtime] hard reset,game booting again");
                ResetForBoot();
            }
        }
    }

    static void ResetForBoot()
    {
        Audio.Detach();

        Sdk.LibCd.Reset();
        Sdk.LibCdStream.Reset();
        Assets.Xa.XaRouter.Reset();
        Sdk.LibPad.Reset();
        Dispatch.Dispatcher.Reset();
        Bios.BiosB.Reset();
        OverlayLog.Clear();

        Cpu = null;
        Mem = null;
        Gpu = null;
        Spu = null;
        Cd = null;

        if (Hle.GpuHle.Backend is { Ready: true } backend)
        {
            backend.FillRect(0, 0, global::RecompOne.Runtime.Gpu.VramWidth, global::RecompOne.Runtime.Gpu.VramHeight, 0);
            backend.Flush();
        }
    }

    public static void PresentFrame()
    {
        if (_hardResetPending)
        {
            _hardResetPending = false;
            throw new HardResetSignal();
        }

        HostWindow.Present(Gpu);
        Audio.Attach(Spu);
        FrameClock.Throttle();
        Sdk.LibCd.Tick();
        if (Mem != null) { Bios.BiosB.RefreshPad(Mem); Sdk.LibPad.Refresh(Mem); } //is this correct?
        DispatchIrq(0); //using this to dispatch irqs too if necessary, probably not needed after the rest of stuff is reimplemented
    }

    public static void DispatchIrq(int irq)
    {
        if (Cpu != null && Mem != null)
            Interrupts.Deliver(irq, Cpu, Mem);
    }

    public static void Shutdown()
    {
        Audio.Shutdown();
        HostWindow.Shutdown();
    }
}
