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
        if (spu == null) return;
        _spu = spu;
        spu.VoiceGain = Config.ConfigManager.Game.SpuVolume;
        spu.XaGain = Config.ConfigManager.Game.XaVolume;
    }

    public static void Detach() => _spu = null;

    public static void SetMasterVolume(float volume)
    {
        _masterVolume = Math.Clamp(volume, 0f, 1f);
        if (_al != null && _source != 0)
            _al.SetSourceProperty(_source, SourceFloat.Gain, _masterVolume);
    }
    
    static readonly int BufferMs = Math.Max(1, FramesPerBuffer * 1000 / 44100);

    static void MixerLoop()
    {
        while (_running)
        {
            var spu = _spu;
            if (spu != null) FillBuffers(spu);
            Thread.Sleep(spu != null ? BufferMs : 20);
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
