using Silk.NET.OpenAL;
using ALDevice = Silk.NET.OpenAL.Device;
using ALCtx = Silk.NET.OpenAL.Context;

namespace RecompOne.Runtime.Host;

internal static unsafe class Audio
{
    private static ALContext? _alc;
    private static AL? _al;
    private static ALDevice* _device;
    private static ALCtx* _context;


    private const int NumBuffers = 8;
    private const int FramesPerBuffer = 256;

    private static uint _source;
    private static uint _previewSource;
    private static uint _previewBuffer;
    private static readonly object _previewGate = new();
    private static uint[] _buffers = new uint[NumBuffers];
    private static short[] _sampleBuf = new short[FramesPerBuffer * 2];

    private static Thread? _mixerThread;
    private static Spu? _spu;
    private static volatile bool _running;
    private static float _masterVolume = 1.0f;

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
            {
                _al.GenBuffers(NumBuffers, ptr);
            }

            //initial empty rihgt
            for (var i = 0; i < _buffers.Length; i++)
            {
                _al.BufferData(_buffers[i], BufferFormat.Stereo16, _sampleBuf, 44100);
                var b = _buffers[i];
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
        spu.VoiceGain = Config.Settings.System.Audio.Spu;
        spu.XaGain = Config.Settings.System.Audio.Xa;
    }

    public static void Detach()
    {
        _spu = null;
    }

    public static void Preview(short[] pcm, int channels, int rate)
    {
        if (_al == null || pcm.Length == 0) return;

        lock (_previewGate)
        {
            StopPreviewLocked();
            if (_previewSource == 0) _previewSource = _al.GenSource();
            _previewBuffer = _al.GenBuffer();
            _al.BufferData(_previewBuffer, channels >= 2 ? BufferFormat.Stereo16 : BufferFormat.Mono16, pcm, rate);
            _al.SetSourceProperty(_previewSource, SourceFloat.Gain, _masterVolume);
            _al.SetSourceProperty(_previewSource, SourceInteger.Buffer, (int)_previewBuffer);
            _al.SourcePlay(_previewSource);
        }
    }

    public static void StopPreview()
    {
        if (_al == null) return;
        lock (_previewGate)
        {
            StopPreviewLocked();
        }
    }

    public static bool PreviewPlaying
    {
        get
        {
            if (_al == null || _previewSource == 0) return false;
            lock (_previewGate)
            {
                _al.GetSourceProperty(_previewSource, GetSourceInteger.SourceState, out var state);
                return state == (int)SourceState.Playing;
            }
        }
    }

    private static void StopPreviewLocked()
    {
        if (_previewSource == 0) return;
        _al!.SourceStop(_previewSource);
        _al.SetSourceProperty(_previewSource, SourceInteger.Buffer, 0);
        if (_previewBuffer != 0) _al.DeleteBuffer(_previewBuffer);
        _previewBuffer = 0;
    }

    public static void SetMasterVolume(float volume)
    {
        _masterVolume = Math.Clamp(volume, 0f, 1f);
        if (_al != null && _source != 0)
            _al.SetSourceProperty(_source, SourceFloat.Gain, _masterVolume);
    }

    private static readonly int BufferMs = Math.Max(1, FramesPerBuffer * 1000 / 44100);

    private static void MixerLoop()
    {
        while (_running)
        {
            var spu = _spu;
            if (spu != null) FillBuffers(spu);
            Thread.Sleep(spu != null ? BufferMs : 20);
        }
    }

    private static void FillBuffers(Spu spu)
    {
        _al!.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out var processed);
        while (processed > 0)
        {
            uint buf = 0;
            _al.SourceUnqueueBuffers(_source, 1, &buf);

            spu.Mix(_sampleBuf, FramesPerBuffer);

            _al.BufferData(buf, BufferFormat.Stereo16, _sampleBuf, 44100);
            _al.SourceQueueBuffers(_source, 1, &buf);
            processed--;
        }

        _al.GetSourceProperty(_source, GetSourceInteger.SourceState, out var state);
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
            StopPreview();
            if (_previewSource != 0) _al.DeleteSource(_previewSource);
            _al.SourceStop(_source);
            _al.DeleteSource(_source);
            _al.DeleteBuffers(_buffers);
        }

        if (_context != null) _alc.DestroyContext(_context);
        if (_device != null) _alc.CloseDevice(_device);
    }
}