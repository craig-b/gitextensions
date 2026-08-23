using GitCommands.Git;
using GitExtensions.Extensibility.Git;

namespace GitCommands.Push;

public enum MultiBranchAction
{
    None,
    Push,
    Force,
    Delete,
}

/// <summary>
///  One row of the multi-branch push grid. A null local branch is a remote-only row whose
///  only legal action is delete.
/// </summary>
public sealed record MultiBranchRow(
    string? LocalBranch,
    string RemoteBranch,
    string AheadBehindDisplay,
    MultiBranchAction Action = MultiBranchAction.None)
{
    public bool IsDeleteOnly => string.IsNullOrEmpty(LocalBranch);

    public bool IsTracked => !string.IsNullOrEmpty(LocalBranch) && !string.IsNullOrEmpty(RemoteBranch);

    /// <summary>The grid's mutual exclusivity: choosing any action clears the others.</summary>
    public MultiBranchRow With(MultiBranchAction action)
        => this with { Action = IsDeleteOnly && action is MultiBranchAction.Push or MultiBranchAction.Force ? Action : action };
}

/// <summary>The multi-branch tab's decisions (FormPush's DataTable becomes a projection).</summary>
public static class MultiBranchPushModel
{
    /// <summary>
    ///  Strips any leading process noise so the ls-remote output starts at the first SHA1
    ///  (the first tab is expected at index 40).
    /// </summary>
    public static string CleanLsRemoteOutput(string processOutput)
    {
        int firstTabIdx = processOutput.IndexOf('\t');
        return firstTabIdx == 40
            ? processOutput
            : firstTabIdx > 40
                ? processOutput[(firstTabIdx - 40)..]
                : string.Empty;
    }

    /// <summary>
    ///  The rows: one per local head (with its remote pairing and ahead/behind display),
    ///  then delete-only rows for remote branches without a local counterpart.
    /// </summary>
    public static IReadOnlyList<MultiBranchRow> BuildRows(
        IReadOnlyList<IGitRef> localHeads,
        IReadOnlyList<IGitRef> remoteHeads,
        string remote,
        IReadOnlyDictionary<string, AheadBehindData>? aheadBehindData)
    {
        Dictionary<string, IGitRef> remoteBranches = new();
        foreach (IGitRef remoteHead in remoteHeads)
        {
            remoteBranches.TryAdd(remoteHead.LocalName, remoteHead);
        }

        List<MultiBranchRow> rows = [];

        foreach (IGitRef head in localHeads)
        {
            string remoteName = head.Remote == remote
                ? head.MergeWith ?? head.Name
                : string.Empty;
            bool isKnownAtRemote = remoteBranches.TryGetValue(head.Name, out IGitRef? remoteBranch);

            bool isAheadRemote = false;
            AheadBehindData aheadBehind = default;
            if (aheadBehindData is not null && aheadBehindData.TryGetValue(head.Name, out aheadBehind))
            {
                isAheadRemote = GitRefName.GetRemoteName(aheadBehind.RemoteRef) == remote;
            }

            string remoteCell = isAheadRemote ? GitRefName.GetRemoteBranch(aheadBehind.RemoteRef) : remoteName;
            string aheadDisplay = isAheadRemote
                ? aheadBehind.ToDisplay()
                : !isKnownAtRemote
                    ? ""
                    : head.ObjectId == remoteBranch!.ObjectId
                        ? "="
                        : "<>";

            rows.Add(new MultiBranchRow(head.Name, remoteCell, aheadDisplay));
        }

        // Offer to delete all the left over remote branches.
        foreach (IGitRef remoteHead in remoteHeads)
        {
            if (localHeads.All(head => head.Name != remoteHead.LocalName))
            {
                rows.Add(new MultiBranchRow(LocalBranch: null, remoteHead.LocalName, AheadBehindDisplay: ""));
            }
        }

        return rows;
    }

    /// <summary>
    ///  The push actions for the checked rows: a blank remote branch falls back to the local
    ///  name, rows still blank are skipped, delete rows delete.
    /// </summary>
    public static IReadOnlyList<GitPushAction> ToPushActions(IEnumerable<MultiBranchRow> rows)
    {
        List<GitPushAction> actions = [];
        foreach (MultiBranchRow row in rows)
        {
            string remoteBranch = string.IsNullOrWhiteSpace(row.RemoteBranch) ? row.LocalBranch ?? "" : row.RemoteBranch;
            if (string.IsNullOrWhiteSpace(remoteBranch))
            {
                continue;
            }

            switch (row.Action)
            {
                case MultiBranchAction.Push or MultiBranchAction.Force when row.LocalBranch is not null:
                    actions.Add(new GitPushAction(row.LocalBranch, remoteBranch, force: row.Action is MultiBranchAction.Force));
                    break;
                case MultiBranchAction.Delete:
                    actions.Add(GitPushAction.DeleteRemoteBranch(remoteBranch));
                    break;
            }
        }

        return actions;
    }
}
