using System.Numerics;
using System.Runtime.InteropServices;

namespace RecompOne.Runtime.Host.Window;

internal static unsafe class DockBuilder
{
    private const string Lib = "cimgui";

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void igDockBuilderRemoveNode(uint nodeId);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void igDockBuilderAddNode(uint nodeId, int flags);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void igDockBuilderSetNodeSize(uint nodeId, Vector2 size);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void igDockBuilderDockWindow(byte* windowName, uint nodeId);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void igDockBuilderFinish(uint nodeId);

    public static void SetupCenterLayout(uint dockId, Vector2 size, string windowName)
    {
        igDockBuilderRemoveNode(dockId);
        igDockBuilderAddNode(dockId, 0);
        igDockBuilderSetNodeSize(dockId, size);

        var bytes = System.Text.Encoding.UTF8.GetBytes(windowName + "\0");
        fixed (byte* p = bytes)
        {
            igDockBuilderDockWindow(p, dockId);
        }

        igDockBuilderFinish(dockId);
    }
}