using RecompOne.Runtime.Hle;

namespace RecompOne.Runtime;

//allow emiting custom ot for the renderer
public sealed partial class Gpu
{
    private HleVertex CustomVertex(in PrimVertex p)
    {
        return new HleVertex
        {
            X = p.X + _drawOffsetX,
            Y = p.Y + _drawOffsetY,
            R = p.R, G = p.G, B = p.B,
            U = p.U, V = p.V
        };
    }

    public void EmitPrim(int count, in PrimVertex a, in PrimVertex b, in PrimVertex c, in PrimVertex d,
        bool useImage, int image, bool semiTrans, bool raw, bool gouraud, int blend)
    {
        if (!HleOn || count < 3) return;

        var be = GpuHle.Backend!;
        be.SetDrawEnv(CurEnv());

        var flags = new PrimFlags
        {
            Textured = useImage,
            SemiTrans = semiTrans,
            RawTexture = raw,
            Gouraud = gouraud,
            TPage = (ushort)((blend & 3) << 5),
            Clut = 0,
            UseImage = useImage,
            Image = image
        };

        var v0 = CustomVertex(a);
        var v1 = CustomVertex(b);
        var v2 = CustomVertex(c);
        be.DrawTri(v0, v1, v2, flags);
        if (count >= 4) be.DrawTri(v1, v2, CustomVertex(d), flags);
    }

    public void EmitCustomOrder(int order)
    {
        GpuPrims.Emit(order, this);
    }
}