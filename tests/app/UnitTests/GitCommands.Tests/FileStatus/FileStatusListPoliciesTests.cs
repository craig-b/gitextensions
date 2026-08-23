using GitCommands.Browse;
using GitCommands.FileStatus;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;

namespace GitCommandsTests.FileStatus;

public sealed class FileStatusListPoliciesTests
{
    [Test]
    public void Root_skip_covers_the_three_cases()
    {
        FileStatusRootSkip.ShouldSkip(showDiffGroups: true, rootExpanded: false, rootIsPlaceholderOnly: false, isFileTreeMode: false, hasFilter: false, grepActive: false).Should().BeTrue();
        FileStatusRootSkip.ShouldSkip(false, true, rootIsPlaceholderOnly: true, false, false, false).Should().BeTrue();
        FileStatusRootSkip.ShouldSkip(false, true, false, isFileTreeMode: true, hasFilter: false, grepActive: false).Should().BeTrue();
        FileStatusRootSkip.ShouldSkip(false, true, false, isFileTreeMode: true, hasFilter: true, grepActive: false).Should().BeFalse();
        FileStatusRootSkip.ShouldSkip(false, true, false, false, false, false).Should().BeFalse();
    }

    [Test]
    public void Post_update_selection()
    {
        FileStatusPostUpdate.AfterUpdate(0, false, false, true, 0).Should().Be(PostUpdateSelection.FireEmpty);
        FileStatusPostUpdate.AfterUpdate(1, onlyRootIsLeaf: true, false, true, 0).Should().Be(PostUpdateSelection.SelectOnlyRoot);
        FileStatusPostUpdate.AfterUpdate(3, false, updateCausedByFilter: false, selectFirstOnSetItems: true, 5).Should().Be(PostUpdateSelection.SelectFirstVisible);
        FileStatusPostUpdate.AfterUpdate(3, false, updateCausedByFilter: true, false, previouslySelectedCount: 2).Should().Be(PostUpdateSelection.RestorePrevious);
        FileStatusPostUpdate.AfterUpdate(3, false, updateCausedByFilter: true, false, previouslySelectedCount: 0).Should().Be(PostUpdateSelection.SelectFirstVisible);
    }

    [Test]
    public void Diff_ab_filter_matches_and_applies()
    {
        DiffBranchStatusFilter onlyA = DiffBranchStatusFilter.OnlyAChange;
        DiffAbFilter.Matches(DiffBranchStatus.OnlyAChange, onlyA).Should().BeTrue();
        DiffAbFilter.Matches(DiffBranchStatus.OnlyBChange, onlyA).Should().BeFalse();
        DiffAbFilter.Matches((DiffBranchStatus)999, DiffBranchStatusFilter.None).Should().BeTrue();

        DiffAbFilter.IsApplicable([FileStatusIconNames.Diff, FileStatusIconNames.DiffB]).Should().BeTrue();
        DiffAbFilter.IsApplicable([FileStatusIconNames.Diff]).Should().BeFalse();
    }

    [Test]
    public void Toolbar_state()
    {
        FileStatusToolbarState state = FileStatusToolbarState.Compute(
            canUseGrep: false, hasRevisionRootNode: true, refreshButtonVisible: true, flatList: false, hasGrouping: true, hasDiffAbGroups: true);

        state.Should().Be(new FileStatusToolbarState(
            ShowCollapseGroups: true, ShowRefreshSeparator: true, ShowAsTreeSeparator: true,
            DenseTreeEnabled: true, ShowGroupNodesEnabled: false, ShowDiffAbFilters: true, ShowGrepButton: false));

        FileStatusToolbarState grep = FileStatusToolbarState.Compute(true, false, false, flatList: true, hasGrouping: true, false);
        grep.ShowCollapseGroups.Should().BeTrue();
        grep.DenseTreeEnabled.Should().BeFalse();
        grep.ShowGroupNodesEnabled.Should().BeTrue();
        grep.ShowGrepButton.Should().BeTrue();
    }

    [Test]
    public void Revision_toolbar_state()
    {
        GitRevision worktree = new(ObjectId.WorkTreeId);
        GitRevision normal = new(ObjectId.Parse("aaaa111111111111111111111111111111111111"));

        FileStatusRevisionToolbarState.Compute([worktree]).Should().Be(new FileStatusRevisionToolbarState(RefreshEnabled: true, WorktreeOptionsEnabled: true));
        FileStatusRevisionToolbarState.Compute([new GitRevision(ObjectId.IndexId)]).Should().Be(new FileStatusRevisionToolbarState(RefreshEnabled: true, WorktreeOptionsEnabled: false));
        FileStatusRevisionToolbarState.Compute([normal]).Should().Be(new FileStatusRevisionToolbarState(RefreshEnabled: false, WorktreeOptionsEnabled: false));
    }

    [Test]
    public void Tab_navigation_wraps()
    {
        BrowseTabNavigation.Next(0, 4, forward: true).Should().Be(1);
        BrowseTabNavigation.Next(3, 4, forward: true).Should().Be(0);
        BrowseTabNavigation.Next(0, 4, forward: false).Should().Be(3);
        BrowseTabNavigation.Next(0, 0, forward: true).Should().Be(-1);
    }
}
