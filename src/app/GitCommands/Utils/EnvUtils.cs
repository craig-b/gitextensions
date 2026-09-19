using System.Diagnostics;

namespace GitCommands.Utils;

public static class EnvUtils
{
    /// <summary>
    /// Whether the process runs on Wine rather than a real Windows. Wine exports
    /// <c>wine_get_version</c> from its ntdll; Windows never does.
    /// </summary>
    public static bool RunningUnderWine { get; } = DetectWine();

    private static bool DetectWine()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            return System.Runtime.InteropServices.NativeLibrary.TryLoad("ntdll.dll", out nint ntdll)
                && System.Runtime.InteropServices.NativeLibrary.TryGetExport(ntdll, "wine_get_version", out _);
        }
        catch
        {
            return false;
        }
    }

    public static bool RunningOnWindowsWithMainWindow()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using Process currentProcess = Process.GetCurrentProcess();
        return currentProcess.MainWindowHandle != IntPtr.Zero;
    }

    public static string? ReplaceLinuxNewLinesDependingOnPlatform(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return s;
        }

        if (!OperatingSystem.IsWindows())
        {
            return s;
        }

        return s.Replace("\n", Environment.NewLine);
    }
}
