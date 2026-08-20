using GitCommands;
using GitCommands.Settings.Pages;

namespace GitCommandsTests.Settings.Pages;

internal sealed class BrowseRepoPageModelTests : SettingsPageModelTestBase
{
    protected override (string Name, Func<object?> Get, Action<object?> Set)[] Storage { get; } =
    [
        ("UseBrowseForFileHistory", () => AppSettings.UseBrowseForFileHistory.Value, value => AppSettings.UseBrowseForFileHistory.Value = (bool)value!),
        ("UseDiffViewerForBlame", () => AppSettings.UseDiffViewerForBlame.Value, value => AppSettings.UseDiffViewerForBlame.Value = (bool)value!),
        ("ShowFindInCommitFilesGitGrep", () => AppSettings.ShowFindInCommitFilesGitGrep.Value, value => AppSettings.ShowFindInCommitFilesGitGrep.Value = (bool)value!),
        ("ShowRevisionGridTooltips", () => AppSettings.ShowRevisionGridTooltips.Value, value => AppSettings.ShowRevisionGridTooltips.Value = (bool)value!),
        ("ShowGpgInformation", () => AppSettings.ShowGpgInformation.Value, value => AppSettings.ShowGpgInformation.Value = (bool)value!),
        ("ShowOutputHistoryAsTab", () => AppSettings.ShowOutputHistoryAsTab.Value, value => AppSettings.ShowOutputHistoryAsTab.Value = (bool)value!),
        ("OutputHistoryDepth", () => AppSettings.OutputHistoryDepth.Value, value => AppSettings.OutputHistoryDepth.Value = (int)value!),
    ];

    protected override SettingsPageModel CreateModel() => new BrowseRepoPageModel();

    protected override void ApplyBaseline()
    {
        // within the page's 0..1000 clamp so load-then-save round-trips
        AppSettings.OutputHistoryDepth.Value = 500;
    }

    [Test]
    public void Page_structure_matches_the_settings_page()
    {
        BrowseRepoPageModel model = new();

        model.Title.Should().Be("Browse repository window");
        model.Groups.Select(group => group.Caption).Should().Equal("General", "Tabs (restart required)");
        model.Entries.Should().HaveCount(Storage.Length);
    }

    [Test]
    public void Panel_visibility_is_rederived_only_when_output_history_config_changes()
    {
        AppSettings.ShowOutputHistoryAsTab.Value = false;
        AppSettings.OutputHistoryDepth.Value = 500;
        AppSettings.OutputHistoryPanelVisible.Value = false;

        BrowseRepoPageModel model = new();
        model.Load();
        model.Save();
        AppSettings.OutputHistoryPanelVisible.Value.Should().BeFalse(because: "nothing changed, the running app's panel state is preserved");

        model.Load();
        model.OutputHistoryDepth.Value = 600;
        model.Save();
        AppSettings.OutputHistoryPanelVisible.Value.Should().BeTrue(because: "config changed with panel mode and non-zero depth");
    }
}
