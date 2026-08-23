using GitCommands.Commit;
using GitExtensions.Extensibility.Git;
using NSubstitute;

namespace GitCommandsTests.Commit;

/// <summary>
///  Tests for <see cref="StagedListReconciler"/> - the in-place list reconciliation FormCommit
///  performs after staging/unstaging instead of a full rescan. Extracted logic; the behavior
///  asserted here is the dialog's long-standing behavior, quirks included.
/// </summary>
public class StagedListReconcilerTests
{
    private IGitModule _module = null!;

    [SetUp]
    public void Setup()
    {
        _module = Substitute.For<IGitModule>();
    }

    [Test]
    public void AfterStage_removes_staged_files_from_unstaged()
    {
        GitItemStatus a = new("a.txt");
        GitItemStatus b = new("b.txt");
        GitItemStatus untouched = new("c.txt");

        List<GitItemStatus> result = StagedListReconciler.ReconcileAfterStage([a, b, untouched], [a, b], _module);

        result.Should().Equal(untouched);
    }

    [Test]
    public void AfterStage_matches_renamed_files_by_old_name_too()
    {
        GitItemStatus oldEntry = new("old.txt");
        GitItemStatus renamed = new("new.txt") { IsRenamed = true, OldName = "old.txt" };

        List<GitItemStatus> result = StagedListReconciler.ReconcileAfterStage([oldEntry], [renamed], _module);

        result.Should().BeEmpty();
    }

    [Test]
    public void AfterStage_keeps_dirty_submodules_and_refreshes_their_status()
    {
        GitItemStatus dirtySubmodule = new("sub") { IsSubmodule = true, IsDirty = true };
        GitItemStatus cleanSubmodule = new("sub2") { IsSubmodule = true, IsDirty = false };

        List<GitItemStatus> result = StagedListReconciler.ReconcileAfterStage([dirtySubmodule, cleanSubmodule], [dirtySubmodule, cleanSubmodule], _module);

        result.Should().Equal(dirtySubmodule);
        _module.Received().GetSubmoduleCurrentStatus(Arg.Is<IReadOnlyList<GitItemStatus>>(list => list.Single() == dirtySubmodule));
    }

    [Test]
    public void AfterUnstage_skips_files_still_in_the_staged_list()
    {
        // Partially unstaged: git still reports the file staged, so it must not be duplicated
        // into unstaged.
        GitItemStatus item = new("a.txt");

        List<GitItemStatus> result = StagedListReconciler.ReconcileAfterUnstage(
            unstagedItems: [item],
            currentStagedFiles: [new GitItemStatus("a.txt")],
            currentUnstagedFiles: [],
            _module);

        result.Should().BeEmpty();
    }

    [Test]
    public void AfterUnstage_updates_an_existing_unstaged_entry_in_place()
    {
        GitItemStatus existing = new("a.txt") { IsChanged = false, IsTracked = false, Staged = StagedStatus.WorkTree };
        GitItemStatus unstaged = new("a.txt") { IsChanged = true };

        List<GitItemStatus> result = StagedListReconciler.ReconcileAfterUnstage(
            unstagedItems: [unstaged],
            currentStagedFiles: [],
            currentUnstagedFiles: [existing],
            _module);

        result.Should().Equal(existing);
        existing.IsChanged.Should().BeTrue();
        existing.IsTracked.Should().BeTrue();
        _module.Received().GetSubmoduleCurrentStatus(Arg.Is<IReadOnlyList<GitItemStatus>>(list => list.Single() == existing));
    }

    [Test]
    public void AfterUnstage_appends_a_work_tree_entry_when_none_exists()
    {
        GitItemStatus unstaged = new("a.txt") { IsChanged = true, Staged = StagedStatus.Index };

        List<GitItemStatus> result = StagedListReconciler.ReconcileAfterUnstage(
            unstagedItems: [unstaged],
            currentStagedFiles: [],
            currentUnstagedFiles: [],
            _module);

        result.Should().Equal(unstaged);
        unstaged.Staged.Should().Be(StagedStatus.WorkTree);
        unstaged.IsTracked.Should().BeTrue();
    }

    [Test]
    public void AfterUnstage_new_file_becomes_untracked()
    {
        GitItemStatus added = new("new.txt") { IsNew = true, Staged = StagedStatus.Index };

        List<GitItemStatus> result = StagedListReconciler.ReconcileAfterUnstage(
            unstagedItems: [added],
            currentStagedFiles: [],
            currentUnstagedFiles: [],
            _module);

        result.Should().Equal(added);
        added.IsTracked.Should().BeFalse();
        added.Staged.Should().Be(StagedStatus.WorkTree);
    }

    [Test]
    public void AfterUnstage_rename_splits_into_delete_plus_untracked_new()
    {
        GitItemStatus renamed = new("new.txt") { IsRenamed = true, IsNew = true, OldName = "old.txt", Staged = StagedStatus.Index };

        List<GitItemStatus> result = StagedListReconciler.ReconcileAfterUnstage(
            unstagedItems: [renamed],
            currentStagedFiles: [],
            currentUnstagedFiles: [],
            _module);

        result.Should().HaveCount(2);

        GitItemStatus delete = result[0];
        delete.Name.Should().Be("old.txt");
        delete.IsDeleted.Should().BeTrue();
        delete.IsTracked.Should().BeTrue();
        delete.Staged.Should().Be(StagedStatus.WorkTree);

        result[1].Should().BeSameAs(renamed);
        renamed.IsRenamed.Should().BeFalse();
        renamed.IsNew.Should().BeTrue();
        renamed.IsTracked.Should().BeFalse();
        renamed.OldName.Should().Be(string.Empty);
        renamed.Staged.Should().Be(StagedStatus.WorkTree);
    }

    [Test]
    public void AfterUnstage_inputs_are_not_mutated_as_lists()
    {
        List<GitItemStatus> currentUnstaged = [new GitItemStatus("keep.txt")];

        List<GitItemStatus> result = StagedListReconciler.ReconcileAfterUnstage(
            unstagedItems: [new GitItemStatus("a.txt")],
            currentStagedFiles: [],
            currentUnstagedFiles: currentUnstaged,
            _module);

        result.Should().HaveCount(2);
        currentUnstaged.Should().HaveCount(1);
    }
}
