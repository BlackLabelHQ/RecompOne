using Silk.NET.OpenAL;
using ALDevice = Silk.NET.OpenAL.Device;
using ALCtx = Silk.NET.OpenAL.Context;

namespace RecompOne.Runtime.Host;

internal static unsafe class Audio
{
    static ALContext? _alc;
    static AL? _al;
    static ALDevice* _device;
    static ALCtx* _context;


    const int NumBuffers = 8;
    const int FramesPerBuffer = 256;

    static uint _source;
    static uint[] _buffers = new uint[NumBuffers];
    static short[] _sampleBuf = new short[FramesPerBuffer * 2];

    static Thread? _mixerThread;
    static Spu? _spu;
    static volatile bool _running;
    static float _masterVolume = 1.0f;

    const int AlcConnected = 0x313;
    static int _reconnectTick;
    static volatile bool _audioDeviceChanged;
    static bool _disconnectExtPresent;
    static delegate* unmanaged[Cdecl]<ALDevice*, byte*, int*, byte> _alcReopenDeviceSoft;

    internal static void NotifyAudioDeviceChanged() => _audioDeviceChanged = true;

    public static void Initialize()
    {
        try
        {
            _alc = ALContext.GetApi(true);
            _al = AL.GetApi(true);
            _device = _alc.OpenDevice("");
            if (_device == null)
            {
                Console.Error.WriteLine("[Host] no audio device, audio disabled");
                return;
            }
            _context = _alc.CreateContext(_device, null);
            _alc.MakeContextCurrent(_context);

            _source = _al.GenSource();
            _al.SetSourceProperty(_source, SourceFloat.Gain, _masterVolume);
            fixed (uint* ptr = _buffers)
                _al.GenBuffers(NumBuffers, ptr);

            //initial empty rihgt
            for (int i = 0; i < _buffers.Length; i++)
            {
                _al.BufferData(_buffers[i], BufferFormat.Stereo16, _sampleBuf, 44100);
                uint b = _buffers[i];
                _al.SourceQueueBuffers(_source, 1, &b);
            }

            _al.SourcePlay(_source);

            TryLoadReopenExtension();

            _audioDeviceChanged = false;
            _running = true;
            _mixerThread = new Thread(MixerLoop) { IsBackground = true, Name = "spu-mixer" };
            _mixerThread.Start();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[Host] audio init failed: {e.Message}");
        }
    }

    public static void Attach(Spu? spu)
    {
        if (spu != null) _spu = spu;
    }

    public static void SetMasterVolume(float volume)
    {
        _masterVolume = Math.Clamp(volume, 0f, 1f);
        if (_al != null && _source != 0)
            _al.SetSourceProperty(_source, SourceFloat.Gain, _masterVolume);
    }

    static void MixerLoop()
    {
        while (_running)
        {
            var spu = _spu;
            if (spu != null) FillBuffers(spu);

            if (++_reconnectTick >= 333) // ~1s at 3ms sleep
            {
                _reconnectTick = 0;
                if (!IsDeviceConnected() || _audioDeviceChanged)
                {
                    _audioDeviceChanged = false;
                    if (_alcReopenDeviceSoft != null && _device != null)
                        _alcReopenDeviceSoft(_device, (byte*)null, (int*)null);
                    else
                        ReopenDevice();
                }
            }

            Thread.Sleep(3);
        }
    }

    static void TryLoadReopenExtension()
    {
        if (_alc == null || _device == null) return;
        _disconnectExtPresent = _alc.IsExtensionPresent(_device, "ALC_EXT_disconnect");
        if (!_alc.IsExtensionPresent(_device, "ALC_SOFT_reopen_device")) return;
        if (_alc.Context.TryGetProcAddress("alcReopenDeviceSOFT", out nint ptr) && ptr != 0)
            _alcReopenDeviceSoft = (delegate* unmanaged[Cdecl]<ALDevice*, byte*, int*, byte>)(void*)ptr;
    }

    static bool IsDeviceConnected()
    {
        if (_alc == null || _device == null || !_disconnectExtPresent) return true;
        int connected = 0;
        _alc.GetContextProperty(_device, (GetContextInteger)AlcConnected, 1, &connected);
        return connected != 0;
    }

    static void ReopenDevice()
    {
        if (_al == null || _alc == null) return;
        try
        {
            _al.SourceStop(_source);
            _al.GetSourceProperty(_source, GetSourceInteger.BuffersQueued, out int queued);
            for (int i = 0; i < queued; i++)
            {
                uint buf = 0;
                _al.SourceUnqueueBuffers(_source, 1, &buf);
            }
            _al.DeleteSource(_source);
            _source = 0;
            fixed (uint* ptr = _buffers)
                _al.DeleteBuffers(NumBuffers, ptr);
            Array.Clear(_buffers);

            if (_context != null) { _alc.DestroyContext(_context); _context = null; }
            if (_device != null) { _alc.CloseDevice(_device); _device = null; }

            _device = _alc.OpenDevice("");
            if (_device == null) return;
            _context = _alc.CreateContext(_device, null);
            _alc.MakeContextCurrent(_context);

            _source = _al.GenSource();
            _al.SetSourceProperty(_source, SourceFloat.Gain, _masterVolume);
            fixed (uint* ptr = _buffers)
                _al.GenBuffers(NumBuffers, ptr);

            Array.Clear(_sampleBuf);
            for (int i = 0; i < NumBuffers; i++)
            {
                _al.BufferData(_buffers[i], BufferFormat.Stereo16, _sampleBuf, 44100);
                uint b = _buffers[i];
                _al.SourceQueueBuffers(_source, 1, &b);
            }
            _al.SourcePlay(_source);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[Host] audio reopen failed: {e.Message}");
        }
    }

    static void FillBuffers(Spu spu)
    {
        _al!.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out int processed);
        while (processed > 0)
        {
            uint buf = 0;
            _al.SourceUnqueueBuffers(_source, 1, &buf);

            spu.Mix(_sampleBuf, FramesPerBuffer);

            _al.BufferData(buf, BufferFormat.Stereo16, _sampleBuf, 44100);
            _al.SourceQueueBuffers(_source, 1, &buf);
            processed--;
        }

        _al.GetSourceProperty(_source, GetSourceInteger.SourceState, out int state);
        if (state != (int)SourceState.Playing)
            _al.SourcePlay(_source);
    }

    public static void Shutdown()
    {
        if (_alc == null) return;
        _running = false;
        _mixerThread?.Join();
        if (_al != null)
        {
            _al.SourceStop(_source);
            _al.DeleteSource(_source);
            _al.DeleteBuffers(_buffers);
        }
        if (_context != null) _alc.DestroyContext(_context);
        if (_device != null) _alc.CloseDevice(_device);
    }
}
