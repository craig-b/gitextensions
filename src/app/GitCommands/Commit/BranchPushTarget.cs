using GitExtensions.Extensibility.Git;

namespace GitCommands.Commit;

public enum PushTargetKind
{
    /// <summary>The branch tracks a remote branch; <see cref="BranchPushTarget.Target"/> is "remote/branch".</summary>
    Tracked,

    /// <summary>No tracking configured; <see cref="BranchPushTarget.Target"/> is "defaultRemote/branch" and a push would create it.</summary>
    DefaultRemoteUntracked,

    /// <summary>The repository has no remotes; <see cref="BranchPushTarget.Target"/> is null.</summary>
    NoRemoteConfigured,
}

/// <summary>
///  Resolves where the current branch would push to - the commit dialog's status-bar display
///  (extracted from FormCommit.UpdateBranchNameDisplayAsync). The view owns
///  the wording for the untracked/no-remote annotations; this model owns the resolution: a
///  tracked branch pushes to its tracking remote, an untracked one to "origin" when it exists,
///  else to the alphabetically first remote.
/// </summary>
public sealed record BranchPushTarget(PushTargetKind Kind, string? Target)
{
    public static BranchPushTarget Resolve(IGitRef? currentBranch, IEnumerable<string> remoteNames, string branchName)
    {
        if (currentBranch is not null &&
            !string.IsNullOrEmpty(currentBranch.TrackingRemote) &&
            !string.IsNullOrEmpty(currentBranch.MergeWith))
        {
            return new(PushTargetKind.Tracked, $"{currentBranch.TrackingRemote}/{currentBranch.MergeWith}");
        }

        List<string> remotes = [.. remoteNames];
        string? defaultRemote = remotes.FirstOrDefault(r => r == "origin") ?? remotes.OrderBy(r => r).FirstOrDefault();

        return defaultRemote is not null
            ? new(PushTargetKind.DefaultRemoteUntracked, $"{defaultRemote}/{branchName}")
            : new(PushTargetKind.NoRemoteConfigured, null);
    }
}
