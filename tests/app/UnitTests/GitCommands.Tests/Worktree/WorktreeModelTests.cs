using GitCommands.Worktree;
using GitExtensions.Extensibility.Git;

namespace GitCommandsTests.Worktree;

public sealed class WorktreeModelTests
{
    private static GitWorktree Worktree(string path, bool isDeleted = false)
        => new(path, GitWorktreeHeadType.Branch, Sha1: "0000000000000000000000000000000000000000", Branch: "b", IsDeleted: isDeleted);

    [Test]
    public void New_branch_option_switches_forms()
    {
        WorktreeCreateModel.NewBranchOption(createNewBranch: true, "feature", "ignored").Should().Be("-b feature");
        WorktreeCreateModel.NewBranchOption(createNewBranch: false, "ignored", "main").Should().Be("main");
    }

    [Test]
    public void Suggested_directory_folds_invalid_characters()
    {
        WorktreeCreateModel.SuggestDirectory("/repo/work", "feature/x").Should().Be("/repo/work_feature/x".Replace("feature/x", WorktreeCreateModel.NormalizeBranchName("feature/x")));
        WorktreeCreateModel.NormalizeBranchName("a/b").Should().NotContain("/");
        WorktreeCreateModel.NormalizeBranchName("name.").Should().Be("name");
    }

    [Test]
    public void Branch_choice_validity()
    {
        WorktreeCreateModel.IsBranchChoiceValid(checkoutExistingBranch: true, hasSelectedExisting: true, "", ["main"]).Should().BeTrue();
        WorktreeCreateModel.IsBranchChoiceValid(checkoutExistingBranch: true, hasSelectedExisting: false, "", ["main"]).Should().BeFalse();
        WorktreeCreateModel.IsBranchChoiceValid(checkoutExistingBranch: false, hasSelectedExisting: false, "new", ["main"]).Should().BeTrue();
        WorktreeCreateModel.IsBranchChoiceValid(checkoutExistingBranch: false, hasSelectedExisting: false, "main", ["main"]).Should().BeFalse();
        WorktreeCreateModel.IsBranchChoiceValid(checkoutExistingBranch: false, hasSelectedExisting: false, "  ", ["main"]).Should().BeFalse();
    }

    [Test]
    public void Create_command_seeds_relative_paths_only_when_unset()
    {
        WorktreeCreateModel.CreateCommand(_ => null, "\"../wt\"", "-b feature").ToString()
            .Should().Contain("worktree.useRelativePaths=true").And.Contain("add").And.Contain("-b feature");
        WorktreeCreateModel.CreateCommand(_ => "false", "\"../wt\"", "-b feature").ToString()
            .Should().NotContain("worktree.useRelativePaths=true");
    }

    [Test]
    public void Manage_policy_blocks_deleted_current_and_main_rows()
    {
        string current = Path.Combine(Path.GetTempPath(), "wt-current");
        GitWorktree[] worktrees = [Worktree(current), Worktree(Path.Combine(Path.GetTempPath(), "wt-other")), Worktree(Path.Combine(Path.GetTempPath(), "wt-stale"), isDeleted: true)];

        WorktreeManagePolicy.CanActOn(worktrees, 1, current).Should().BeTrue();
        WorktreeManagePolicy.CanActOn(worktrees, 0, current).Should().BeFalse();
        WorktreeManagePolicy.CanActOn(worktrees, 2, current).Should().BeFalse();
        WorktreeManagePolicy.CanDelete(worktrees, 1, current).Should().BeTrue();
        WorktreeManagePolicy.CanDelete(worktrees, 0, Path.Combine(Path.GetTempPath(), "elsewhere")).Should().BeFalse();
        WorktreeManagePolicy.CanPrune(worktrees).Should().BeTrue();
        WorktreeManagePolicy.CanPrune([worktrees[0], worktrees[1]]).Should().BeFalse();
    }

    [Test]
    public void Submodule_add_validation_and_ls_remote_parsing()
    {
        SubmoduleAddModel.IsValid("url", "path").Should().BeTrue();
        SubmoduleAddModel.IsValid("", "path").Should().BeFalse();

        string output = "aaa\trefs/heads/main\nbbb\trefs/heads/feature/x\nnoise line\n";
        SubmoduleAddModel.ParseLsRemoteHeads(output).Should().Equal("main", "feature/x");
    }
}
