using GitExtensions.Extensibility.Git;

namespace GitCommands.Push;

/// <summary>The tag tab's selection: the "[ All ]" sentinel maps to a push of all tags.</summary>
public readonly record struct TagPushSelection(string Tag, bool AllTags)
{
    public const string AllRefsSentinel = "[ All ]";

    public static TagPushSelection Parse(string comboText)
        => comboText == AllRefsSentinel
            ? new TagPushSelection("", AllTags: true)
            : new TagPushSelection(comboText, AllTags: false);

    /// <summary>The historical gate: a blank single tag cannot be pushed.</summary>
    public bool IsValid => AllTags || !string.IsNullOrWhiteSpace(Tag);
}

/// <summary>Which remote the push dialog preselects.</summary>
public sealed record RemotePreselection(int SelectedIndex, string? SelectedName);

public static class PushRemoteSelector
{
    public const string DefaultRemoteName = "origin";

    /// <summary>Explicit name, else branch.&lt;name&gt;.remote, else "origin", else the first remote.</summary>
    public static RemotePreselection Preselect(
        IReadOnlyList<string> remoteNames,
        string? explicitRemoteName,
        string? branchRemoteSetting)
    {
        string? wanted = string.IsNullOrEmpty(explicitRemoteName) ? branchRemoteSetting : explicitRemoteName;

        if (!string.IsNullOrEmpty(wanted))
        {
            int index = IndexOf(name => name.Equals(wanted, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                return new RemotePreselection(index, remoteNames[index]);
            }
        }

        int originIndex = IndexOf(name => name == DefaultRemoteName);
        if (originIndex >= 0)
        {
            return new RemotePreselection(originIndex, remoteNames[originIndex]);
        }

        return remoteNames.Count > 0
            ? new RemotePreselection(0, remoteNames[0])
            : new RemotePreselection(-1, null);

        int IndexOf(Func<string, bool> predicate)
        {
            for (int i = 0; i < remoteNames.Count; i++)
            {
                if (predicate(remoteNames[i]))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}

public static class PushDestinationResolver
{
    /// <summary>
    ///  A URL push has no named remote (scripts and ssh key selection key off the name);
    ///  a remote push trims the name - the asymmetric Trim is the historical behavior.
    /// </summary>
    public static (string Remote, string Destination) Resolve(bool pushToUrl, string url, string? remoteName)
        => pushToUrl
            ? ("", url)
            : (remoteName?.Trim() ?? "", remoteName ?? "");
}

/// <summary>Module-free resolution of a remote's default push target from its push refspecs.</summary>
public static class PushRefspecResolver
{
    /// <summary>
    ///  The first "lhs:rhs" refspec whose lhs names the branch (or "*") yields the rhs head
    ///  name (wildcards substituted) - GetDefaultPushRemote's historical rules without the
    ///  GitRef/module detour.
    /// </summary>
    public static string? ResolveDefaultPushTarget(IEnumerable<string>? pushRefspecs, string branch)
    {
        if (pushRefspecs is null)
        {
            return null;
        }

        List<(string Lhs, string Rhs)> pairs = [.. pushRefspecs
            .Select(refspec => refspec.Split(':'))
            .Where(parts => parts.Length == 2)
            .Select(parts => (parts[0], parts[1]))];

        foreach ((string lhs, string rhs) in pairs)
        {
            if (HeadName(lhs) is string lhsName
                && lhsName.Equals(branch, StringComparison.OrdinalIgnoreCase)
                && HeadName(rhs) is string rhsName)
            {
                return rhsName;
            }
        }

        foreach ((string lhs, string rhs) in pairs)
        {
            if (HeadName(lhs) == "*" && HeadName(rhs.Replace("*", branch)) is string rhsName)
            {
                return rhsName;
            }
        }

        return null;

        static string? HeadName(string completeName)
            => completeName.StartsWith(GitRefName.RefsHeadsPrefix)
                ? completeName[GitRefName.RefsHeadsPrefix.Length..]
                : null;
    }
}

/// <summary>The push dialog's new-branch-for-remote warning rule.</summary>
public static class NewBranchWarning
{
    /// <summary>
    ///  A remote branch counts as known when a remote ref carries its name for this remote,
    ///  or a local head with that name tracks this remote.
    /// </summary>
    public static bool IsBranchKnownToRemote(IEnumerable<IGitRef> refs, string? remote, string branch)
        => refs.Any(gitRef => gitRef.IsRemote && gitRef.Remote == remote && gitRef.LocalName == branch)
            || refs.Any(gitRef => gitRef.IsHead && gitRef.Name == branch && gitRef.TrackingRemote == remote);

    /// <summary>
    ///  Warn when pushing a single branch whose remote name is neither the configured push
    ///  target nor known to the remote.
    /// </summary>
    public static bool ShouldWarnNewBranch(
        bool isBranchTab,
        bool pushToRemote,
        bool isBareRepository,
        string localBranchText,
        string allRefsSentinel,
        string remoteBranchText,
        string? defaultPushTarget,
        bool knownToRemote)
        => isBranchTab
            && pushToRemote
            && !isBareRepository
            && localBranchText != allRefsSentinel
            && remoteBranchText != defaultPushTarget
            && !knownToRemote;
}

public enum RejectionFollowUp
{
    GiveUp,
    PullThenRetry,
    UnsupportedPullAction,
    BlockedByMergeCommit,
}

public static class PushRejectionPolicy
{
    /// <summary>
    ///  After a rejected push: Default resolves through the configured pull action, None
    ///  gives up, only Merge/Rebase can auto-pull, and rebasing a merge commit is refused.
    /// </summary>
    public static RejectionFollowUp Decide(GitPullAction onRejectedPullAction, GitPullAction defaultPullAction, bool isRebasingMergeCommit)
    {
        if (onRejectedPullAction == GitPullAction.Default)
        {
            onRejectedPullAction = defaultPullAction;
        }

        if (onRejectedPullAction == GitPullAction.None)
        {
            return RejectionFollowUp.GiveUp;
        }

        if (onRejectedPullAction is not (GitPullAction.Merge or GitPullAction.Rebase))
        {
            return RejectionFollowUp.UnsupportedPullAction;
        }

        return isRebasingMergeCommit ? RejectionFollowUp.BlockedByMergeCommit : RejectionFollowUp.PullThenRetry;
    }
}
