using GitCommands.Refs;

namespace GitCommandsTests.Refs;

public sealed class BatchRefOperationsTests
{
    private const char Us = '\u001f';

    private static BatchRefRow Branch(string name, bool merged = true, bool current = false, string? upstream = null)
        => new(name, BatchRefKind.LocalBranch, null, current, upstream, "subject", merged);

    private static BatchRefRow Tag(string name)
        => new(name, BatchRefKind.Tag, null, false, null, "subject", null);

    [Test]
    public void ParseRows_reads_the_for_each_ref_fields()
    {
        string output =
            $"refs/heads/feature/one{Us}aaaa567890aaaa567890aaaa567890aaaa567890{Us}origin/feature/one{Us}*{Us}the subject\n" +
            $"refs/tags/v1.0{Us}bbbb567890bbbb567890bbbb567890bbbb567890{Us}{Us}{Us}tagged\n" +
            $"refs/remotes/origin/main{Us}cccc567890cccc567890cccc567890cccc567890{Us}{Us}{Us}remote tip\n";

        IReadOnlyList<BatchRefRow> rows = BatchRefOperations.ParseRows(output, isMergedIntoCurrent: name => name == "feature/one");

        rows.Should().HaveCount(3);
        rows[0].Should().Be(new BatchRefRow(
            "feature/one",
            BatchRefKind.LocalBranch,
            GitExtensions.Extensibility.Git.ObjectId.Parse("aaaa567890aaaa567890aaaa567890aaaa567890"),
            IsCurrent: true,
            Upstream: "origin/feature/one",
            Subject: "the subject",
            MergedIntoCurrent: true));
        rows[1].Kind.Should().Be(BatchRefKind.Tag);
        rows[1].MergedIntoCurrent.Should().BeNull();
        rows[2].Kind.Should().Be(BatchRefKind.RemoteBranch);
        rows[2].Name.Should().Be("origin/main");
    }

    [Test]
    public void Partition_skips_inapplicable_rows_instead_of_erroring()
    {
        BatchRefRow current = Branch("main", current: true);
        BatchRefRow remote = new("origin/main", BatchRefKind.RemoteBranch, null, false, null, null, null);

        var (applicable, skipped) = BatchRefOperations.Partition(
            BatchRefVerb.Delete, [Branch("feature/a"), Tag("v1"), current, remote]);

        applicable.Select(row => row.Name).Should().Equal("feature/a", "v1");
        skipped.Select(row => row.Name).Should().Equal("main", "origin/main");
    }

    [Test]
    public void Force_gate_arms_on_unmerged_branches_only()
    {
        BatchRefOperations.RequiresForceDelete([Branch("a", merged: true), Tag("v1")]).Should().BeFalse();
        BatchRefOperations.RequiresForceDelete([Branch("a", merged: false)]).Should().BeTrue();
    }

    [Test]
    public void Delete_builds_git_native_list_commands()
    {
        IReadOnlyList<BatchRefCommand> commands = BatchRefOperations.BuildDeleteCommands(
            [Branch("feature/a", upstream: "origin/feature/a"), Branch("feature/b"), Tag("v1")],
            force: false,
            deleteRemoteCounterparts: true);

        commands.Should().HaveCount(3);
        commands[0].Arguments.ToString().Should().Be("branch -d \"feature/a\" \"feature/b\"");
        commands[1].Arguments.ToString().Should().Be("tag -d \"v1\"");
        commands[2].Arguments.ToString().Should().Be("push --porcelain \"origin\" \":refs/heads/feature/a\"");
        commands[2].RowNames.Should().Equal("feature/a");

        BatchRefOperations.BuildDeleteCommands([Branch("x", merged: false)], force: true, deleteRemoteCounterparts: false)
            .Single().Arguments.ToString().Should().Be("branch -D \"x\"");
    }

    [Test]
    public void Push_builds_one_porcelain_command_with_full_refspecs()
    {
        BatchRefCommand command = BatchRefOperations.BuildPushCommand(
            [Branch("feature/a"), Tag("v1")], "origin", forceWithLease: true);

        command.Arguments.ToString().Should().Be(
            "push --porcelain --force-with-lease \"origin\" \"refs/heads/feature/a:refs/heads/feature/a\" \"refs/tags/v1:refs/tags/v1\"");
    }

    [Test]
    public void Delete_output_maps_back_to_rows()
    {
        BatchRefCommand command = BatchRefOperations.BuildDeleteCommands(
            [Branch("merged"), Branch("unmerged", merged: false)], force: false, deleteRemoteCounterparts: false)[0];

        string output =
            "Deleted branch merged (was abc1234).\n" +
            "error: the branch 'unmerged' is not fully merged\n";

        IReadOnlyList<BatchRefRowResult> results = BatchRefOperations.ParseResults(command, success: false, output);
        results[0].Should().Be(new BatchRefRowResult("merged", BatchRefOutcome.Succeeded));
        results[1].Outcome.Should().Be(BatchRefOutcome.Failed);
        results[1].Message.Should().Contain("not fully merged");
    }

    [Test]
    public void Push_porcelain_maps_per_refspec_status()
    {
        BatchRefCommand command = BatchRefOperations.BuildPushCommand(
            [Branch("ok-branch"), Branch("rejected"), Tag("v1")], "origin", forceWithLease: false);

        string output =
            "To /srv/remote\n" +
            "*\trefs/heads/ok-branch:refs/heads/ok-branch\t[new branch]\n" +
            "!\trefs/heads/rejected:refs/heads/rejected\t[rejected] (non-fast-forward)\n" +
            "*\trefs/tags/v1:refs/tags/v1\t[new tag]\n" +
            "Done\n";

        IReadOnlyList<BatchRefRowResult> results = BatchRefOperations.ParseResults(command, success: false, output);
        results[0].Outcome.Should().Be(BatchRefOutcome.Succeeded);
        results[1].Outcome.Should().Be(BatchRefOutcome.Failed);
        results[1].Message.Should().Contain("rejected");
        results[2].Outcome.Should().Be(BatchRefOutcome.Succeeded);
    }
}
