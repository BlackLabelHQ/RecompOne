namespace RecompOne.Runtime.Assets.Decoders;

public static class PcmDecoderFactory
{
    public static bool
        IsSupported(
            string name) //in the future add other formats, for now .wav and .ogg are the easier to make a custom decoder wich will be needed
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext is ".wav" or ".wave" or ".ogg" or ".oga";
    }

    public static IPcmDecoder Open(string name, byte[] data)
    {
        var ms = new MemoryStream(data, false);
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".ogg" or ".oga" => new VorbisDecoder(ms),
            ".wav" or ".wave" => new WavDecoder(ms),
            _ => throw new NotSupportedException($"unsupported audio format '{ext}' ({name}); use .wav or .ogg!")
        };
    }
}