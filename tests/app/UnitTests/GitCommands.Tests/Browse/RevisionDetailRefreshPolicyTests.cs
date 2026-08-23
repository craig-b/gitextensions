using GitCommands.Browse;

namespace GitCommandsTests.Browse;

public sealed class RevisionDetailRefreshPolicyTests
{
    [Test]
    public void Panes_fill_only_while_active_and_only_once()
    {
        RevisionDetailRefreshPolicy.ShouldFill(RevisionDetailTarget.DiffList, RevisionDetailTarget.None, RevisionDetailTarget.DiffList)
            .Should().BeTrue();
        RevisionDetailRefreshPolicy.ShouldFill(RevisionDetailTarget.DiffList, RevisionDetailTarget.None, RevisionDetailTarget.FileTree)
            .Should().BeFalse(because: "the diff pane is not active");
        RevisionDetailRefreshPolicy.ShouldFill(RevisionDetailTarget.DiffList, RevisionDetailTarget.DiffList, RevisionDetailTarget.DiffList)
            .Should().BeFalse(because: "it already filled for this selection");
        RevisionDetailRefreshPolicy.ShouldFill(RevisionDetailTarget.FileTree, RevisionDetailTarget.DiffList, RevisionDetailTarget.FileTree)
            .Should().BeTrue(because: "another pane's fill does not block this one");
    }

    [Test]
    public void Commit_info_fills_regardless_of_tab_when_positioned_outside_the_tab_control()
    {
        RevisionDetailRefreshPolicy.ShouldFill(RevisionDetailTarget.CommitInfo, RevisionDetailTarget.None, RevisionDetailTarget.DiffList, commitInfoInTabControl: false)
            .Should().BeTrue();
        RevisionDetailRefreshPolicy.ShouldFill(RevisionDetailTarget.CommitInfo, RevisionDetailTarget.None, RevisionDetailTarget.DiffList, commitInfoInTabControl: true)
            .Should().BeFalse();
        RevisionDetailRefreshPolicy.ShouldFill(RevisionDetailTarget.CommitInfo, RevisionDetailTarget.None, RevisionDetailTarget.CommitInfo, commitInfoInTabControl: true)
            .Should().BeTrue();
        RevisionDetailRefreshPolicy.ShouldFill(RevisionDetailTarget.CommitInfo, RevisionDetailTarget.CommitInfo, RevisionDetailTarget.CommitInfo, commitInfoInTabControl: false)
            .Should().BeFalse(because: "filled is filled, wherever it is positioned");
    }
}
