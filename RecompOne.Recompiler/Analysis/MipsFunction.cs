using RecompOne.Recompiler.Variants;
using RecompOne.Recompiler.Disasm;

namespace RecompOne.Recompiler.Analysis;

public sealed class MipsFunction
{
    public string Name = "";
    public string OverlayName = "";
    public uint Start;
    public uint End;
    public MipsInstruction[] Instructions = [];
    public string EmittedName = "";
    public bool IsStub;
    public bool IsPatch;
    public bool Precise;
    public string PatchTarget = "";
    public List<string> PreHookTargets = [];
    public List<string> LatePreHookTargets = [];
    public FunctionVariant? Variant;
    public List<string> PostHookTargets = [];
    public List<JumpTable> JumpTables = [];
}