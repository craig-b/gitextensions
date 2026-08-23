using GitCommands.Commit;
using GitExtensions.Extensibility.Git;
using NSubstitute;

namespace GitCommandsTests.Commit;

public class BranchPushTargetTests
{
    private static IGitRef Branch(string? trackingRemote, string? mergeWith)
    {
        IGitRef gitRef = Substitute.For<IGitRef>();
        gitRef.TrackingRemote.Returns(trackingRemote);
        gitRef.MergeWith.Returns(mergeWith);
        return gitRef;
    }

    [Test]
    public void Tracked_branch_pushes_to_its_tracking_remote()
    {
        BranchPushTarget.Resolve(Branch("upstream", "main"), ["origin", "upstream"], "main")
            .Should().Be(new BranchPushTarget(PushTargetKind.Tracked, "upstream/main"));
    }

    [TestCase(null, "main")]
    [TestCase("", "main")]
    [TestCase("origin", null)]
    [TestCase("origin", "")]
    public void Partial_tracking_config_counts_as_untracked(string? trackingRemote, string? mergeWith)
    {
        BranchPushTarget.Resolve(Branch(trackingRemote, mergeWith), ["origin"], "feature")
            .Should().Be(new BranchPushTarget(PushTargetKind.DefaultRemoteUntracked, "origin/feature"));
    }

    [Test]
    public void Untracked_branch_prefers_origin_over_alphabetical_order()
    {
        BranchPushTarget.Resolve(Branch(null, null), ["zzz", "origin", "aaa"], "feature")
            .Should().Be(new BranchPushTarget(PushTargetKind.DefaultRemoteUntracked, "origin/feature"));
    }

    [Test]
    public void Untracked_branch_falls_back_to_the_alphabetically_first_remote()
    {
        BranchPushTarget.Resolve(Branch(null, null), ["zzz", "aaa"], "feature")
            .Should().Be(new BranchPushTarget(PushTargetKind.DefaultRemoteUntracked, "aaa/feature"));
    }

    [Test]
    public void No_remotes_configured()
    {
        BranchPushTarget.Resolve(Branch(null, null), [], "feature")
            .Should().Be(new BranchPushTarget(PushTargetKind.NoRemoteConfigured, null));
    }

    [Test]
    public void Null_branch_resolves_like_untracked()
    {
        BranchPushTarget.Resolve(currentBranch: null, ["origin"], "feature")
            .Should().Be(new BranchPushTarget(PushTargetKind.DefaultRemoteUntracked, "origin/feature"));
    }
}
