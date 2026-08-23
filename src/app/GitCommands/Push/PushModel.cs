using System.Text.RegularExpressions;
using GitCommands.Git;
using GitExtensions.Extensibility.Git;

namespace GitCommands.Push;

/// <summary>What to do about the local branch's tracking reference when pushing.</summary>
public enum TrackingRefDecision
{
    NoTrack,
    TrackSilently,
    ConfirmTrack,
}

/// <summary>The push dialog's decisions.</summary>
public static class PushPreflight
{
    /// <summary>Tags cannot be pushed with --force-with-lease, so any tag force means plain force.</summary>
    public static ForcePushOptions ResolveForceOption(bool forceBranches, bool forceTags, bool forceWithLease)
        => forceBranches || forceTags
            ? ForcePushOptions.Force
            : forceWithLease
                ? ForcePushOptions.ForceWithLease
                : ForcePushOptions.DoNotForce;

    /// <summary>
    ///  The default remote branch for a selected local branch: the remote's configured push
    ///  refspec target, else the tracked branch when it tracks this remote, else a new
    ///  "{prefix}{branch}" name on the remote.
    /// </summary>
    public static string ResolveRemoteBranch(
        string branchText,
        bool pushToRemote,
        IGitRef? selectedBranchRef,
        string? selectedRemoteName,
        string? defaultPushRemote,
        string? remotePrefix)
    {
        if (pushToRemote && selectedBranchRef is not null && selectedRemoteName is not null)
        {
            if (!string.IsNullOrEmpty(defaultPushRemote))
            {
                return defaultPushRemote;
            }

            if (selectedBranchRef.TrackingRemote.Equals(selectedRemoteName, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(selectedBranchRef.MergeWith))
            {
                return selectedBranchRef.MergeWith;
            }
        }

        return $"{remotePrefix}{branchText}";
    }

    /// <summary>
    ///  Whether the push should set up a tracking reference: explicitly requested, or derived
    ///  for an untracked local branch that isn't named like a remote ref - vetoed by
    ///  git config branch.autosetupmerge=false, and confirmed unless suppressed.
    /// </summary>
    public static TrackingRefDecision EvaluateTracking(
        bool replaceTrackingRequested,
        string branchText,
        string remoteBranchText,
        IGitRef? selectedLocalBranch,
        IEnumerable<string> remoteNames,
        string? autoSetupMergeSetting,
        bool dontConfirmSetting)
    {
        if (replaceTrackingRequested)
        {
            return TrackingRefDecision.TrackSilently;
        }

        if (string.IsNullOrWhiteSpace(remoteBranchText))
        {
            return TrackingRefDecision.NoTrack;
        }

        bool track = selectedLocalBranch is not null
            && string.IsNullOrEmpty(selectedLocalBranch.TrackingRemote)
            && !remoteNames.Any(remoteName => branchText.StartsWith(remoteName, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(autoSetupMergeSetting) && autoSetupMergeSetting.ToLowerInvariant() == "false")
        {
            track = false;
        }

        return !track
            ? TrackingRefDecision.NoTrack
            : dontConfirmSetting
                ? TrackingRefDecision.TrackSilently
                : TrackingRefDecision.ConfirmTrack;
    }
}

/// <summary>Detects a rejected push in the process output (which contains color codes etc.).</summary>
public static class PushRejectionAnalyzer
{
    public readonly record struct PushRejection(bool IsRejected, bool IsCurrentBranch);

    public static PushRejection Analyze(string processOutput, string currentBranchName)
    {
        Regex isRejected = new($"! \\[rejected\\]\\s*((?<currBranch>{Regex.Escape(currentBranchName)})|.*) -> ");
        Match match = isRejected.Match(processOutput);
        return new PushRejection(match.Success, match.Success && match.Groups["currBranch"].Success);
    }

    /// <summary>
    ///  Adds --force-with-lease to an existing push argument line for a retry, unless a force
    ///  flag is already present. Returns null when the line should stay unchanged.
    ///  (Note that WSL may add other arguments before the actual command, so "push" may not be first.)
    /// </summary>
    public static string? WithForceWithLease(string processArguments)
    {
        if (processArguments.Contains(" -f ") || processArguments.Contains(" --force"))
        {
            return null;
        }

        int position = processArguments.IndexOf("push ");
        return position < 0
            ? null
            : processArguments.Insert(position + "push ".Length, "--force-with-lease ");
    }
}
