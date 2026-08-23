using GitExtensions.Extensibility.Git;

namespace GitCommands.Browse;

/// <summary>One worktree row of the browse toolbar's dropdown.</summary>
public readonly record struct WorktreeMenuEntry(GitWorktree Worktree, bool IsCurrent, bool Enabled);

public static class WorktreeMenuModel
{
    /// <summary>The dropdown only appears with something to switch between.</summary>
    public static bool IsToolbarVisible(bool isValidWorkingDir, int worktreeCount)
        => isValidWorkingDir && worktreeCount > 1;

    /// <summary>
    ///  Per-entry state: the current worktree is checked and disabled, deleted worktrees are
    ///  disabled. The historical comparison is case-insensitive (a Windows-ism made explicit).
    /// </summary>
    public static IReadOnlyList<WorktreeMenuEntry> Build(IReadOnlyList<GitWorktree> worktrees, string currentWorkingDir, bool pathsCaseInsensitive)
    {
        string normalizedCurrent = TrimSeparators(currentWorkingDir);
        StringComparison comparison = pathsCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        List<WorktreeMenuEntry> entries = [];
        foreach (GitWorktree worktree in worktrees)
        {
            bool isCurrent = string.Equals(TrimSeparators(worktree.Path), normalizedCurrent, comparison);
            entries.Add(new WorktreeMenuEntry(worktree, isCurrent, Enabled: !isCurrent && !worktree.IsDeleted));
        }

        return entries;

        static string TrimSeparators(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>The create-worktree base path: the first (main) worktree, else the working dir.</summary>
    public static string MainWorktreePath(IReadOnlyList<GitWorktree> worktrees, string workingDir)
        => worktrees.Count > 0 ? worktrees[0].Path : workingDir;
}
