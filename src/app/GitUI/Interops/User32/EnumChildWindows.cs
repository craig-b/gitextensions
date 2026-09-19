using System.Runtime.InteropServices;
using static System.Interop;

namespace System;

internal static partial class NativeMethods
{
    public delegate BOOL EnumChildWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport(Libraries.User32, ExactSpelling = true)]
    public static extern BOOL EnumChildWindows(IntPtr hWndParent, EnumChildWindowsProc lpEnumFunc, IntPtr lParam);

    /// <summary>
    ///  Returns the first direct or indirect child window of <paramref name="hWndParent"/>, or <see cref="IntPtr.Zero"/>.
    /// </summary>
    public static IntPtr GetFirstChildWindow(IntPtr hWndParent)
    {
        IntPtr first = IntPtr.Zero;
        EnumChildWindowsProc callback = (hWnd, _) =>
        {
            first = hWnd;
            return BOOL.FALSE;
        };
        EnumChildWindows(hWndParent, callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return first;
    }
}
