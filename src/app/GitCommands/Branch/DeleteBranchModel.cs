using GitCommands.Git;
using GitExtensions.Extensibility.Git;

namespace GitCommands.Branch;

public enum DeleteBranchGate
{
    Proceed,
    NoSelection,
    BlockCurrentBranch,
}

/// <summary>Parsed "git branch --merged" output: the current branch and the merged names.</summary>
public sealed record MergedBranchScan(string? CurrentBranch, IReadOnlySet<string> MergedBranches)
{
    public static MergedBranchScan Parse(IEnumerable<string> gitBranchMergedOutput)
    {
        string? currentBranch = null;
        HashSet<string> mergedBranches = [];
        foreach (string branch in gitBranchMergedOutput)
        {
            if (branch.StartsWith("* "))
            {
                currentBranch = branch.Trim('*', ' ');
            }
            else
            {
                mergedBranches.Add(branch.Trim());
            }
        }

        return new MergedBranchScan(currentBranch, mergedBranches);
    }
}

/// <summary>The delete-branch dialog's decisions.</summary>
public static class DeleteBranchPreflight
{
    public static DeleteBranchGate Evaluate(IReadOnlyList<IGitRef> selectedBranches, string? currentBranch)
    {
        if (selectedBranches.Count == 0)
        {
            return DeleteBranchGate.NoSelection;
        }

        return selectedBranches.Any(branch => branch.Name == currentBranch)
            ? DeleteBranchGate.BlockCurrentBranch
            : DeleteBranchGate.Proceed;
    }

    /// <summary>The merged-branch scan is skipped entirely when the warning is suppressed (a load-time cost).</summary>
    public static bool NeedsMergedBranchScan(bool dontConfirmSetting) => !dontConfirmSetting;

    /// <summary>Branches are treated as unmerged whenever HEAD is detached.</summary>
    public static bool ShouldConfirmUnmerged(
        bool dontConfirmSetting,
        string? currentBranch,
        IReadOnlyList<IGitRef> selectedBranches,
        IReadOnlySet<string> mergedBranches)
    {
        if (dontConfirmSetting)
        {
            return false;
        }

        return currentBranch is null
            || DetachedHeadParser.IsDetachedHead(currentBranch)
            || selectedBranches.Any(branch => IsUnmerged(currentBranch, branch.Name, mergedBranches));
    }

    /// <summary>A single branch's unmerged verdict; a detached HEAD forces the warning.</summary>
    public static bool IsUnmerged(string? currentBranch, string branchName, IReadOnlySet<string> mergedBranches)
        => currentBranch is null
            || DetachedHeadParser.IsDetachedHead(currentBranch)
            || !mergedBranches.Contains(branchName);
}

/// <summary>
///  Which selected branches are checked out in worktrees: the main worktree blocks deletion,
///  linked worktrees can be offered for removal, stale (deleted) worktrees only require a
///  prune. The worktree of the current working directory is exempt (its branch is blocked as
///  the current branch instead).
/// </summary>
public readonly record struct WorktreeBranchClassification(
    bool HasDeletedWorktrees,
    IReadOnlyList<(IGitRef Branch, GitWorktree Worktree)> MainWorktreeBranches,
    IReadOnlyList<(IGitRef Branch, GitWorktree Worktree)> LinkedWorktreeBranches)
{
    public static WorktreeBranchClassification Classify(
        IReadOnlyList<IGitRef> selectedBranches,
        IReadOnlyList<GitWorktree> worktrees,
        string currentWorkingDir)
    {
        bool hasDeletedWorktrees = false;
        List<(IGitRef Branch, GitWorktree Worktree)> mainWorktreeBranches = [];
        List<(IGitRef Branch, GitWorktree Worktree)> linkedWorktreeBranches = [];

        for (int i = 0; i < worktrees.Count; i++)
        {
            GitWorktree worktree = worktrees[i];
            if (worktree.Branch is null)
            {
                continue;
            }

            if (worktree.IsDeleted)
            {
                if (selectedBranches.Any(b => b.Name == worktree.Branch))
                {
                    hasDeletedWorktrees = true;
                }

                continue;
            }

            string worktreeDir = Path.GetFullPath(worktree.Path).TrimEnd(Path.DirectorySeparatorChar);
            if (string.Equals(worktreeDir, currentWorkingDir, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (IGitRef branch in selectedBranches)
            {
                if (branch.Name == worktree.Branch)
                {
                    if (i == 0)
                    {
                        mainWorktreeBranches.Add((branch, worktree));
                    }
                    else
                    {
                        linkedWorktreeBranches.Add((branch, worktree));
                    }

                    break;
                }
            }
        }

        return new(hasDeletedWorktrees, mainWorktreeBranches, linkedWorktreeBranches);
    }
}
