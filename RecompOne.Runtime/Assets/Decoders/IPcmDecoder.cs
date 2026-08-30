namespace RecompOne.Runtime.Assets.Decoders;

//base of the audio decoder, so you can get the audio at any point, should be enough for vorbis and wav
public interface IPcmDecoder : IDisposable
{
    int SampleRate { get; }
    int Channels { get; }
    long TotalFrames { get; }

    int ReadFrames(short[] dst, int frames);
    void SeekFrames(long frame);
}