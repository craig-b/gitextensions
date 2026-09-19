using System.Runtime.InteropServices;
using static System.Interop;

namespace System;

internal static partial class NativeMethods
{
    [Flags]
    public enum RDW : uint
    {
        INVALIDATE = 0x0001,
        ERASE = 0x0004,
        ALLCHILDREN = 0x0080,
        UPDATENOW = 0x0100,
    }

    [DllImport(Libraries.User32, ExactSpelling = true)]
    public static extern BOOL RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, RDW flags);
}
