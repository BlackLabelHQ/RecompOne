namespace RecompOne.Runtime.Hle;

public interface IGlVram
{
    uint Texture { get; }
    uint Fbo { get; }

    void Init();
    void BindDraw();

    uint BeginDestRead(uint targetTex, int targetW, int targetH, int x, int y, int w, int h);

    void SampleBarrier();

    void Fill(int x, int y, int w, int h, ushort color15);
    void CopyRect(int sx, int sy, int dx, int dy, int w, int h);
    void WriteRect(int x, int y, int w, int h, ReadOnlySpan<ushort> px);
    void ReadRect(int x, int y, int w, int h, Span<ushort> dst);

    void Dispose();
}
