using GitCommands.Merge;

namespace GitCommandsTests.Merge;

public sealed class MergeModelTests
{
    [Test]
    public void Options_build_the_merge_command()
    {
        MergeBranchOptions options = new("feature");

        options.ToArguments(mergeMessagePath: null, path => path).ToString()
            .Should().Be("merge --no-edit feature");
    }

    [Test]
    public void Options_carry_every_advanced_switch()
    {
        MergeBranchOptions options = new(
            "feature",
            AllowFastForward: false,
            Squash: true,
            NoCommit: true,
            Strategy: "ours",
            AllowUnrelatedHistories: true,
            LogMessageCount: 5);

        string arguments = options.ToArguments("/tmp/MERGE_MSG", path => path).ToString();

        arguments.Should().Contain("--no-ff");
        arguments.Should().Contain("--squash");
        arguments.Should().Contain("--no-commit");
        arguments.Should().Contain("--strategy=ours");
        arguments.Should().Contain("--allow-unrelated-histories");
        arguments.Should().Contain("--log=5");
        arguments.Should().Contain("-F \"/tmp/MERGE_MSG\"");
        arguments.Should().EndWith("feature");
    }

    [TestCase(false, false, false, false, true, false, false, false)]
    [TestCase(true, false, false, false, false, false, false, false)]
    [TestCase(false, true, true, true, true, true, true, true)]
    public void Availability_follows_the_option_interplay(
        bool noFastForward, bool nonDefaultStrategy, bool addLogMessages, bool addMergeMessage,
        bool squashAllowed, bool strategyVisible, bool logCountEnabled, bool messageEnabled)
    {
        MergeOptionAvailability availability = MergeOptionAvailability.Evaluate(noFastForward, nonDefaultStrategy, addLogMessages, addMergeMessage);

        availability.Should().Be(new MergeOptionAvailability(squashAllowed, strategyVisible, logCountEnabled, messageEnabled));
    }

    [TestCase(true, false, true)]
    [TestCase(false, true, true)]
    [TestCase(false, false, false)]
    public void Dialog_closes_on_success_or_conflict(bool success, bool wasConflict, bool expected)
    {
        MergePostAction.ShouldCloseAndNotify(success, wasConflict).Should().Be(expected);
    }

    [Test]
    public void Conflict_handler_offers_commit_unless_no_commit()
    {
        MergePostAction.OfferCommitOnConflict(noCommit: false).Should().BeTrue();
        MergePostAction.OfferCommitOnConflict(noCommit: true).Should().BeFalse();
    }

    [TestCase("caller/branch", "origin/main", "caller/branch")]
    [TestCase(null, "origin/main", "origin/main")]
    [TestCase("", "origin/main", "origin/main")]
    [TestCase(null, null, null)]
    public void Default_branch_prefers_the_caller_choice(string? defaultBranch, string? remoteBranch, string? expected)
    {
        MergeDefaultBranch.Resolve(defaultBranch, remoteBranch).Should().Be(expected);
    }

    [Test]
    public void Known_strategies_match_the_git_manual()
    {
        MergeStrategies.Known.Should().Equal("resolve", "recursive", "octopus", "ours", "subtree");
    }
}
