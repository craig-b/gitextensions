namespace GitCommands.Open;

/// <summary>
///  The decisions of the repository-switch transaction, shared by every host: what to persist,
///  whether the dashboard (or the repository view) is shown, and whether view state that is
///  scoped to one repository — filters, quick filters, the terminal's folder — must be reset.
/// </summary>
/// <param name="IsValid">Whether the new module points at a valid git working directory.</param>
/// <param name="PathChanged">Whether the working directory actually changed (ordinal comparison).</param>
public sealed record RepoSwitchPlan(bool IsValid, bool PathChanged)
{
    /// <summary>The new path becomes the remembered last working directory only when it is valid.</summary>
    public bool PersistRecentWorkingDir => IsValid;

    /// <summary>An invalid working directory shows the dashboard instead of the repository view.</summary>
    public bool ShowDashboard => !IsValid;

    /// <summary>
    ///  Repository-scoped view state (revision filters, quick filters, terminal folder) is reset
    ///  only on an actual path change — re-opening the same repository keeps it.
    /// </summary>
    public bool ResetRepositoryScopedViewState => IsValid && PathChanged;

    public static RepoSwitchPlan Create(string? originalWorkingDir, string? newWorkingDir, bool isValidWorkingDir)
        => new(isValidWorkingDir, !string.Equals(originalWorkingDir, newWorkingDir, StringComparison.Ordinal));
}
