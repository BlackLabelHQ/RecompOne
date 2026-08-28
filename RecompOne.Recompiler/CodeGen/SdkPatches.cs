using RecompOne.Recompiler.Analysis;

namespace RecompOne.Recompiler.CodeGen;


public static class SdkPatches
{
    static readonly (string Key, string Class, string[] Names)[] Libraries =
    {
        ("libcd", "RecompOne.Runtime.Sdk.LibCd", new[]
        {
            "CdInit", "CdReset", "CdControl", "CdControlF", "CdControlB",
            "CdSync", "CdReady", "CdRead", "CdReadSync", "CdGetSector",
            "CdDataSync", "CdSearchFile", "CdSyncCallback", "CdReadyCallback",
            "CdReadCallback", "CdDataCallback", "CdStatus", "CdMode",
            "CdLastCom", "CdMix",
        }),
        ("libetc", "RecompOne.Runtime.Sdk.LibEtc", new[]
        {
            "VSync",
        }),
        ("libgpu", "RecompOne.Runtime.Sdk.LibGpu", new[]
        {
            "DrawOTag", "DrawSync", "PutDrawEnv", "PutDispEnv",
            "LoadImage", "StoreImage", "MoveImage", "ClearImage",
        }),
        ("libcdstream", "RecompOne.Runtime.Sdk.LibCdStream", new[]
        {
            "StSetRing", "StClearRing", "StUnSetRing", "StSetStream",
            "StSetMask", "StGetNext", "StFreeRing", "StGetBackloc",
        }),
        ("libpad", "RecompOne.Runtime.Sdk.LibPad", new[]
        {
            "PadInitDirect", "PadStartCom", "PadStopCom", "PadEnableCom",
            "PadChkVsync", "PadChkMtap", "PadGetState", "PadInfoMode",
            "PadInfoAct", "PadInfoComb", "PadSetMainMode", "PadSetActAlign",
            "PadSetAct",
        }),

    };

    static readonly (string Key, string Target, string[] Names)[] Aliases =
    {
        ("libapi", "RecompOne.Runtime.Sdk.LibApi.PatchPad", new[]
        {
            "_patch_pad",
        }),
        ("libapi", "RecompOne.Runtime.Sdk.LibApi.PatchCard", new[]
        {
            "_patch_card",
        }),
        ("libapi", "RecompOne.Runtime.Sdk.LibApi.PatchCard2", new[]
        {
            "_patch_card2",
        }),
        ("libapi", "RecompOne.Runtime.Sdk.LibApi.PatchedBiosCall", new[]
        {
            "_patch_pad_call", "_patch_pad_call2", "_patch_card_call",
        }),
    };

    public static void Apply(List<MipsFunction> funcs, IEnumerable<string>? disabled = null)
    {
        var off = new HashSet<string>(disabled ?? [], StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, cls, names) in Libraries)
        {
            if (off.Contains(key)) continue;
            foreach (var name in names)
                map[name] = $"{cls}.{name}";
        }
        foreach (var (key, target, names) in Aliases)
        {
            if (off.Contains(key)) continue;
            foreach (var name in names)
                map[name] = target;
        }
        if (off.Count > 0)
            Console.WriteLine($"[Recompiler] hle impl disabled for: {string.Join(", ", off)}");

        int applied = 0;
        foreach (var func in funcs)
        {
            if (func.IsPatch || func.IsStub) continue;
            if (map.TryGetValue(func.Name, out var target))
            {
                func.IsPatch = true;
                func.PatchTarget = target;
                applied++;
            }
        }
        Console.WriteLine($"[Recompiler] it was applied {applied} reimplementations");
    }
}
