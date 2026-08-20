using GitCommands.Push;
using GitExtensions.Extensibility.Git;

namespace GitCommandsTests.Push;

public sealed class PushDialogModelTests
{
    [Test]
    public void Tag_selection_maps_the_sentinel_and_gates_blank_tags()
    {
        TagPushSelection.Parse("[ All ]").Should().Be(new TagPushSelection("", AllTags: true));
        TagPushSelection.Parse("v1.0").Should().Be(new TagPushSelection("v1.0", AllTags: false));
        TagPushSelection.Parse("[ All ]").IsValid.Should().BeTrue();
        TagPushSelection.Parse("v1.0").IsValid.Should().BeTrue();
        TagPushSelection.Parse("  ").IsValid.Should().BeFalse();
    }

    [Test]
    public void Remote_preselection_prefers_explicit_then_branch_setting_then_origin_then_first()
    {
        string[] remotes = ["fork", "origin", "upstream"];

        PushRemoteSelector.Preselect(remotes, "UPSTREAM", null).Should().Be(new RemotePreselection(2, "upstream"));
        PushRemoteSelector.Preselect(remotes, null, "fork").Should().Be(new RemotePreselection(0, "fork"));
        PushRemoteSelector.Preselect(remotes, null, null).Should().Be(new RemotePreselection(1, "origin"));
        PushRemoteSelector.Preselect(["a", "b"], null, "missing").Should().Be(new RemotePreselection(0, "a"));
        PushRemoteSelector.Preselect([], null, null).Should().Be(new RemotePreselection(-1, null));
    }

    [Test]
    public void Destination_split_keeps_the_asymmetric_trim()
    {
        PushDestinationResolver.Resolve(pushToUrl: true, "https://host/repo.git", "ignored")
            .Should().Be(("", "https://host/repo.git"));
        PushDestinationResolver.Resolve(pushToUrl: false, "", " origin ")
            .Should().Be(("origin", " origin "));
    }

    [TestCase(GitPullAction.None, GitPullAction.Merge, false, RejectionFollowUp.GiveUp)]
    [TestCase(GitPullAction.Default, GitPullAction.None, false, RejectionFollowUp.GiveUp)]
    [TestCase(GitPullAction.Default, GitPullAction.Fetch, false, RejectionFollowUp.UnsupportedPullAction)]
    [TestCase(GitPullAction.Merge, GitPullAction.None, false, RejectionFollowUp.PullThenRetry)]
    [TestCase(GitPullAction.Rebase, GitPullAction.None, true, RejectionFollowUp.BlockedByMergeCommit)]
    [TestCase(GitPullAction.Default, GitPullAction.Rebase, false, RejectionFollowUp.PullThenRetry)]
    public void Rejection_follow_up_policy(GitPullAction chosen, GitPullAction configured, bool rebasingMergeCommit, RejectionFollowUp expected)
    {
        PushRejectionPolicy.Decide(chosen, configured, rebasingMergeCommit).Should().Be(expected);
    }
}
