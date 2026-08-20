using GitCommands;
using GitCommands.Settings.Pages;

namespace GitCommandsTests.Settings.Pages;

internal sealed class BlameViewerPageModelTests : SettingsPageModelTestBase
{
    protected override (string Name, Func<object?> Get, Action<object?> Set)[] Storage { get; } =
    [
        ("IgnoreWhitespaceOnBlame", () => AppSettings.IgnoreWhitespaceOnBlame, value => AppSettings.IgnoreWhitespaceOnBlame = (bool)value!),
        ("DetectCopyInFileOnBlame", () => AppSettings.DetectCopyInFileOnBlame, value => AppSettings.DetectCopyInFileOnBlame = (bool)value!),
        ("DetectCopyInAllOnBlame", () => AppSettings.DetectCopyInAllOnBlame, value => AppSettings.DetectCopyInAllOnBlame = (bool)value!),
        ("BlameDisplayAuthorFirst", () => AppSettings.BlameDisplayAuthorFirst, value => AppSettings.BlameDisplayAuthorFirst = (bool)value!),
        ("BlameShowAuthor", () => AppSettings.BlameShowAuthor, value => AppSettings.BlameShowAuthor = (bool)value!),
        ("BlameShowAuthorDate", () => AppSettings.BlameShowAuthorDate, value => AppSettings.BlameShowAuthorDate = (bool)value!),
        ("BlameShowAuthorTime", () => AppSettings.BlameShowAuthorTime, value => AppSettings.BlameShowAuthorTime = (bool)value!),
        ("BlameShowLineNumbers", () => AppSettings.BlameShowLineNumbers, value => AppSettings.BlameShowLineNumbers = (bool)value!),
        ("BlameShowOriginalFilePath", () => AppSettings.BlameShowOriginalFilePath, value => AppSettings.BlameShowOriginalFilePath = (bool)value!),
        ("BlameShowAuthorAvatar", () => AppSettings.BlameShowAuthorAvatar, value => AppSettings.BlameShowAuthorAvatar = (bool)value!),
    ];

    protected override SettingsPageModel CreateModel() => new BlameViewerPageModel();

    [Test]
    public void Page_structure_matches_the_settings_page()
    {
        BlameViewerPageModel model = new();

        model.Title.Should().Be("Blame viewer");
        model.Groups.Select(group => group.Caption).Should().Equal("Blame settings", "Display result settings");
        model.Entries.Should().HaveCount(Storage.Length);
    }
}
