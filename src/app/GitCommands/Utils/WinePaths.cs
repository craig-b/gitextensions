using System.Collections.Concurrent;
using System.Diagnostics;

namespace GitCommands.Utils;

/// <summary>
///  Translates Windows paths to the host's form when the app runs under Wine.
/// </summary>
public static class WinePaths
{
    private static readonly ConcurrentDictionary<char, string?> _driveRoots = new();

    /// <summary>
    ///  Returns the host (Linux) path for a Windows path: forward slashes, and the drive letter replaced by the
    ///  directory its dosdevices link points to, as winepath reports it. A path without a resolvable drive comes
    ///  back with forward slashes only.
    /// </summary>
    public static string ToHostPath(string path)
    {
        string posix = path.ToPosixPath();
        if (posix.Length >= 2 && posix[1] == ':' && char.IsAsciiLetter(posix[0]))
        {
            string? root = _driveRoots.GetOrAdd(char.ToLowerInvariant(posix[0]), ResolveDriveRoot);
            if (root is not null)
            {
                string host = root + posix[2..];
                return host.Length == 0 ? "/" : host;
            }
        }

        return posix;
    }

    /// <summary>
    ///  Asks winepath for the Unix directory behind a drive letter, once per drive and process.
    /// </summary>
    private static string? ResolveDriveRoot(char drive)
    {
        try
        {
            using Process process = Process.Start(new ProcessStartInfo("winepath.exe", $"-u {drive}:\\")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            })!;
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 && output.StartsWith('/') ? output.TrimEnd('/') : null;
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
            return null;
        }
    }
}
