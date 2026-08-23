namespace GitCommands.Open;

/// <summary>
///  The "start with the last repository" decision. When the remembered directory is no longer a
///  valid repository (deleted, moved), it is reported for pruning instead of being silently kept
///  to fail again on every launch.
/// </summary>
/// <param name="WorkingDir">The directory to start in, or <see langword="null"/> to start without a repository.</param>
/// <param name="StalePathToPrune">A remembered directory that is no longer valid and should be forgotten.</param>
public readonly record struct StartupWorkingDir(string? WorkingDir, string? StalePathToPrune)
{
    public static StartupWorkingDir Resolve(bool startWithRecentWorkingDir, string? recentWorkingDir, Func<string?, bool> isValidGitWorkingDir)
    {
        if (!startWithRecentWorkingDir || string.IsNullOrWhiteSpace(recentWorkingDir))
        {
            return new(WorkingDir: null, StalePathToPrune: null);
        }

        return isValidGitWorkingDir(recentWorkingDir)
            ? new(WorkingDir: recentWorkingDir, StalePathToPrune: null)
            : new(WorkingDir: null, StalePathToPrune: recentWorkingDir);
    }
}
