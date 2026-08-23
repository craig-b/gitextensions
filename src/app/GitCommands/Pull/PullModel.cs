using System.Text.RegularExpressions;
using GitCommands.Git;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;

namespace GitCommands.Pull;

public enum PullActionKind
{
    Merge,
    Rebase,
    Fetch,
}

public enum PullSourceKind
{
    Remote,
    Url,
    AllRemotes,
}

/// <summary>
///  The pull dialog's option tuple. The tri-state tag option maps
///  reachable/none/all to null/false/true; "all remotes" is an explicit kind rather than
///  the dialog's "[ All ]" sentinel text.
/// </summary>
public sealed record PullOptions(
    PullActionKind Action,
    PullSourceKind SourceKind,
    string Source,
    string? RemoteBranch,
    string? LocalBranch,
    bool? FetchTags = false,
    bool Unshallow = false,
    bool Prune = false,
    bool PruneTags = false)
{
    /// <summary>The source argument: the URL, "--all", or the remote name.</summary>
    public string EffectiveSource => SourceKind is PullSourceKind.AllRemotes ? "--all" : Source;

    public ArgumentString ToArguments(IGitModule module)
        => Action is PullActionKind.Fetch
            ? module.FetchCmd(EffectiveSource, RemoteBranch, LocalBranch, FetchTags, Unshallow, Prune, PruneTags)
            : module.PullCmd(EffectiveSource, RemoteBranch, Action is PullActionKind.Rebase, FetchTags, Unshallow);
}

/// <summary>The dialog's option-availability rules: what each action kind allows or forces.</summary>
public readonly record struct PullOptionAvailability(
    bool MergeAllowed,
    bool RebaseAllowed,
    bool LocalBranchAllowed,
    bool AllTagsAllowed,
    bool PruneAllowed,
    bool PruneTagsAllowed,
    bool ForceReachableTags,
    bool ForceFetchAction)
{
    public static PullOptionAvailability Evaluate(PullActionKind action, bool isPullAll)
    {
        bool isFetch = action is PullActionKind.Fetch;
        return new PullOptionAvailability(
            MergeAllowed: !isPullAll,
            RebaseAllowed: !isPullAll,
            LocalBranchAllowed: isFetch,
            AllTagsAllowed: isFetch,
            PruneAllowed: isFetch,
            PruneTagsAllowed: isFetch,
            ForceReachableTags: !isFetch,
            ForceFetchAction: isPullAll);
    }
}

public enum PullGate
{
    Proceed,
    BlockNoUrl,
    BlockNoRemote,
    BlockStarBranchNeedsFetch,
}

/// <summary>The prompt a refspec resolution may require before the pull can proceed.</summary>
public enum PullRefspecPrompt
{
    None,

    /// <summary>"There is no remote branch configured - pull from remote/local-branch?"</summary>
    ConfirmPullFromDerivedBranch,

    /// <summary>The fetch flavor of the same question.</summary>
    ConfirmFetchFromDerivedBranch,
}

/// <summary>The resolved refspec; on a prompt, accepting means fetching into the derived remote branch.</summary>
public sealed record PullRefspec(string? LocalBranch, string? RemoteBranch, PullRefspecPrompt Prompt = PullRefspecPrompt.None)
{
    public PullRefspec AcceptPrompt() => this with { RemoteBranch = LocalBranch, Prompt = PullRefspecPrompt.None };
}

public static class PullPreflight
{
    public static PullGate Evaluate(PullSourceKind sourceKind, string sourceText, PullActionKind action, string remoteBranchText)
    {
        if (sourceKind is PullSourceKind.Url && string.IsNullOrEmpty(sourceText))
        {
            return PullGate.BlockNoUrl;
        }

        if (sourceKind is PullSourceKind.Remote && string.IsNullOrEmpty(sourceText))
        {
            return PullGate.BlockNoRemote;
        }

        if (action is not PullActionKind.Fetch && remoteBranchText == "*")
        {
            return PullGate.BlockStarBranchNeedsFetch;
        }

        return PullGate.Proceed;
    }

    /// <summary>Rebasing a merge commit rewrites it; only remote-sourced rebases warn.</summary>
    public static bool ShouldConfirmRebaseMergeCommit(PullActionKind action, PullSourceKind sourceKind, bool mergeCommitExists)
        => action is PullActionKind.Rebase && sourceKind is PullSourceKind.Remote && mergeCommitExists;

    /// <summary>Auto-stash applies only to real pulls of a dirty non-bare work tree.</summary>
    public static bool ShouldStash(PullActionKind action, bool autoStash, bool isBareRepository, int dirtyFileCount)
        => action is not PullActionKind.Fetch && autoStash && !isBareRepository && dirtyFileCount > 0;

    /// <summary>
    ///  The local/remote branch pair for the command line (the dialog's densest rule).
    ///  <paramref name="configuredBranchRemote"/> is git config branch.&lt;local&gt;.remote.
    /// </summary>
    public static PullRefspec ResolveRefspec(
        bool isPullAll,
        string currentBranch,
        bool isDetachedHead,
        string localBranchText,
        string remoteBranchText,
        string remote,
        string? configuredBranchRemote,
        bool isFetch)
    {
        if (isPullAll)
        {
            return new PullRefspec(LocalBranch: null, RemoteBranch: null);
        }

        if (isDetachedHead)
        {
            return new PullRefspec(LocalBranch: null, remoteBranchText);
        }

        string? localBranch;
        if (currentBranch == localBranchText)
        {
            localBranch = remote == configuredBranchRemote || string.IsNullOrEmpty(configuredBranchRemote)
                ? string.IsNullOrEmpty(remoteBranchText) ? null : currentBranch
                : localBranchText;
        }
        else
        {
            localBranch = localBranchText;
        }

        if (string.IsNullOrEmpty(remoteBranchText) && !string.IsNullOrEmpty(localBranch) && remote != configuredBranchRemote && !isFetch)
        {
            return new PullRefspec(localBranch, remoteBranchText, PullRefspecPrompt.ConfirmPullFromDerivedBranch);
        }

        if (string.IsNullOrEmpty(remoteBranchText) && !string.IsNullOrEmpty(localBranch) && isFetch)
        {
            // fetching the current branch with no remote branch specified: fetch with no refspec
            if (currentBranch == localBranch)
            {
                return new PullRefspec(LocalBranch: null, remoteBranchText);
            }

            return new PullRefspec(localBranch, remoteBranchText, PullRefspecPrompt.ConfirmFetchFromDerivedBranch);
        }

        return new PullRefspec(localBranch, remoteBranchText);
    }
}

/// <summary>Detects the "configured ref no longer exists on the remote" pull failure, fixed by pruning.</summary>
public static partial class PullRejectionAnalyzer
{
    [GeneratedRegex(@"Your configuration specifies to .* the ref '.*'[\r]?\nfrom the remote, but no such ref was fetched.", RegexOptions.ExplicitCapture)]
    private static partial Regex RefRemovedRegex { get; }

    public static bool IsRefRemoved(string processOutput) => RefRemovedRegex.IsMatch(processOutput);
}
