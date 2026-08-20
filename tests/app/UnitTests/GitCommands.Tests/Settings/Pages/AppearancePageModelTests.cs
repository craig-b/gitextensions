using GitCommands;
using GitCommands.Settings.Pages;

namespace GitCommandsTests.Settings.Pages;

internal sealed class AppearancePageModelTests : SettingsPageModelTestBase
{
    protected override (string Name, Func<object?> Get, Action<object?> Set)[] Storage { get; } =
    [
        ("RelativeDate", () => AppSettings.RelativeDate, value => AppSettings.RelativeDate = (bool)value!),
        ("ShowRepoCurrentBranch", () => AppSettings.ShowRepoCurrentBranch, value => AppSettings.ShowRepoCurrentBranch = (bool)value!),
        ("EnableAutoScale", () => AppSettings.EnableAutoScale, value => AppSettings.EnableAutoScale = (bool)value!),
        ("TruncatePathMethod", () => AppSettings.TruncatePathMethod, value => AppSettings.TruncatePathMethod = (TruncatePathMethod)value!),
        ("ShowAuthorAvatarColumn", () => AppSettings.ShowAuthorAvatarColumn, value => AppSettings.ShowAuthorAvatarColumn = (bool)value!),
        ("ShowAuthorAvatarInCommitInfo", () => AppSettings.ShowAuthorAvatarInCommitInfo, value => AppSettings.ShowAuthorAvatarInCommitInfo = (bool)value!),
        ("AvatarImageCacheDays", () => AppSettings.AvatarImageCacheDays, value => AppSettings.AvatarImageCacheDays = (int)value!),
        ("AvatarProvider", () => AppSettings.AvatarProvider, value => AppSettings.AvatarProvider = (AvatarProvider)value!),
        ("AvatarFallbackType", () => AppSettings.AvatarFallbackType, value => AppSettings.AvatarFallbackType = (AvatarFallbackType)value!),
        ("CustomAvatarTemplate", () => AppSettings.CustomAvatarTemplate, value => AppSettings.CustomAvatarTemplate = (string)value!),
        ("Translation", () => AppSettings.Translation, value => AppSettings.Translation = (string)value!),
        ("Dictionary", () => AppSettings.Dictionary, value => AppSettings.Dictionary = (string)value!),
    ];

    protected override SettingsPageModel CreateModel() => new AppearancePageModel();

    protected override void ApplyBaseline()
    {
        AppSettings.AvatarImageCacheDays = 5;
        AppSettings.Translation = "English";
        AppSettings.Dictionary = "none";
    }

    [Test]
    public void Page_structure_matches_the_settings_page()
    {
        AppearancePageModel model = new();

        model.Title.Should().Be("Appearance");
        model.Groups.Select(group => group.Caption).Should().Equal("General", "Author images", "Language");
        model.Entries.Should().HaveCount(Storage.Length);
    }

    [Test]
    public void Truncate_choice_maps_the_page_index_order()
    {
        AppSettings.TruncatePathMethod = TruncatePathMethod.TrimStart;
        AppearancePageModel model = new();

        model.TruncateLongFilenames.Choices.Should().Equal("None", "Compact", "Trim start", "Filename only");

        model.Load();
        model.TruncateLongFilenames.SelectedIndex.Should().Be(2);

        model.TruncateLongFilenames.SelectedIndex = 3;
        model.Save();
        AppSettings.TruncatePathMethod.Should().Be(TruncatePathMethod.FileNameOnly);
    }

    [Test]
    public void Available_languages_start_with_English()
    {
        AppearancePageModel.GetAvailableLanguages().Should().StartWith("English");
    }
}
