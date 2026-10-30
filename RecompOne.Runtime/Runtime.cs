using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Hardware;
using RecompOne.Runtime.Host;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime;

public enum RunMode
{
    Retail,
    Devkit
}

public sealed class HardResetSignal : Exception;

public static class Runtime
{
    public static CpuContext? Cpu { get; private set; }
    public static IMemory? Mem { get; private set; }
    public static Gpu? Gpu;
    public static Spu? Spu;
    public static Mdec? Mdec;
    public static Hardware.Timers? Timers;
    public static Cdrom.CdController? Cd;

    public static RunMode Mode { get; private set; } = RunMode.Retail;

    public static void SetMode(RunMode mode)
    {
        Mode = mode;
        //devkit vs retail, devkits reads from sim and has more ram
        //todo: remove devkit mode
    }

    public static uint RamSize { get; internal set; } = MemoryMap.RetailRamSize;
    public static uint RamWordMask => (RamSize - 1) & ~3u;
    public static string CdPath => Config.Settings.System.DiscPath;

    public static Func<string, string?>? DiscValidator;

    public static string? ValidateDisc(string path)
    {
        try
        {
            return DiscValidator?.Invoke(path);
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public static Hardware.MemoryCard CardA = new("carda.sav") { Enabled = true };
    public static Hardware.MemoryCard CardB = new("cardb.sav") { Enabled = true };

    private static void LoadMemoryCards()
    {
        var system = Config.Settings.System;
        CardA = new MemoryCard(system.CardA.Path) { Enabled = system.CardA.Enabled };
        CardB = new MemoryCard(system.CardB.Path) { Enabled = system.CardB.Enabled };
    }

    public static readonly RamLogger RamLog = new();
    public static readonly Dispatch.OverlayEventLog OverlayLog = new();

    private static bool _hostReady;

    public static void Initialize(string title)
    {
        if (!_hostReady)
        {
            _hostReady = true;
            Diagnostics.ConsoleMirror.Install();
            Host.GpuJobs.Run(() => HostWindow.Initialize(title));
            Audio.Initialize();
        }

        LoadMemoryCards();
        Audio.SetMasterVolume(Config.Settings.System.Audio.EffectiveMaster);
        if (Event.HasAnyListeners<RuntimeReadyEvent>()) Event.Dispatch(new RuntimeReadyEvent());
    }

    public static void WaitForValidDisc()
    {
        HostWindow.WaitForValidDisc();
    }

    public static string Title
    {
        get => HostWindow.Title;
        set => HostWindow.Title = value;
    }

    public static void SetTitle(string title)
    {
        HostWindow.SetTitle(title);
    }

    public static void SetIcon(byte[] data)
    {
        HostWindow.SetIcon(data);
    }

    public static void SetIcon(byte[] rgba, int width, int height)
    {
        HostWindow.SetIcon(rgba, width, height);
    }

    public static void ClearIcon()
    {
        HostWindow.ClearIcon();
    }

    public static void ShowNotice(string message)
    {
        Host.Window.NoticePopup.Show(message);
    }

    public static void SetStartupNotice(string message, string title = "common.notice",
        string ackKey = "StartupNoticeAck")
    {
        Host.Window.StartupNoticePopup.Set(message, title, ackKey);
    }

    public static void AddLanguages(string json)
    {
        Host.Window.Localization.Merge(json);
    }

    public static bool AddLanguages(System.Reflection.Assembly assembly, string resourceName)
    {
        return Host.Window.Localization.MergeEmbedded(assembly, resourceName);
    }

    public static void SetContext(CpuContext c, IMemory m)
    {
        Cpu = c;
        Mem = m;
    }

    private static volatile bool _hardResetPending;

    public static bool HardResetPending => _hardResetPending;

    public static void HardReset()
    {
        _hardResetPending = true;
    }

    private static void ResetForBoot()
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
            backend.FillRect(0, 0, Gpu.VramWidth, Gpu.VramHeight, 0);
            backend.Flush();
        }
    }

    private static volatile bool _gameDone;
    private static readonly System.Diagnostics.Stopwatch _presentWatch = System.Diagnostics.Stopwatch.StartNew();
    private static double _nextPresentMs;
    
    public static void Run(Action boot)
    {
        Host.GpuJobs.Claim();
        
        var thread = new Thread(() => RunGame(boot))
        {
            IsBackground = true,
            Name = "game"
        };
        
        thread.Start();
        
        while (!_gameDone || HostWindow.Ready && (Host.Window.CrashPopup.IsPending ||
                                                   Host.Window.PopupManager.Get<Host.Window.CrashPopup>()?.IsOpen == true))
            PresentLoop();
    }
    
    private static void RunGame(Action boot)
    {
        while (true)
            try
            {
                boot();
                break;
            }
            catch (HardResetSignal)
            {
                Console.WriteLine("[Runtime] hard reset, game restarting");
                ResetForBoot();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"[Runtime] runtime has crashed: {e}");
                Host.GpuJobs.Run(() => Host.Window.CrashPopup.Report(e));
                break;
            }
        
        _gameDone = true;
    }
    
    private static double _idleMark;
    
    private static void IdleRedraw()
    {
        var now = FrameClock.Now;
        if (now - _idleMark < FrameClock.FrameMs) return;
        
        _idleMark = now;
        HostWindow.Compose(Gpu, false);
        Host.GpuJobs.Drain();
    }
    
    private static void PresentLoop()
    {
        Host.GpuJobs.Drain();
        
        if (!HostWindow.Ready)
        {
            Thread.Sleep(1);
            return;
        }
        
        HostWindow.PumpEvents();
        
        var interp = Interp.Interp.Backend;
        
        if (interp == null || !interp.Present(out var fresh))
        {
            IdleRedraw();
            Thread.Sleep(1);
            return;
        }
        
        _idleMark = 0.0;
        if (fresh) HostWindow.AdvanceFrame();
        var pace = interp.PaceMs;
        Pace(pace > 0.0 && _swapMs < pace * 0.5 ? pace : 0.0);
        var swap = _presentWatch.Elapsed.TotalMilliseconds;
        HostWindow.Compose(Gpu);
        Host.GpuJobs.Drain();
        swap = _presentWatch.Elapsed.TotalMilliseconds - swap;
        _swapMs = _swapMs > 0.0 ? _swapMs * 0.9 + swap * 0.1 : swap;
        interp.EndPresent();
    }
    
    private static double _swapMs;
    
    public static bool SwapBlocks => _swapMs > 0.0 && Interp.Interp.Backend is { PaceMs: > 0.0 } b && _swapMs >= b.PaceMs * 0.5;
    
    //the display frames only read as smooth if they land evenly in time, so the schedule is held against the clock instead of leaving it to the swap
    private static void Pace(double intervalMs)
    {
        if (Interrupts.Turbo) return;

        var now = _presentWatch.Elapsed.TotalMilliseconds;
        
        if (intervalMs <= 0.0 || _nextPresentMs <= 0.0 || now - _nextPresentMs > 250.0)
        {
            _nextPresentMs = now + intervalMs;
            return;
        }
        
        var wait = _nextPresentMs - now;
        
        if (wait > 1.5) Thread.Sleep((int)(wait - 1.0));
        while (_presentWatch.Elapsed.TotalMilliseconds < _nextPresentMs) Thread.SpinWait(32);
        
        _nextPresentMs += intervalMs;
    }
    
    public static void PresentFrame()
    {
        if (_hardResetPending)
        {
            _hardResetPending = false;
            throw new HardResetSignal();
        }

        Interp.Interp.Backend?.Publish();
        
        Audio.Attach(Spu);
        Hle.NativeGeometry.EndFrame();
        Hle.PrimIdentity.EndFrame();
        Gte.CloseFrame();
        FrameClock.MarkFrame();
        Sdk.LibCd.Tick();
        if (Cpu != null && Mem != null) Sdk.LibMcrd.Tick(Cpu, Mem);
        if (Mem != null)
        {
            Bios.BiosB.RefreshPad(Mem);
            Sdk.LibPad.Refresh(Mem);
        } //is this correct?
        
        // Interrupts.Raise(0);
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
