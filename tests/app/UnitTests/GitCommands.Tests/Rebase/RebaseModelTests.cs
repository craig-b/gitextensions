using GitCommands.Rebase;

namespace GitCommandsTests.Rebase;

public sealed class RebaseModelTests
{
    [Test]
    public void Complete_range_rebases_from_onto()
    {
        RebaseTarget target = RebaseTarget.Resolve("main", specificRange: true, fromText: "abc123", toText: "feature");

        target.Should().Be(new RebaseTarget(OnTo: "main", From: "abc123", BranchName: "feature"));
    }

    [TestCase("", "feature")]
    [TestCase("abc123", "")]
    [TestCase(" ", " ")]
    public void Incomplete_range_degrades_to_a_plain_rebase(string fromText, string toText)
    {
        RebaseTarget target = RebaseTarget.Resolve("main", specificRange: true, fromText, toText);

        target.Should().Be(new RebaseTarget(OnTo: null, From: null, BranchName: "main"));
    }

    [Test]
    public void Unchecked_range_is_a_plain_rebase()
    {
        RebaseTarget target = RebaseTarget.Resolve("main", specificRange: false, fromText: "abc123", toText: "feature");

        target.Should().Be(new RebaseTarget(OnTo: null, From: null, BranchName: "main"));
    }

    [Test]
    public void Date_options_disable_interactive_and_each_other()
    {
        RebaseOptionAvailability availability = RebaseOptionAvailability.Evaluate(
            interactive: true, ignoreDate: true, committerDateIsAuthorDate: false, isDirtyWorkingDir: true, supportsUpdateRefs: true);

        availability.InteractiveAllowed.Should().BeFalse();
        availability.PreserveMergesAllowed.Should().BeFalse();
        availability.AutoSquashAllowed.Should().BeFalse();
        availability.IgnoreDateAllowed.Should().BeTrue();
        availability.CommitterDateAllowed.Should().BeFalse();
        availability.AutoStashAllowed.Should().BeTrue();
        availability.UpdateRefsVisible.Should().BeTrue();
    }

    [Test]
    public void Autosquash_needs_interactive()
    {
        RebaseOptionAvailability.Evaluate(interactive: false, ignoreDate: false, committerDateIsAuthorDate: false, isDirtyWorkingDir: false, supportsUpdateRefs: false)
            .AutoSquashAllowed.Should().BeFalse();

        RebaseOptionAvailability.Evaluate(interactive: true, ignoreDate: false, committerDateIsAuthorDate: false, isDirtyWorkingDir: false, supportsUpdateRefs: false)
            .AutoSquashAllowed.Should().BeTrue();
    }

    [TestCase(false, null, true, null)]
    [TestCase(true, true, true, null)]
    [TestCase(true, false, true, true)]
    [TestCase(true, null, false, false)]
    [TestCase(true, null, true, true)]
    public void Update_refs_is_passed_only_when_differing_from_config(bool supported, bool? configured, bool checkbox, bool? expected)
    {
        RebasePreflight.ResolveUpdateRefsChoice(supported, configured, checkbox).Should().Be(expected);
    }

    [TestCase(false, false, true)]
    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    public void Dialog_closes_only_when_nothing_is_in_flight(bool inAction, bool inPatch, bool expected)
    {
        RebasePreflight.ShouldCloseAfterAction(inAction, inPatch).Should().Be(expected);
    }

    [TestCase("Current branch feature/x is up to date.", true)]
    [TestCase("Current branch a is up to date.", true)]
    [TestCase("  Current branch main is up to date.  ", true)]
    [TestCase("Current branch  is up to date.", false)]
    [TestCase("Successfully rebased and updated refs/heads/main.", false)]
    public void Up_to_date_output_is_recognized_for_any_branch_name(string output, bool expected)
    {
        RebaseOutputAnalyzer.IsBranchUpToDate(output).Should().Be(expected);
    }
}
