using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtUtils;

namespace GitCommands.Worktree;

/// <summary>The create-worktree dialog's decisions.</summary>
public static class WorktreeCreateModel
{
    /// <summary>"-b name" for a new branch, else the existing ref's name.</summary>
    public static string? NewBranchOption(bool createNewBranch, string newBranchName, string? existingRefName)
        => createNewBranch ? $"-b {newBranchName}" : existingRefName;

    /// <summary>The suggested directory: the base path plus the branch name with path-invalid characters folded.</summary>
    public static string SuggestDirectory(string? basePath, string branchName)
        => $"{basePath}_{NormalizeBranchName(branchName)}";

    public static string NormalizeBranchName(string branchName)
        => string.Join("_", branchName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');

    /// <summary>An existing branch needs a selection; a new branch needs an unused, non-blank name.</summary>
    public static bool IsBranchChoiceValid(bool checkoutExistingBranch, bool hasSelectedExisting, string newBranchName, IEnumerable<string> existingBranchNames)
        => checkoutExistingBranch
            ? hasSelectedExisting
            : !string.IsNullOrWhiteSpace(newBranchName) && !existingBranchNames.Contains(newBranchName);

    /// <summary>The target folder must be missing or empty.</summary>
    public static bool IsTargetFolderValid(string? worktreeDirectory)
    {
        if (string.IsNullOrWhiteSpace(worktreeDirectory))
        {
            return false;
        }

        try
        {
            DirectoryInfo directoryInfo = new(worktreeDirectory);
            return !directoryInfo.Exists || (!directoryInfo.EnumerateFiles().Any() && !directoryInfo.EnumerateDirectories().Any());
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///  The worktree-add command, seeding worktree.useRelativePaths=true when the config
    ///  leaves it unset (FormCreateWorktree's historical shape).
    /// </summary>
    public static GitArgumentBuilder CreateCommand(Func<string, string?> getEffectiveSetting, string relativePath, string newBranchOption)
    {
        // https://git-scm.com/docs/git-worktree
        const string command = "worktree";
        GitCommandConfiguration commandConfiguration = new();
        IReadOnlyList<GitConfigItem> items = GitCommandConfiguration.Default.Get(command);
        foreach (GitConfigItem cfg in items)
        {
            commandConfiguration.Add(cfg, command);
        }

        if (string.IsNullOrEmpty(getEffectiveSetting("worktree.useRelativePaths")))
        {
            commandConfiguration.Add(new GitConfigItem("worktree.useRelativePaths", "true"), command);
        }

        return new GitArgumentBuilder(command, commandConfiguration)
        {
            "add",
            relativePath,
            newBranchOption,
        };
    }
}

/// <summary>The manage-worktrees dialog's enablement rules.</summary>
public static class WorktreeManagePolicy
{
    /// <summary>
    ///  A worktree can be acted on when it is not the only one, not deleted, and not the
    ///  currently opened one (path-normalized comparison).
    /// </summary>
    public static bool CanActOn(IReadOnlyList<GitWorktree> worktrees, int selectedIndex, string currentWorkingDir)
    {
        if (worktrees.Count <= 1 || selectedIndex < 0 || selectedIndex >= worktrees.Count)
        {
            return false;
        }

        GitWorktree worktree = worktrees[selectedIndex];
        if (worktree.IsDeleted)
        {
            return false;
        }

        return !PathsEqual(currentWorkingDir, worktree.Path);
    }

    /// <summary>The main worktree (row 0) can never be deleted.</summary>
    public static bool CanDelete(IReadOnlyList<GitWorktree> worktrees, int selectedIndex, string currentWorkingDir)
        => CanActOn(worktrees, selectedIndex, currentWorkingDir) && selectedIndex != 0;

    /// <summary>Pruning applies when any linked worktree is stale.</summary>
    public static bool CanPrune(IReadOnlyList<GitWorktree> worktrees)
        => worktrees.Skip(1).Any(worktree => worktree.IsDeleted);

    private static bool PathsEqual(string first, string second)
    {
        try
        {
            return new DirectoryInfo(first).FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                == new DirectoryInfo(second).FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>Submodule helpers shared by the add-submodule surfaces.</summary>
public static class SubmoduleAddModel
{
    /// <summary>Both paths are required before the add can run.</summary>
    public static bool IsValid(string? remotePath, string? localPath)
        => !string.IsNullOrEmpty(remotePath) && !string.IsNullOrEmpty(localPath);

    /// <summary>Parses "ls-remote --heads" output into branch names; ignores errors and warnings.</summary>
    public static IReadOnlyList<string> ParseLsRemoteHeads(string output)
        => [.. output.LazySplit('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(head =>
            {
                int branchIndex = head.IndexOf(GitRefName.RefsHeadsPrefix);
                return branchIndex == -1 ? null : head[(branchIndex + GitRefName.RefsHeadsPrefix.Length)..];
            })
            .WhereNotNull()];
}
