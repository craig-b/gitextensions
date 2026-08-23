using GitCommands.Git;
using GitCommands.Push;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;
using NSubstitute;

namespace GitCommandsTests.Push;

public sealed class PushModelTests
{
    private static IGitRef Ref(string name, string trackingRemote = "", string mergeWith = "")
    {
        IGitRef gitRef = Substitute.For<IGitRef>();
        gitRef.Name.Returns(name);
        gitRef.TrackingRemote.Returns(trackingRemote);
        gitRef.MergeWith.Returns(mergeWith);
        return gitRef;
    }

    [TestCase(true, false, false, ForcePushOptions.Force)]
    [TestCase(false, true, false, ForcePushOptions.Force, TestName = "tags cannot use force-with-lease")]
    [TestCase(false, true, true, ForcePushOptions.Force)]
    [TestCase(false, false, true, ForcePushOptions.ForceWithLease)]
    [TestCase(false, false, false, ForcePushOptions.DoNotForce)]
    public void Force_option_resolves(bool branches, bool tags, bool lease, ForcePushOptions expected)
    {
        PushPreflight.ResolveForceOption(branches, tags, lease).Should().Be(expected);
    }

    [Test]
    public void Remote_branch_prefers_the_configured_push_refspec()
    {
        PushPreflight.ResolveRemoteBranch("main", pushToRemote: true, Ref("main"), "origin", defaultPushRemote: "trunk", remotePrefix: "")
            .Should().Be("trunk");
    }

    [Test]
    public void Remote_branch_falls_back_to_the_tracked_branch_then_a_new_name()
    {
        PushPreflight.ResolveRemoteBranch("main", pushToRemote: true, Ref("main", trackingRemote: "origin", mergeWith: "main"), "origin", defaultPushRemote: null, remotePrefix: "")
            .Should().Be("main");

        PushPreflight.ResolveRemoteBranch("feature", pushToRemote: true, Ref("feature", trackingRemote: "upstream", mergeWith: "feature"), "origin", defaultPushRemote: null, remotePrefix: "")
            .Should().Be("feature", because: "an untracked-for-this-remote branch pushes to a same-named new branch");

        PushPreflight.ResolveRemoteBranch("main", pushToRemote: true, Ref("main", trackingRemote: "origin", mergeWith: ""), "origin", defaultPushRemote: null, remotePrefix: "")
            .Should().Be("main", because: "an empty tracked merge target falls through to the new-name rule");

        PushPreflight.ResolveRemoteBranch("main", pushToRemote: false, Ref("main", "origin", "other"), "origin", "trunk", remotePrefix: "")
            .Should().Be("main", because: "URL pushes never consult remote config");
    }

    [Test]
    public void Tracking_decision_derives_only_for_untracked_non_remote_like_branches()
    {
        string[] remotes = ["origin", "upstream"];

        PushPreflight.EvaluateTracking(replaceTrackingRequested: true, "main", "main", Ref("main"), remotes, autoSetupMergeSetting: null, dontConfirmSetting: false)
            .Should().Be(TrackingRefDecision.TrackSilently);

        PushPreflight.EvaluateTracking(false, "main", "", Ref("main"), remotes, null, false)
            .Should().Be(TrackingRefDecision.NoTrack, because: "no remote branch, nothing to track");

        PushPreflight.EvaluateTracking(false, "main", "main", Ref("main", trackingRemote: ""), remotes, null, false)
            .Should().Be(TrackingRefDecision.ConfirmTrack);

        PushPreflight.EvaluateTracking(false, "main", "main", Ref("main", trackingRemote: ""), remotes, null, dontConfirmSetting: true)
            .Should().Be(TrackingRefDecision.TrackSilently);

        PushPreflight.EvaluateTracking(false, "main", "main", Ref("main", trackingRemote: "origin"), remotes, null, false)
            .Should().Be(TrackingRefDecision.NoTrack, because: "already tracking");

        PushPreflight.EvaluateTracking(false, "origin_main", "main", Ref("origin_main", ""), remotes, null, false)
            .Should().Be(TrackingRefDecision.NoTrack, because: "a branch named like a remote is not auto-tracked");

        PushPreflight.EvaluateTracking(false, "main", "main", Ref("main", ""), remotes, autoSetupMergeSetting: "FALSE", false)
            .Should().Be(TrackingRefDecision.NoTrack, because: "branch.autosetupmerge=false vetoes");
    }

    [Test]
    public void Rejection_detects_the_current_branch()
    {
        const string output = "some noise\n ! [rejected]        main -> main (non-fast-forward)\n";

        PushRejectionAnalyzer.PushRejection current = PushRejectionAnalyzer.Analyze(output, "main");
        current.IsRejected.Should().BeTrue();
        current.IsCurrentBranch.Should().BeTrue();

        PushRejectionAnalyzer.PushRejection other = PushRejectionAnalyzer.Analyze(output, "feature");
        other.IsRejected.Should().BeTrue();
        other.IsCurrentBranch.Should().BeFalse();

        PushRejectionAnalyzer.Analyze("Everything up-to-date", "main").IsRejected.Should().BeFalse();
    }

    [Test]
    public void Force_with_lease_splices_after_the_push_command_only_when_not_forced()
    {
        PushRejectionAnalyzer.WithForceWithLease("push \"origin\" main:main")
            .Should().Be("push --force-with-lease \"origin\" main:main");

        PushRejectionAnalyzer.WithForceWithLease("wsl git push \"origin\" main")
            .Should().Be("wsl git push --force-with-lease \"origin\" main");

        PushRejectionAnalyzer.WithForceWithLease("push --force \"origin\" main").Should().BeNull();
        PushRejectionAnalyzer.WithForceWithLease("push -f \"origin\" main").Should().BeNull(because: "-f mid-line is already forced");
        PushRejectionAnalyzer.WithForceWithLease("fetch origin").Should().BeNull();
    }
}
