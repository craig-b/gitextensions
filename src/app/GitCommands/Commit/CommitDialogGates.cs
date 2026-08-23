namespace GitCommands.Commit;

/// <summary>
///  The interaction gates the commit dialog walks before running the commit, in order. The view
///  maps each gate to its dialog; a Block* gate always aborts, the others abort on the user's
///  choice.
/// </summary>
public enum CommitDialogGate
{
    /// <summary>Yes/no warning that amend rewrites the last commit.</summary>
    ConfirmAmend,

    /// <summary>Yes/no: commit an empty changeset to conclude a merge.</summary>
    ConfirmEmptyMergeCommit,

    /// <summary>Nothing staged: offer stage-all (per filter) / empty commit / cancel.</summary>
    ResolveNoStagedChanges,

    /// <summary>A conflicted merge is in progress: error, abort.</summary>
    BlockConflictedMerge,

    /// <summary>The message is empty or still the unchanged template: error, abort.</summary>
    BlockEmptyMessage,

    /// <summary>Walk <see cref="CommitMessageValidator"/>'s violations, prompting per violation.</summary>
    ValidateMessage,

    /// <summary>HEAD is detached: offer checkout / create branch / continue / cancel.</summary>
    ConfirmDetachedHead,
}

/// <summary>
///  Computes which gates the commit dialog must walk (extracted from FormCommit's
///  ConfirmOrStageCommit and the guard prefix of DoCommit). Two phases,
///  matching the original control flow: the stage phase runs first and may change what is
///  staged (stage-all), then the commit phase guards the actual commit. Amend skips the
///  staged-changes gates entirely - amending just the message is legitimate.
/// </summary>
public static class CommitDialogGates
{
    public static IReadOnlyList<CommitDialogGate> EvaluateStagePhase(
        bool isAmend,
        bool dontConfirmAmend,
        bool stagedIsEmpty,
        bool isMergeCommit)
    {
        if (isAmend)
        {
            return dontConfirmAmend ? [] : [CommitDialogGate.ConfirmAmend];
        }

        if (stagedIsEmpty)
        {
            return isMergeCommit ? [CommitDialogGate.ConfirmEmptyMergeCommit] : [CommitDialogGate.ResolveNoStagedChanges];
        }

        return [];
    }

    /// <returns>
    ///  The gates in walk order; the list ends at the first Block* gate because nothing after
    ///  it can run.
    /// </returns>
    public static IReadOnlyList<CommitDialogGate> EvaluateCommitPhase(
        bool inConflictedMerge,
        bool useFormCommitMessage,
        bool messageIsEmptyOrTemplate,
        bool dontConfirmCommitIfNoBranch,
        bool isDetachedHead,
        bool inRebase)
    {
        if (inConflictedMerge)
        {
            return [CommitDialogGate.BlockConflictedMerge];
        }

        if (useFormCommitMessage && messageIsEmptyOrTemplate)
        {
            return [CommitDialogGate.BlockEmptyMessage];
        }

        List<CommitDialogGate> gates = [];

        if (useFormCommitMessage)
        {
            gates.Add(CommitDialogGate.ValidateMessage);
        }

        if (!dontConfirmCommitIfNoBranch && isDetachedHead && !inRebase)
        {
            gates.Add(CommitDialogGate.ConfirmDetachedHead);
        }

        return gates;
    }
}
