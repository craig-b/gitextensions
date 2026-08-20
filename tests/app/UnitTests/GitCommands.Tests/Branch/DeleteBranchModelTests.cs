using GitCommands.Branch;
using GitExtensions.Extensibility.Git;
using NSubstitute;

namespace GitCommandsTests.Branch;

public sealed class DeleteBranchModelTests
{
    private static IGitRef Ref(string name)
    {
        IGitRef gitRef = Substitute.For<IGitRef>();
        gitRef.Name.Returns(name);
        return gitRef;
    }

    private static GitWorktree Worktree(string path, string? branch, bool isDeleted = false)
        => new(path, GitWorktreeHeadType.Branch, Sha1: "0000000000000000000000000000000000000000", branch, isDeleted);

    [Test]
    public void Merged_branch_scan_separates_the_current_branch()
    {
        MergedBranchScan scan = MergedBranchScan.Parse(["  main", "* feature", "  release/1.0"]);

        scan.CurrentBranch.Should().Be("feature");
        scan.MergedBranches.Should().BeEquivalentTo(["main", "release/1.0"]);
    }

    [Test]
    public void Preflight_blocks_the_current_branch_and_empty_selection()
    {
        DeleteBranchPreflight.Evaluate([], "main").Should().Be(DeleteBranchGate.NoSelection);
        DeleteBranchPreflight.Evaluate([Ref("main")], "main").Should().Be(DeleteBranchGate.BlockCurrentBranch);
        DeleteBranchPreflight.Evaluate([Ref("feature")], "main").Should().Be(DeleteBranchGate.Proceed);
    }

    [Test]
    public void Merged_branch_scan_is_skipped_when_the_warning_is_suppressed()
    {
        DeleteBranchPreflight.NeedsMergedBranchScan(dontConfirmSetting: true).Should().BeFalse();
        DeleteBranchPreflight.NeedsMergedBranchScan(dontConfirmSetting: false).Should().BeTrue();
    }

    [Test]
    public void Unmerged_confirmation_covers_detached_head_and_unmerged_selection()
    {
        HashSet<string> merged = ["merged"];

        DeleteBranchPreflight.ShouldConfirmUnmerged(false, "main", [Ref("merged")], merged).Should().BeFalse();
        DeleteBranchPreflight.ShouldConfirmUnmerged(false, "main", [Ref("unmerged")], merged).Should().BeTrue();
        DeleteBranchPreflight.ShouldConfirmUnmerged(false, currentBranch: null, [Ref("merged")], merged).Should().BeTrue();
        DeleteBranchPreflight.ShouldConfirmUnmerged(false, "(no branch)", [Ref("merged")], merged).Should().BeTrue();
        DeleteBranchPreflight.ShouldConfirmUnmerged(dontConfirmSetting: true, currentBranch: null, [Ref("unmerged")], merged).Should().BeFalse();
    }

    [Test]
    public void Classification_splits_main_linked_and_deleted_worktrees()
    {
        IGitRef inMain = Ref("in-main");
        IGitRef inLinked = Ref("in-linked");
        IGitRef inStale = Ref("in-stale");
        IGitRef unrelated = Ref("unrelated");

        WorktreeBranchClassification classification = WorktreeBranchClassification.Classify(
            [inMain, inLinked, inStale, unrelated],
            [
                Worktree("/repo/main", "in-main"),
                Worktree("/repo/linked", "in-linked"),
                Worktree("/repo/stale", "in-stale", isDeleted: true),
                Worktree("/repo/other", "elsewhere"),
            ],
            currentWorkingDir: "/repo/current");

        classification.HasDeletedWorktrees.Should().BeTrue();
        classification.MainWorktreeBranches.Should().ContainSingle().Which.Branch.Should().Be(inMain);
        classification.LinkedWorktreeBranches.Should().ContainSingle().Which.Branch.Should().Be(inLinked);
    }

    [Test]
    public void Current_working_directory_worktree_is_exempt()
    {
        IGitRef branch = Ref("here");

        WorktreeBranchClassification classification = WorktreeBranchClassification.Classify(
            [branch],
            [Worktree("/repo/current/", "here")],
            currentWorkingDir: "/repo/current");

        classification.MainWorktreeBranches.Should().BeEmpty();
        classification.LinkedWorktreeBranches.Should().BeEmpty();
        classification.HasDeletedWorktrees.Should().BeFalse();
    }

    [Test]
    public void Detached_worktrees_do_not_match_any_branch()
    {
        WorktreeBranchClassification classification = WorktreeBranchClassification.Classify(
            [Ref("feature")],
            [new GitWorktree("/repo/detached", GitWorktreeHeadType.Detached, Sha1: "0000000000000000000000000000000000000000", Branch: null, IsDeleted: false)],
            currentWorkingDir: "/repo/current");

        classification.MainWorktreeBranches.Should().BeEmpty();
        classification.LinkedWorktreeBranches.Should().BeEmpty();
    }
}
