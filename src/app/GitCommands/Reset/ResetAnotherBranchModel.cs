using GitCommands.Git;
using GitExtensions.Extensibility.Git;

namespace GitCommands.Reset;

/// <summary>
///  The reset-another-branch dialog's candidate list: local branches
///  except the current one and those already at the target revision, ordered so branches
///  tracking (or sharing a name with) the revision's remote branches come first.
/// </summary>
public static class ResetAnotherBranchCandidates
{
    public static IReadOnlyList<IGitRef> Build(
        IReadOnlyList<IGitRef> localRefs,
        IReadOnlyList<IGitRef> revisionRemoteRefs,
        string currentBranch,
        ObjectId revisionId)
    {
        bool isDetachedHead = currentBranch == DetachedHeadParser.DetachedBranch;

        return [.. localRefs
            .Where(r => r.IsHead)
            .Where(r => isDetachedHead || r.LocalName != currentBranch)
            .Where(r => revisionId != r.ObjectId)
            .OrderByDescending(r => revisionRemoteRefs.Any(r.IsTrackingRemote))
            .ThenByDescending(r => revisionRemoteRefs.Any(remote => remote.LocalName == r.LocalName))];
    }

    /// <summary>
    ///  The branch to preselect: with exactly one remote branch on the revision, the single
    ///  candidate tracking it (or sharing its name) - otherwise nothing.
    /// </summary>
    public static string? ResolveDefault(IReadOnlyList<IGitRef> candidates, IReadOnlyList<IGitRef> revisionRemoteRefs)
    {
        if (revisionRemoteRefs.Count != 1)
        {
            return null;
        }

        IGitRef availableRemote = revisionRemoteRefs[0];
        IGitRef[] matches = [.. candidates.Where(r => r.IsTrackingRemote(availableRemote) || r.LocalName == availableRemote.LocalName)];
        return matches.Length == 1 ? matches[0].Name : null;
    }
}
