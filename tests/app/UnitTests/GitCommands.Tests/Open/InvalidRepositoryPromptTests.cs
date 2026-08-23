using GitCommands.Open;

namespace GitCommandsTests.Open;

public sealed class InvalidRepositoryPromptTests
{
    [Test]
    public void Evaluate_counts_the_invalid_entries_via_the_predicate()
    {
        InvalidRepositoryPromptOptions options = InvalidRepositoryPromptOptions.Evaluate(
            ["good", "bad1", "bad2"],
            isValidGitWorkingDir: path => path == "good");

        options.InvalidCount.Should().Be(2);
    }

    [Test]
    public void RemoveAll_is_offered_only_when_more_than_one_entry_is_invalid()
    {
        new InvalidRepositoryPromptOptions(InvalidCount: 0).OfferRemoveAll.Should().BeFalse();
        new InvalidRepositoryPromptOptions(InvalidCount: 1).OfferRemoveAll.Should().BeFalse();
        new InvalidRepositoryPromptOptions(InvalidCount: 2).OfferRemoveAll.Should().BeTrue();
    }

    [Test]
    public void Evaluate_with_a_single_invalid_entry_does_not_offer_remove_all()
    {
        InvalidRepositoryPromptOptions.Evaluate(["good", "bad"], path => path == "good")
            .OfferRemoveAll.Should().BeFalse();
    }

    [Test]
    public void Evaluate_with_no_entries_offers_nothing()
    {
        InvalidRepositoryPromptOptions options = InvalidRepositoryPromptOptions.Evaluate([], _ => false);

        options.InvalidCount.Should().Be(0);
        options.OfferRemoveAll.Should().BeFalse();
    }
}
