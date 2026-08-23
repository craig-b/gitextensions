using GitCommands.Reset;
using GitExtensions.Extensibility.Git;
using NSubstitute;

namespace GitCommandsTests.Reset;

public sealed class ResetAnotherBranchModelTests
{
    private static readonly ObjectId Target = ObjectId.Parse("aaaa111111111111111111111111111111111111");
    private static readonly ObjectId Elsewhere = ObjectId.Parse("bbbb222222222222222222222222222222222222");

    private static IGitRef LocalRef(string name, ObjectId objectId, IGitRef? tracks = null)
    {
        IGitRef gitRef = Substitute.For<IGitRef>();
        gitRef.Name.Returns(name);
        gitRef.LocalName.Returns(name);
        gitRef.IsHead.Returns(true);
        gitRef.ObjectId.Returns(objectId);
        gitRef.IsTrackingRemote(Arg.Any<IGitRef?>()).Returns(call => tracks is not null && ReferenceEquals(call.Arg<IGitRef?>(), tracks));
        return gitRef;
    }

    private static IGitRef RemoteRef(string localName)
    {
        IGitRef gitRef = Substitute.For<IGitRef>();
        gitRef.IsRemote.Returns(true);
        gitRef.LocalName.Returns(localName);
        gitRef.Name.Returns($"origin/{localName}");
        return gitRef;
    }

    [Test]
    public void Candidates_exclude_the_current_branch_and_branches_already_at_the_revision()
    {
        IGitRef current = LocalRef("main", Elsewhere);
        IGitRef atRevision = LocalRef("done", Target);
        IGitRef other = LocalRef("feature", Elsewhere);

        IReadOnlyList<IGitRef> candidates = ResetAnotherBranchCandidates.Build(
            [current, atRevision, other], [], currentBranch: "main", Target);

        candidates.Should().Equal(other);
    }

    [Test]
    public void Detached_head_keeps_every_branch()
    {
        IGitRef main = LocalRef("main", Elsewhere);

        ResetAnotherBranchCandidates.Build([main], [], currentBranch: "(no branch)", Target)
            .Should().Equal(main);
    }

    [Test]
    public void Branches_tracking_the_revision_remotes_come_first()
    {
        IGitRef remote = RemoteRef("feature");
        IGitRef unrelated = LocalRef("zoo", Elsewhere);
        IGitRef tracking = LocalRef("feature", Elsewhere, tracks: remote);

        IReadOnlyList<IGitRef> candidates = ResetAnotherBranchCandidates.Build(
            [unrelated, tracking], [remote], currentBranch: "main", Target);

        candidates.Should().Equal(tracking, unrelated);
    }

    [Test]
    public void Default_is_the_single_candidate_matching_the_single_remote()
    {
        IGitRef remote = RemoteRef("feature");
        IGitRef tracking = LocalRef("feature", Elsewhere, tracks: remote);
        IGitRef other = LocalRef("zoo", Elsewhere);

        ResetAnotherBranchCandidates.ResolveDefault([tracking, other], [remote]).Should().Be("feature");
        ResetAnotherBranchCandidates.ResolveDefault([tracking, other], []).Should().BeNull();
        ResetAnotherBranchCandidates.ResolveDefault([tracking, LocalRef("feature", Elsewhere, tracks: remote)], [remote]).Should().BeNull();
    }
}
