using GitCommands.Commit;

namespace GitCommandsTests.Commit;

public class CommitDialogGatesTests
{
    [Test]
    public void Amend_asks_for_confirmation_and_skips_the_staged_gates()
    {
        CommitDialogGates.EvaluateStagePhase(isAmend: true, dontConfirmAmend: false, stagedIsEmpty: true, isMergeCommit: true)
            .Should().Equal(CommitDialogGate.ConfirmAmend);
    }

    [Test]
    public void Amend_with_confirmation_disabled_walks_no_gates_even_with_nothing_staged()
    {
        CommitDialogGates.EvaluateStagePhase(isAmend: true, dontConfirmAmend: true, stagedIsEmpty: true, isMergeCommit: false)
            .Should().BeEmpty();
    }

    [Test]
    public void Empty_staged_during_merge_asks_to_confirm_the_empty_merge_commit()
    {
        CommitDialogGates.EvaluateStagePhase(isAmend: false, dontConfirmAmend: false, stagedIsEmpty: true, isMergeCommit: true)
            .Should().Equal(CommitDialogGate.ConfirmEmptyMergeCommit);
    }

    [Test]
    public void Empty_staged_otherwise_offers_the_stage_all_or_empty_commit_choice()
    {
        CommitDialogGates.EvaluateStagePhase(isAmend: false, dontConfirmAmend: false, stagedIsEmpty: true, isMergeCommit: false)
            .Should().Equal(CommitDialogGate.ResolveNoStagedChanges);
    }

    [Test]
    public void Staged_changes_walk_no_stage_phase_gates()
    {
        CommitDialogGates.EvaluateStagePhase(isAmend: false, dontConfirmAmend: false, stagedIsEmpty: false, isMergeCommit: false)
            .Should().BeEmpty();
    }

    [Test]
    public void Conflicted_merge_blocks_everything()
    {
        CommitDialogGates.EvaluateCommitPhase(
                inConflictedMerge: true,
                useFormCommitMessage: true,
                messageIsEmptyOrTemplate: true,
                dontConfirmCommitIfNoBranch: false,
                isDetachedHead: true,
                inRebase: false)
            .Should().Equal(CommitDialogGate.BlockConflictedMerge);
    }

    [Test]
    public void Empty_or_template_message_blocks_when_the_form_message_is_used()
    {
        CommitDialogGates.EvaluateCommitPhase(
                inConflictedMerge: false,
                useFormCommitMessage: true,
                messageIsEmptyOrTemplate: true,
                dontConfirmCommitIfNoBranch: false,
                isDetachedHead: true,
                inRebase: false)
            .Should().Equal(CommitDialogGate.BlockEmptyMessage);
    }

    [Test]
    public void Empty_message_does_not_block_when_git_supplies_the_message()
    {
        CommitDialogGates.EvaluateCommitPhase(
                inConflictedMerge: false,
                useFormCommitMessage: false,
                messageIsEmptyOrTemplate: true,
                dontConfirmCommitIfNoBranch: false,
                isDetachedHead: false,
                inRebase: false)
            .Should().BeEmpty();
    }

    [Test]
    public void Validation_then_detached_head_in_that_order()
    {
        CommitDialogGates.EvaluateCommitPhase(
                inConflictedMerge: false,
                useFormCommitMessage: true,
                messageIsEmptyOrTemplate: false,
                dontConfirmCommitIfNoBranch: false,
                isDetachedHead: true,
                inRebase: false)
            .Should().Equal(CommitDialogGate.ValidateMessage, CommitDialogGate.ConfirmDetachedHead);
    }

    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    [TestCase(false, false, true)]
    public void Detached_head_gate_requires_detached_not_rebasing_and_not_suppressed(bool dontConfirm, bool notDetached, bool inRebase)
    {
        CommitDialogGates.EvaluateCommitPhase(
                inConflictedMerge: false,
                useFormCommitMessage: false,
                messageIsEmptyOrTemplate: false,
                dontConfirmCommitIfNoBranch: dontConfirm,
                isDetachedHead: !notDetached,
                inRebase: inRebase)
            .Should().BeEmpty();
    }
}
