namespace RecompOne.Runtime.Hle;

/// <summary>
/// GPU BACKEND
/// </summary>
public interface IGpuBackend
{
    // false in headless or if gl init failed
    bool Ready { get; }

    // submit
    void SetDrawEnv(in HleDrawEnv env);
    void DrawTri(in HleVertex a, in HleVertex b, in HleVertex c, in PrimFlags f);
    void DrawRect(in HleRect r, in PrimFlags f);
    void DrawLine(in HleVertex a, in HleVertex b, in PrimFlags f);
    void FillRect(int x, int y, int w, int h, ushort color15);
    void CopyVram(int sx, int sy, int dx, int dy, int w, int h);
    void WriteVram(int x, int y, int w, int h, ReadOnlySpan<ushort> px);
    void ReadVram(int x, int y, int w, int h, Span<ushort> px);

    //add other stuff
    int RegisterImage(ReadOnlySpan<byte> rgba, int width, int height);

    // these touch gl
    void Flush();
    void Present(in HleDispEnv disp);
}