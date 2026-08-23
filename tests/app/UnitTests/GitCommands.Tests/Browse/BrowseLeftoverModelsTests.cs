using GitCommands;
using GitCommands.Browse;
using GitCommands.FileStatus;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;

namespace GitCommandsTests.Browse;

public sealed class BrowseLeftoverModelsTests
{
    [Test]
    public void Repo_capabilities_follow_the_state()
    {
        RepoUiCapabilities.For(new RepoUiState(HasWorkingDir: true, IsValidWorkingDir: true, IsBare: false, IsDashboardVisible: false))
            .Should().Be(new RepoUiCapabilities(ValidBrowseDir: true, CanLevelUp: true, CanCommit: true, CanShowDetailTabs: true, CanShowStashCount: true));

        RepoUiCapabilities bare = RepoUiCapabilities.For(new RepoUiState(true, true, IsBare: true, false));
        bare.CanLevelUp.Should().BeFalse();
        bare.CanCommit.Should().BeFalse();
        bare.CanShowStashCount.Should().BeFalse();
        bare.ValidBrowseDir.Should().BeTrue();

        RepoUiCapabilities dashboard = RepoUiCapabilities.For(new RepoUiState(true, true, false, IsDashboardVisible: true));
        dashboard.ValidBrowseDir.Should().BeFalse();
        dashboard.CanCommit.Should().BeFalse();
    }

    [Test]
    public void Revision_command_availability()
    {
        GitRevision normal = new(ObjectId.Parse("aaaa111111111111111111111111111111111111"));
        GitRevision artificial = new(ObjectId.WorkTreeId);

        RevisionCommandAvailability single = RevisionCommandAvailability.For(isBare: false, [normal]);
        single.SingleNormalCommit.Should().BeTrue();
        single.CanBranchCheckoutMergeCherryPickBisect.Should().BeTrue();
        single.CanRebase.Should().BeTrue();
        single.CanTagOrArchive.Should().BeTrue();

        RevisionCommandAvailability.For(isBare: false, [normal, normal]).CanRebase.Should().BeTrue();
        RevisionCommandAvailability.For(isBare: false, [artificial]).SingleNormalCommit.Should().BeFalse();
        RevisionCommandAvailability.For(isBare: false, [normal, artificial]).CanRebase.Should().BeFalse();
        RevisionCommandAvailability.For(isBare: true, [normal]).CanBranchCheckoutMergeCherryPickBisect.Should().BeFalse();
        RevisionCommandAvailability.For(isBare: true, [normal]).CanTagOrArchive.Should().BeTrue();
        RevisionCommandAvailability.For(isBare: true, [normal]).CanRepositoryCommands.Should().BeFalse();
    }

    [Test]
    public void Commit_info_layout_table()
    {
        CommitInfoLayout below = CommitInfoLayoutTable.For(CommitInfoPosition.BelowList);
        below.Should().Be(new CommitInfoLayout(
            CommitInfoInTabControl: true, RevisionPane.CommitInfoTab, RevisionPane.SplitPanel1, FixFirstPanel: false, DetailPanelCollapsed: true));

        CommitInfoLayoutTable.For(CommitInfoPosition.RightwardFromList).RevisionInfoPane.Should().Be(RevisionPane.SplitPanel2);
        CommitInfoLayoutTable.For(CommitInfoPosition.LeftwardFromList).FixFirstPanel.Should().BeTrue();

        CommitInfoLayoutTable.Next(CommitInfoPosition.BelowList).Should().Be(CommitInfoPosition.LeftwardFromList);
        CommitInfoLayoutTable.Next(CommitInfoPosition.RightwardFromList).Should().Be(CommitInfoPosition.BelowList);

        CommitInfoLayoutTable.SplitterDistance(CommitInfoPosition.RightwardFromList, 1000, 490).Should().Be(510);
        CommitInfoLayoutTable.SplitterDistance(CommitInfoPosition.RightwardFromList, 300, 490).Should().Be(0);
        CommitInfoLayoutTable.SplitterDistance(CommitInfoPosition.LeftwardFromList, 1000, 490).Should().Be(490);
    }

    [Test]
    public void Sort_type_composes_and_decomposes()
    {
        foreach (DiffListSortType sortType in Enum.GetValues<DiffListSortType>())
        {
            (DiffListGrouping grouping, bool flat) = DiffListSortLayout.Decompose(sortType);
            DiffListSortLayout.Compose(grouping, flat).Should().Be(sortType);
        }

        DiffListSortLayout.Decompose(DiffListSortType.FileExtensionFlat).Should().Be((DiffListGrouping.FileExtension, true));
    }

    [Test]
    public void Mru_keep_position_never_promotes_and_caps()
    {
        ComboMruHistory.Add(["a", "b"], "b", 10, MruDedupe.KeepPosition).Changed.Should().BeFalse();
        MruUpdate added = ComboMruHistory.Add(["a", "b"], "c", 10, MruDedupe.KeepPosition);
        added.Items.Should().Equal("c", "a", "b");

        string[] full = [.. Enumerable.Range(0, 10).Select(i => $"i{i}")];
        MruUpdate evicted = ComboMruHistory.Add(full, "new", 10, MruDedupe.KeepPosition);
        evicted.Items.Should().HaveCount(10);
        evicted.Items[0].Should().Be("new");
        evicted.Items.Should().NotContain("i9");
    }

    [Test]
    public void Mru_move_to_front_promotes_existing()
    {
        ComboMruHistory.Add(["a", "b", "c"], "b", 30, MruDedupe.MoveToFront).Items.Should().Equal("b", "a", "c");
        ComboMruHistory.Add(["a", "b"], "a", 30, MruDedupe.MoveToFront).Changed.Should().BeFalse();
    }

    [Test]
    public void Worktree_menu_marks_current_and_deleted()
    {
        GitWorktree main = new("/repo/main", GitWorktreeHeadType.Branch, "0000000000000000000000000000000000000000", "main", IsDeleted: false);
        GitWorktree other = new("/repo/other/", GitWorktreeHeadType.Branch, "0000000000000000000000000000000000000000", "b", IsDeleted: false);
        GitWorktree stale = new("/repo/stale", GitWorktreeHeadType.Branch, "0000000000000000000000000000000000000000", "c", IsDeleted: true);

        IReadOnlyList<WorktreeMenuEntry> entries = WorktreeMenuModel.Build([main, other, stale], "/repo/main/", pathsCaseInsensitive: false);

        entries[0].IsCurrent.Should().BeTrue();
        entries[0].Enabled.Should().BeFalse();
        entries[1].IsCurrent.Should().BeFalse();
        entries[1].Enabled.Should().BeTrue();
        entries[2].Enabled.Should().BeFalse();

        WorktreeMenuModel.IsToolbarVisible(isValidWorkingDir: true, worktreeCount: 2).Should().BeTrue();
        WorktreeMenuModel.IsToolbarVisible(isValidWorkingDir: true, worktreeCount: 1).Should().BeFalse();
        WorktreeMenuModel.MainWorktreePath([main, other], "/wd").Should().Be("/repo/main");
        WorktreeMenuModel.MainWorktreePath([], "/wd").Should().Be("/wd");
    }
}
