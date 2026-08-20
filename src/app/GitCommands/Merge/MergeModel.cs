using GitCommands.Git;
using GitExtensions.Extensibility;

namespace GitCommands.Merge;

/// <summary>
///  The merge dialog's option tuple over the portable
///  <see cref="Commands.MergeBranch"/> builder.
/// </summary>
public sealed record MergeBranchOptions(
    string Branch,
    bool AllowFastForward = true,
    bool Squash = false,
    bool NoCommit = false,
    string Strategy = "",
    bool AllowUnrelatedHistories = false,
    int? LogMessageCount = null)
{
    public ArgumentString ToArguments(string? mergeMessagePath, Func<string?, string?> getPathForGitExecution)
        => Commands.MergeBranch(Branch, AllowFastForward, Squash, NoCommit, Strategy, AllowUnrelatedHistories, mergeMessagePath, getPathForGitExecution, LogMessageCount);
}

public static class MergeStrategies
{
    public static IReadOnlyList<string> Known { get; } = ["resolve", "recursive", "octopus", "ours", "subtree"];
}

/// <summary>What the merge dialog's options allow, given each other.</summary>
public readonly record struct MergeOptionAvailability(
    bool SquashAllowed,
    bool StrategyTextVisible,
    bool LogCountEnabled,
    bool MessageTextEnabled)
{
    public static MergeOptionAvailability Evaluate(bool noFastForward, bool nonDefaultStrategy, bool addLogMessages, bool addMergeMessage)
        => new(
            SquashAllowed: !noFastForward,
            StrategyTextVisible: nonDefaultStrategy,
            LogCountEnabled: addLogMessages,
            MessageTextEnabled: addMergeMessage);
}

public static class MergePostAction
{
    /// <summary>The dialog closes and notifies on success - and also after conflicts, which the handler owns.</summary>
    public static bool ShouldCloseAndNotify(bool success, bool wasConflict) => success || wasConflict;

    /// <summary>The conflict handler offers a commit unless --no-commit was requested.</summary>
    public static bool OfferCommitOnConflict(bool noCommit) => !noCommit;
}

public static class MergeDefaultBranch
{
    /// <summary>The preselected branch: the caller's choice, else the selected head's remote branch.</summary>
    public static string? Resolve(string? defaultBranch, string? remoteBranchOfSelectedHead)
        => !string.IsNullOrEmpty(defaultBranch) ? defaultBranch : remoteBranchOfSelectedHead;
}
