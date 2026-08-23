using GitCommands.Pull;

namespace GitCommandsTests.Pull;

public sealed class PullModelTests
{
    [TestCase(PullSourceKind.Url, "", PullActionKind.Merge, "", PullGate.BlockNoUrl)]
    [TestCase(PullSourceKind.Remote, "", PullActionKind.Merge, "", PullGate.BlockNoRemote)]
    [TestCase(PullSourceKind.AllRemotes, "", PullActionKind.Fetch, "", PullGate.Proceed)]
    [TestCase(PullSourceKind.Remote, "origin", PullActionKind.Merge, "*", PullGate.BlockStarBranchNeedsFetch)]
    [TestCase(PullSourceKind.Remote, "origin", PullActionKind.Fetch, "*", PullGate.Proceed)]
    [TestCase(PullSourceKind.Remote, "origin", PullActionKind.Rebase, "main", PullGate.Proceed)]
    public void Preflight_gates(PullSourceKind sourceKind, string source, PullActionKind action, string remoteBranch, PullGate expected)
    {
        PullPreflight.Evaluate(sourceKind, source, action, remoteBranch).Should().Be(expected);
    }

    [Test]
    public void Availability_follows_the_action_and_pull_all()
    {
        PullOptionAvailability fetch = PullOptionAvailability.Evaluate(PullActionKind.Fetch, isPullAll: false);
        fetch.LocalBranchAllowed.Should().BeTrue();
        fetch.PruneAllowed.Should().BeTrue();
        fetch.ForceReachableTags.Should().BeFalse();

        PullOptionAvailability merge = PullOptionAvailability.Evaluate(PullActionKind.Merge, isPullAll: false);
        merge.LocalBranchAllowed.Should().BeFalse();
        merge.AllTagsAllowed.Should().BeFalse();
        merge.ForceReachableTags.Should().BeTrue();

        PullOptionAvailability pullAll = PullOptionAvailability.Evaluate(PullActionKind.Fetch, isPullAll: true);
        pullAll.MergeAllowed.Should().BeFalse();
        pullAll.RebaseAllowed.Should().BeFalse();
        pullAll.ForceFetchAction.Should().BeTrue();
    }

    [Test]
    public void Refspec_pull_all_and_detached_head_clear_the_branches()
    {
        PullPreflight.ResolveRefspec(isPullAll: true, "main", isDetachedHead: false, "main", "main", "origin", "origin", isFetch: false)
            .Should().Be(new PullRefspec(null, null));

        PullPreflight.ResolveRefspec(isPullAll: false, "(no branch)", isDetachedHead: true, "", "remote-branch", "origin", null, isFetch: false)
            .Should().Be(new PullRefspec(null, "remote-branch"));
    }

    [Test]
    public void Refspec_current_branch_with_configured_remote_pulls_itself()
    {
        PullPreflight.ResolveRefspec(isPullAll: false, "main", isDetachedHead: false, "main", "main", "origin", "origin", isFetch: false)
            .Should().Be(new PullRefspec("main", "main"));

        PullPreflight.ResolveRefspec(isPullAll: false, "main", isDetachedHead: false, "main", "", "origin", "origin", isFetch: false)
            .Should().Be(new PullRefspec(null, ""), because: "no remote branch and matching remote means a plain pull");
    }

    [Test]
    public void Refspec_prompts_when_pulling_from_a_different_remote_without_a_branch()
    {
        PullRefspec refspec = PullPreflight.ResolveRefspec(
            isPullAll: false, "main", isDetachedHead: false, "main", "", "upstream", "origin", isFetch: false);

        refspec.Prompt.Should().Be(PullRefspecPrompt.ConfirmPullFromDerivedBranch);
        refspec.LocalBranch.Should().Be("main");
        refspec.AcceptPrompt().Should().Be(new PullRefspec("main", "main"));
    }

    [Test]
    public void Refspec_fetch_of_the_current_branch_uses_no_refspec()
    {
        PullPreflight.ResolveRefspec(isPullAll: false, "main", isDetachedHead: false, "main", "", "upstream", "origin", isFetch: true)
            .Should().Be(new PullRefspec(null, ""));

        PullRefspec other = PullPreflight.ResolveRefspec(
            isPullAll: false, "main", isDetachedHead: false, "feature", "", "upstream", "origin", isFetch: true);
        other.Prompt.Should().Be(PullRefspecPrompt.ConfirmFetchFromDerivedBranch);
        other.LocalBranch.Should().Be("feature");
    }

    [Test]
    public void Stash_only_for_real_pulls_of_dirty_non_bare_trees()
    {
        PullPreflight.ShouldStash(PullActionKind.Merge, autoStash: true, isBareRepository: false, dirtyFileCount: 1).Should().BeTrue();
        PullPreflight.ShouldStash(PullActionKind.Fetch, autoStash: true, isBareRepository: false, dirtyFileCount: 1).Should().BeFalse();
        PullPreflight.ShouldStash(PullActionKind.Merge, autoStash: false, isBareRepository: false, dirtyFileCount: 1).Should().BeFalse();
        PullPreflight.ShouldStash(PullActionKind.Merge, autoStash: true, isBareRepository: true, dirtyFileCount: 1).Should().BeFalse();
        PullPreflight.ShouldStash(PullActionKind.Merge, autoStash: true, isBareRepository: false, dirtyFileCount: 0).Should().BeFalse();
    }

    [Test]
    public void Rebase_merge_commit_confirmation_is_remote_rebase_only()
    {
        PullPreflight.ShouldConfirmRebaseMergeCommit(PullActionKind.Rebase, PullSourceKind.Remote, mergeCommitExists: true).Should().BeTrue();
        PullPreflight.ShouldConfirmRebaseMergeCommit(PullActionKind.Rebase, PullSourceKind.Url, mergeCommitExists: true).Should().BeFalse();
        PullPreflight.ShouldConfirmRebaseMergeCommit(PullActionKind.Merge, PullSourceKind.Remote, mergeCommitExists: true).Should().BeFalse();
        PullPreflight.ShouldConfirmRebaseMergeCommit(PullActionKind.Rebase, PullSourceKind.Remote, mergeCommitExists: false).Should().BeFalse();
    }

    [Test]
    public void Ref_removed_rejection_is_detected()
    {
        const string output = """
            Your configuration specifies to merge with the ref 'refs/heads/gone'
            from the remote, but no such ref was fetched.
            """;

        PullRejectionAnalyzer.IsRefRemoved(output).Should().BeTrue();
        PullRejectionAnalyzer.IsRefRemoved("everything up to date").Should().BeFalse();
    }
}
