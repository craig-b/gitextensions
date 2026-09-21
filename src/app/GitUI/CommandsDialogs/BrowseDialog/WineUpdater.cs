using GitCommands;
using GitExtensions.Extensibility;

namespace GitUI.CommandsDialogs.BrowseDialog;

/// <summary>
/// Drives <c>eng/wine/update</c>, which applies a release once this process has gone.
/// </summary>
/// <remarks>
/// The install directory is found the way <see cref="Shells.LinuxShell"/> finds <c>open-terminal</c>:
/// beside the terminal relay, whose Windows path the launcher passes in. Both the helper and the
/// installer live there, and so does the VERSION file the installer writes, which is the only record
/// of which release is installed.
/// </remarks>
internal static class WineUpdater
{
    /// <summary>
    /// The install this process is running from, when it was installed from a release and that
    /// release is new enough to carry the update helper.
    /// </summary>
    public static bool TryLocate(out string updateProgram, out string installRoot)
    {
        updateProgram = "";
        installRoot = "";

        if (NativeGitBridge.TerminalRelay is not { } relay || Path.GetDirectoryName(relay) is not { } root)
        {
            return false;
        }

        string program = Path.Combine(root, "update");

        // install.sh is what the helper runs, and its absence is what tells a development overlay
        // apart from a release install.
        if (!File.Exists(program) || !File.Exists(Path.Combine(root, "install.sh")))
        {
            return false;
        }

        updateProgram = program;
        installRoot = root;
        return true;
    }

    /// <summary>The release tag recorded by the installer, or <see langword="null"/> if there is none.</summary>
    public static string? ReadInstalledTag(string installRoot)
    {
        try
        {
            string path = Path.Combine(installRoot, "VERSION");
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Starts the update and returns once it is detached, so the caller may exit.
    /// </summary>
    /// <remarks>
    /// Waiting matters. The helper's first stage returns only after the worker has been forked out of
    /// this process's reach and has recorded itself; until then the worker is still in the tree the
    /// bridge daemon kills when this process closes the connection.
    /// </remarks>
    public static async Task<(bool Started, string Message)> StartAsync(string updateProgram, string tag, string? repository)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        ArgumentString arguments = repository is null
            ? $"--tag {tag.Quote()}"
            : $"--tag {tag.Quote()} --repo {repository.Quote()}";

        try
        {
            // A path with no Windows extension is routed to the Linux side by Executable; the daemon
            // translates it and the repository argument out of their Z: form on the way.
            Executable executable = new(updateProgram, repository ?? "");
            using IProcess process = executable.Start(arguments, createWindow: false, redirectOutput: true, throwOnErrorExit: false);

            string output = await process.StandardOutput.ReadToEndAsync();
            int exitCode = await process.WaitForExitAsync();
            string error = process.StandardError;

            string message = Trim(output, error);
            return exitCode == 0
                ? (true, message)
                : (false, message.Length > 0 ? message : $"The update could not be started (exit code {exitCode}).");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }

        static string Trim(string output, string error)
        {
            string text = string.IsNullOrWhiteSpace(error) ? output : error;
            return text.Trim();
        }
    }
}
