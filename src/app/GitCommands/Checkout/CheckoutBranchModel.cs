using GitCommands.Git;
using GitExtensions.Extensibility.Git;

namespace GitCommands.Checkout;

/// <summary>
///  The checkout-branch dialog's option tuple - exactly the
///  parameters of <see cref="Commands.CheckoutBranch"/>.
/// </summary>
public sealed record CheckoutBranchOptions(
    string BranchName,
    bool IsRemote,
    LocalChangesAction LocalChanges = LocalChangesAction.DontChange,
    CheckoutNewBranchMode NewBranchMode = CheckoutNewBranchMode.DontCreate,
    string? NewBranchName = null)
{
    public IGitCommand ToCommand()
        => Commands.CheckoutBranch(BranchName, IsRemote, LocalChanges, NewBranchMode, NewBranchName);
}

/// <summary>
///  The names the dialog derives when a remote branch is selected: its remote, the local
///  tracking branch, and a unique suggested name for a custom local branch.
/// </summary>
public sealed record RemoteBranchCheckoutNames(
    string RemoteName,
    string LocalBranchName,
    string SuggestedNewBranchName,
    bool LocalBranchExists)
{
    public static RemoteBranchCheckoutNames Resolve(
        string branch,
        IEnumerable<string> remoteNames,
        Func<string, string, string?> getLocalTrackingBranchName,
        Func<string, bool> localBranchExists)
    {
        string remoteName = GitRefName.GetRemoteName(branch, remoteNames);
        string localBranchName = getLocalTrackingBranchName(remoteName, branch) ?? "";
        string remoteBranchName = remoteName.Length > 0 ? branch[(remoteName.Length + 1)..] : branch;

        string suggestedName = string.Concat(remoteName, "_", remoteBranchName);
        int suffix = 2;
        while (localBranchExists(suggestedName))
        {
            // historical quirk kept as-is: the uniquified name seeds from the local
            // tracking name rather than the remote branch name
            suggestedName = string.Concat(remoteName, "_", localBranchName, "_", suffix.ToString());
            suffix++;
        }

        return new RemoteBranchCheckoutNames(remoteName, localBranchName, suggestedName, localBranchExists(localBranchName));
    }
}

/// <summary>The fast-forward test behind the "reset local branch" warning.</summary>
public readonly record struct ResetBranchFastForwardCheck(bool IsFastForward, string MergeBaseDisplay)
{
    public static ResetBranchFastForwardCheck Evaluate(ObjectId localBranchId, ObjectId mergeBaseId)
        => new(
            localBranchId == mergeBaseId,
            mergeBaseId.IsZero ? "merge base" : mergeBaseId.ToShortString());
}

/// <summary>The effective local-changes handling for a checkout.</summary>
public readonly record struct CheckoutLocalChangesDecision(LocalChangesAction EffectiveAction, bool PersistAsDefault);

public static class CheckoutLocalChangesPolicy
{
    /// <summary>
    ///  The action downgrades to DontChange when there is nothing to protect (no uncommitted
    ///  changes, or a dialog-less checkout without the use-default-action setting), and the
    ///  selection persists as the default only when requested and not Reset.
    /// </summary>
    public static CheckoutLocalChangesDecision Resolve(
        LocalChangesAction selectedAction,
        bool setAsDefaultRequested,
        bool dialogVisible,
        bool useDefaultActionSetting,
        bool hasUncommittedChanges)
    {
        bool persistAsDefault = selectedAction is not LocalChangesAction.Reset && setAsDefaultRequested;

        LocalChangesAction effectiveAction = (!dialogVisible && !useDefaultActionSetting) || !hasUncommittedChanges
            ? LocalChangesAction.DontChange
            : selectedAction;

        return new CheckoutLocalChangesDecision(effectiveAction, persistAsDefault);
    }
}

/// <summary>The dialog's branch-candidate rules.</summary>
public static class CheckoutBranchCandidates
{
    /// <summary>Whether the dialog shows at all, or the checkout runs directly.</summary>
    public static bool ShouldShowDialog(
        bool alwaysShowDialogSetting,
        bool localBranchSelected,
        bool hasUncommittedChanges,
        bool useDefaultActionSetting)
        => alwaysShowDialogSetting
            || !localBranchSelected
            || (hasUncommittedChanges && !useDefaultActionSetting);

    /// <summary>
    ///  The branches containing every one of the given commits (detached heads and
    ///  "*/HEAD" pseudo-branches excluded).
    /// </summary>
    public static IReadOnlyList<string> IntersectBranchesContainingCommits(
        IReadOnlyList<ObjectId> objectIds,
        Func<ObjectId, IEnumerable<string>> getBranchesContainingCommit)
    {
        HashSet<string> result = [];
        if (objectIds.Count > 0)
        {
            result.UnionWith(Eligible(getBranchesContainingCommit(objectIds[0])));
        }

        for (int index = 1; index < objectIds.Count; index++)
        {
            result.IntersectWith(Eligible(getBranchesContainingCommit(objectIds[index])));
        }

        return [.. result];

        static IEnumerable<string> Eligible(IEnumerable<string> branches)
            => branches.Where(branch => !DetachedHeadParser.IsDetachedHead(branch) && !branch.EndsWith("/HEAD"));
    }
}
