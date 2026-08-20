using GitCommands.Stash;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;

namespace GitCommandsTests.Stash;

public sealed class StashModelTests
{
    private static readonly ObjectId Head = ObjectId.Parse("3333333333333333333333333333333333333333");
    private static readonly ObjectId StashId = ObjectId.Parse("4444444444444444444444444444444444444444");

    [TestCase("stash@{0}", 1)]
    [TestCase("stash@{4}", 5)]
    [TestCase("stash@{x}", null)]
    [TestCase("garbage", null)]
    [TestCase(null, null)]
    public void Initial_stash_maps_to_a_list_index_past_the_working_dir_item(string? initialStash, int? expected)
    {
        StashSelectionPolicy.ParseInitialIndex(initialStash).Should().Be(expected);
    }

    [Test]
    public void Startup_selection_remembers_a_drop_and_clamps_to_the_shrunken_list()
    {
        StashSelectionPolicy.ResolveStartupSelection(lastSelectedIndex: 2, manageStashes: false, itemCount: 5).Should().Be(2);
        StashSelectionPolicy.ResolveStartupSelection(lastSelectedIndex: 4, manageStashes: false, itemCount: 4).Should().Be(3);
    }

    [Test]
    public void Startup_selection_prefers_the_first_real_stash_in_manage_mode()
    {
        StashSelectionPolicy.ResolveStartupSelection(lastSelectedIndex: -1, manageStashes: true, itemCount: 3).Should().Be(1);
        StashSelectionPolicy.ResolveStartupSelection(lastSelectedIndex: -1, manageStashes: true, itemCount: 1).Should().Be(0);
    }

    [Test]
    public void Startup_selection_falls_back_to_the_working_dir_item()
    {
        StashSelectionPolicy.ResolveStartupSelection(lastSelectedIndex: -1, manageStashes: false, itemCount: 2).Should().Be(0);
        StashSelectionPolicy.ResolveStartupSelection(lastSelectedIndex: -1, manageStashes: false, itemCount: 0).Should().Be(-1);
    }

    [Test]
    public void Navigation_moves_toward_newest_and_stops_at_the_edges()
    {
        StashSelectionPolicy.Navigate(selectedIndex: 2, itemCount: 4, next: true).Should().Be(1);
        StashSelectionPolicy.Navigate(selectedIndex: 2, itemCount: 4, next: false).Should().Be(3);
        StashSelectionPolicy.Navigate(selectedIndex: 0, itemCount: 4, next: true).Should().BeNull();
        StashSelectionPolicy.Navigate(selectedIndex: 3, itemCount: 4, next: false).Should().BeNull();
    }

    [Test]
    public void Working_dir_item_edits_the_message_but_cannot_drop_or_apply()
    {
        StashSelectionCapabilities.Evaluate(isWorkingDirItem: true)
            .Should().Be(new StashSelectionCapabilities(MessageEditable: true, DropAllowed: false, ApplyAllowed: false));
        StashSelectionCapabilities.Evaluate(isWorkingDirItem: false)
            .Should().Be(new StashSelectionCapabilities(MessageEditable: false, DropAllowed: true, ApplyAllowed: true));
    }

    [Test]
    public void Partial_stash_needs_the_working_dir_item_and_selected_files()
    {
        StashSelectionCapabilities.PartialStashAllowed(isWorkingDirItem: true, hasSelectedFiles: true).Should().BeTrue();
        StashSelectionCapabilities.PartialStashAllowed(isWorkingDirItem: true, hasSelectedFiles: false).Should().BeFalse();
        StashSelectionCapabilities.PartialStashAllowed(isWorkingDirItem: false, hasSelectedFiles: true).Should().BeFalse();
    }

    [Test]
    public void Working_dir_diff_splits_into_index_and_worktree_halves()
    {
        StashDiffRevisions revisions = StashDiffRevisions.ForWorkingDir(Head);

        revisions.ShowSplit.Should().BeTrue();
        revisions.First!.ObjectId.Should().Be(Head);
        revisions.SplitIndexRevision!.ObjectId.Should().Be(ObjectId.IndexId);
        revisions.SplitIndexRevision.ParentIds.Should().Equal(Head);
        revisions.Second.ObjectId.Should().Be(ObjectId.WorkTreeId);
        revisions.Second.ParentIds.Should().Equal(ObjectId.IndexId);
    }

    [Test]
    public void Unborn_head_degrades_to_a_single_worktree_diff()
    {
        StashDiffRevisions revisions = StashDiffRevisions.ForWorkingDir(ObjectId.Parse("0000000000000000000000000000000000000000"));

        revisions.ShowSplit.Should().BeFalse();
        revisions.First.Should().BeNull();
        revisions.Second.ObjectId.Should().Be(ObjectId.WorkTreeId);
    }

    [Test]
    public void Stash_diff_pairs_the_stash_with_its_parent()
    {
        StashDiffRevisions revisions = StashDiffRevisions.ForStash(Head, StashId);

        revisions.First!.ObjectId.Should().Be(Head);
        revisions.Second.ObjectId.Should().Be(StashId);
        revisions.Second.ParentIds.Should().Equal(Head);
        revisions.ShowSplit.Should().BeFalse();
    }

    [Test]
    public void Rootless_stash_diffs_without_a_parent()
    {
        StashDiffRevisions revisions = StashDiffRevisions.ForStash(ObjectId.Parse("0000000000000000000000000000000000000000"), StashId);

        revisions.First.Should().BeNull();
        revisions.Second.ParentIds.Should().BeNull();
    }

    [TestCase("fix things", " fix things")]
    [TestCase("  fix things  ", " fix things")]
    [TestCase("   ", "")]
    [TestCase(null, "")]
    public void Save_message_keeps_the_historical_leading_space(string? text, string expected)
    {
        StashSaveMessage.Normalize(text).Should().Be(expected);
    }
}
