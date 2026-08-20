using GitCommands;
using GitCommands.Settings.Pages;
using GitUIPluginInterfaces;

namespace GitCommandsTests.Settings.Pages;

internal sealed class SortingPageModelTests : SettingsPageModelTestBase
{
    protected override (string Name, Func<object?> Get, Action<object?> Set)[] Storage { get; } =
    [
        ("RevisionSortOrder", () => AppSettings.RevisionSortOrder.Value, value => AppSettings.RevisionSortOrder.Value = (RevisionSortOrder)value!),
        ("RefsSortBy", () => AppSettings.RefsSortBy, value => AppSettings.RefsSortBy = (GitRefsSortBy)value!),
        ("RefsSortOrder", () => AppSettings.RefsSortOrder, value => AppSettings.RefsSortOrder = (GitRefsSortOrder)value!),
        ("PrioritizedBranchNames", () => AppSettings.PrioritizedBranchNames, value => AppSettings.PrioritizedBranchNames = (string)value!),
        ("PrioritizedRemoteNames", () => AppSettings.PrioritizedRemoteNames, value => AppSettings.PrioritizedRemoteNames = (string)value!),
    ];

    protected override SettingsPageModel CreateModel() => new SortingPageModel();

    [Test]
    public void Page_structure_matches_the_settings_page()
    {
        SortingPageModel model = new();

        model.Title.Should().Be("Sorting");
        model.Groups.Select(group => group.Caption).Should().Equal("Sorting");
        model.Entries.Should().HaveCount(Storage.Length);
    }

    [Test]
    public void Choice_captions_are_the_enum_descriptions()
    {
        SortingPageModel model = new();

        model.BranchesOrder.Choices.Should().Equal("A ↓ Z", "Z ↑ A");
        model.RevisionsSortBy.Choices.Should().HaveCount(Enum.GetValues<RevisionSortOrder>().Length);
        model.BranchesSortBy.Choices.Should().HaveCount(Enum.GetValues<GitRefsSortBy>().Length);
    }
}
