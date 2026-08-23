using GitCommands.Settings.Pages;

namespace GitCommandsTests.Settings.Pages;

internal sealed class SettingsPageSearchTests
{
    private static readonly string[] _keywords = ["Amend last commit", "Drop stash", "Rebase / conflict resolution:"];

    [TestCase("confirm", true, TestName = "title substring matches")]
    [TestCase("CONFIRMATIONS", true, TestName = "title match is case-insensitive")]
    [TestCase("stash", true, TestName = "single keyword partially matches")]
    [TestCase("drop stash", true, TestName = "all space-separated keywords must match")]
    [TestCase("drop rebase", true, TestName = "keywords may match different page texts")]
    [TestCase("drop nothing", false, TestName = "one unmatched keyword fails the page")]
    [TestCase("zzz", false, TestName = "no match at all")]
    public void Matches(string searchText, bool expected)
    {
        SettingsPageSearch.Matches(searchText, "Confirmations", _keywords).Should().Be(expected);
    }

    [Test]
    public void Page_models_expose_group_and_entry_captions_as_keywords()
    {
        ConfirmationsPageModel model = new();

        string[] keywords = [.. model.GetSearchKeywords()];

        keywords.Should().Contain("Commits:");
        keywords.Should().Contain("Amend last commit");
        keywords.Should().HaveCount(model.Groups.Count + model.Entries.Count());
    }

    [Test]
    public void Search_finds_pages_the_way_the_dialog_does()
    {
        // "amend" appears in Confirmations, Commit dialog, and Advanced entry captions
        SettingsPageModel[] pages = [new ConfirmationsPageModel(), new BlameViewerPageModel(), new SortingPageModel()];

        SettingsPageModel[] matches = [.. pages.Where(page => SettingsPageSearch.Matches("amend", page.Title, page.GetSearchKeywords()))];

        matches.Should().ContainSingle().Which.Should().BeOfType<ConfirmationsPageModel>();
    }
}
