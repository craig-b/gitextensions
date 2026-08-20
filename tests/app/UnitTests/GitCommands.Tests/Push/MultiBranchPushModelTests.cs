using GitCommands;
using GitCommands.Git;
using GitCommands.Push;
using GitExtensions.Extensibility.Git;
using NSubstitute;

namespace GitCommandsTests.Push;

public sealed class MultiBranchPushModelTests
{
    private static readonly ObjectId IdA = ObjectId.Parse("aaaa111111111111111111111111111111111111");
    private static readonly ObjectId IdB = ObjectId.Parse("bbbb222222222222222222222222222222222222");

    private static IGitRef Head(string name, ObjectId objectId, string remote = "", string? mergeWith = null)
    {
        IGitRef gitRef = Substitute.For<IGitRef>();
        gitRef.Name.Returns(name);
        gitRef.LocalName.Returns(name);
        gitRef.ObjectId.Returns(objectId);
        gitRef.Remote.Returns(remote);
        gitRef.MergeWith.Returns(mergeWith);
        return gitRef;
    }

    [TestCase("aaaa111111111111111111111111111111111111\trefs/heads/main\n", true)]
    public void Ls_remote_output_cleaning(string output, bool unchanged)
    {
        MultiBranchPushModel.CleanLsRemoteOutput(output).Should().Be(output);
        MultiBranchPushModel.CleanLsRemoteOutput("noise" + output).Should().Be(output);
        MultiBranchPushModel.CleanLsRemoteOutput("short\tx").Should().Be("");
    }

    [Test]
    public void Rows_pair_local_heads_with_the_remote_and_compare_ids()
    {
        IGitRef localSame = Head("same", IdA, remote: "origin", mergeWith: "same");
        IGitRef localDiffers = Head("differs", IdA, remote: "origin", mergeWith: "differs");
        IGitRef localUnknown = Head("only-local", IdA);
        IGitRef remoteSame = Head("same", IdA);
        IGitRef remoteDiffers = Head("differs", IdB);
        IGitRef remoteOnly = Head("only-remote", IdB);

        IReadOnlyList<MultiBranchRow> rows = MultiBranchPushModel.BuildRows(
            [localSame, localDiffers, localUnknown],
            [remoteSame, remoteDiffers, remoteOnly],
            "origin",
            aheadBehindData: null);

        rows.Should().HaveCount(4);
        rows[0].Should().Be(new MultiBranchRow("same", "same", "="));
        rows[1].Should().Be(new MultiBranchRow("differs", "differs", "<>"));
        rows[2].Should().Be(new MultiBranchRow("only-local", "", ""));
        rows[3].Should().Be(new MultiBranchRow(null, "only-remote", ""));
        rows[3].IsDeleteOnly.Should().BeTrue();
    }

    [Test]
    public void Ahead_behind_data_wins_the_remote_cell_and_display()
    {
        IGitRef local = Head("main", IdA, remote: "origin", mergeWith: "main");
        Dictionary<string, AheadBehindData> aheadBehind = new()
        {
            ["main"] = new AheadBehindData("main", "refs/remotes/origin/main", "2", "1"),
        };

        IReadOnlyList<MultiBranchRow> rows = MultiBranchPushModel.BuildRows([local], [], "origin", aheadBehind);

        rows[0].RemoteBranch.Should().Be("main");
        rows[0].AheadBehindDisplay.Should().NotBeEmpty();
    }

    [Test]
    public void Actions_fall_back_to_the_local_name_and_skip_blank_rows()
    {
        MultiBranchRow push = new("feature", "", "", MultiBranchAction.Push);
        MultiBranchRow force = new("main", "main-remote", "", MultiBranchAction.Force);
        MultiBranchRow delete = new(null, "gone", "", MultiBranchAction.Delete);
        MultiBranchRow none = new("idle", "idle", "", MultiBranchAction.None);
        MultiBranchRow blank = new(null, "", "", MultiBranchAction.Delete);

        IReadOnlyList<GitPushAction> actions = MultiBranchPushModel.ToPushActions([push, force, delete, none, blank]);

        actions.Should().HaveCount(3);
        actions[0].ToString().Should().Contain("feature");
        actions[1].ToString().Should().Contain("+").And.Contain("main-remote");
        actions[2].ToString().Should().Contain(":").And.Contain("gone");
    }

    [Test]
    public void Delete_only_rows_refuse_push_and_force()
    {
        MultiBranchRow deleteOnly = new(null, "gone", "");

        deleteOnly.With(MultiBranchAction.Push).Action.Should().Be(MultiBranchAction.None);
        deleteOnly.With(MultiBranchAction.Delete).Action.Should().Be(MultiBranchAction.Delete);
        new MultiBranchRow("local", "remote", "").With(MultiBranchAction.Force).Action.Should().Be(MultiBranchAction.Force);
    }
}
