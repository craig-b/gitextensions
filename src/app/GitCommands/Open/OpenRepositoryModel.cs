namespace GitCommands.Open;

/// <summary>
///  Decision logic for the "open repository" flow: which directories to offer, whether a typed
///  path can actually be opened, and parent-directory navigation. The host owns the dialog,
///  the module construction and the history write; this class only decides.
/// </summary>
public static class OpenRepositoryModel
{
    /// <summary>
    ///  The directories offered in the open-repository dropdown, most relevant first:
    ///  the default clone destination, the parent of the current repository, then the recent
    ///  history. Only when all of those are absent: the last-used working directory and the
    ///  user's home directory.
    /// </summary>
    public static IReadOnlyList<string> CandidateDirectories(
        string? defaultCloneDestinationPath,
        string? currentWorkingDir,
        IEnumerable<string> historyPaths,
        string? recentWorkingDir,
        string? homeDir)
    {
        List<string> directories = [];

        if (!string.IsNullOrWhiteSpace(defaultCloneDestinationPath))
        {
            directories.Add(defaultCloneDestinationPath.EnsureTrailingPathSeparator());
        }

        if (!string.IsNullOrWhiteSpace(currentWorkingDir))
        {
            DirectoryInfo di = new(currentWorkingDir);
            if (di.Parent is not null)
            {
                directories.Add(di.Parent.FullName.EnsureTrailingPathSeparator());
            }
        }

        directories.AddRange(historyPaths);

        if (directories.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(recentWorkingDir))
            {
                directories.Add(recentWorkingDir.EnsureTrailingPathSeparator());
            }

            if (!string.IsNullOrWhiteSpace(homeDir))
            {
                directories.Add(homeDir.EnsureTrailingPathSeparator());
            }
        }

        return directories.Distinct().ToList();
    }

    /// <summary>
    ///  The gate for opening a typed path: the directory must exist and, with a trailing
    ///  separator, be a valid git working directory.
    /// </summary>
    /// <returns>The normalized working directory to open, or <see langword="null"/>.</returns>
    public static string? TryGetOpenablePath(string? path, Func<string, bool> directoryExists, Func<string, bool> isValidGitWorkingDir)
    {
        path = path?.Trim();
        if (string.IsNullOrWhiteSpace(path) || !directoryExists(path))
        {
            return null;
        }

        string normalized = path.EnsureTrailingPathSeparator();
        return isValidGitWorkingDir(normalized) ? normalized : null;
    }

    /// <summary>
    ///  The parent directory for "go up" navigation, without a trailing separator, or
    ///  <see langword="null"/> at a root or for an invalid path.
    /// </summary>
    public static string? ParentOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return new DirectoryInfo(path).Parent?.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>"Go up" is available when the typed directory exists and has a parent.</summary>
    public static bool CanGoUp(string? path, Func<string, bool> directoryExists)
        => !string.IsNullOrWhiteSpace(path) && directoryExists(path) && ParentOf(path) is not null;
}
