using GitExtensions.Extensibility;

namespace GitCommands;

/// <summary>
///  Provides helper methods for interacting with the OS shell (opening files, URLs, and File Explorer).
/// </summary>
public static class OsShellUtil
{
    private static IExecutable CreateExecutable(string command)
        => TestAccessor.MockExecutable ?? new Executable(command);

    /// <summary>
    ///  Open a file with its associated default application.
    /// </summary>
    /// <param name="filePath">Pathname of the file to open.</param>
    public static void Open(string filePath)
    {
        try
        {
            _ = CreateExecutable(filePath).Start(useShellExecute: true, throwOnErrorExit: false);
        }
        catch (Exception)
        {
            OpenAs(filePath);
        }
    }

    /// <summary>
    ///  Let the user chose an application to open a file.
    /// </summary>
    /// <param name="filePath">Pathname of the file to open.</param>
    public static void OpenAs(string filePath)
    {
        // filePath must not be quoted
        _ = CreateExecutable("rundll32.exe").Start("shell32.dll,OpenAs_RunDLL " + filePath, redirectOutput: true, outputEncoding: System.Text.Encoding.UTF8);
    }

    /// <summary>
    ///  Selects the specified file in Windows File Explorer.
    /// </summary>
    /// <param name="filePath">The full path of the file to select.</param>
    public static void SelectPathInFileExplorer(string filePath)
        => OpenWithFileExplorer($"/select, {filePath.Quote()}", quote: false);

    /// <summary>
    ///  Opens Windows File Explorer with the specified arguments.
    /// </summary>
    /// <param name="arguments">The arguments to pass to explorer.exe.</param>
    /// <param name="quote">Whether to quote the <paramref name="arguments"/>.</param>
    public static void OpenWithFileExplorer(string arguments, bool quote = true)
    {
        _ = CreateExecutable("explorer.exe").Start(quote ? arguments.Quote() : arguments);
    }

    /// <summary>
    ///  Opens the specified URL in the user's default web browser.
    /// </summary>
    /// <param name="url">The URL to open, or <see langword="null"/> to do nothing.</param>
    public static void OpenUrlInDefaultBrowser(string? url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            _ = CreateExecutable(url).Start(useShellExecute: true, throwOnErrorExit: false);
        }
    }

    internal struct TestAccessor
    {
        public static IExecutable? MockExecutable { get; set; }
    }
}
