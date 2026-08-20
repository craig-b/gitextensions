using GitCommands;
using GitCommands.Settings.Pages;

namespace GitCommandsTests.Settings.Pages;

internal sealed class AdvancedPageModelTests : SettingsPageModelTestBase
{
    protected override (string Name, Func<object?> Get, Action<object?> Set)[] Storage { get; } =
    [
        ("AlwaysShowCheckoutBranchDlg", () => AppSettings.AlwaysShowCheckoutBranchDlg, value => AppSettings.AlwaysShowCheckoutBranchDlg = (bool)value!),
        ("UseDefaultCheckoutBranchAction", () => AppSettings.UseDefaultCheckoutBranchAction, value => AppSettings.UseDefaultCheckoutBranchAction = (bool)value!),
        ("DontShowHelpImages", () => AppSettings.DontShowHelpImages, value => AppSettings.DontShowHelpImages = (bool)value!),
        ("AlwaysShowAdvOpt", () => AppSettings.AlwaysShowAdvOpt, value => AppSettings.AlwaysShowAdvOpt = (bool)value!),
        ("UseConsoleEmulatorForCommands", () => AppSettings.UseConsoleEmulatorForCommands.Value, value => AppSettings.UseConsoleEmulatorForCommands.Value = (bool)value!),
        ("AutoNormaliseBranchName", () => AppSettings.AutoNormaliseBranchName, value => AppSettings.AutoNormaliseBranchName = (bool)value!),
        ("AutoNormaliseSymbol", () => AppSettings.AutoNormaliseSymbol, value => AppSettings.AutoNormaliseSymbol = (string)value!),
        ("CommitAndPushForcedWhenAmend", () => AppSettings.CommitAndPushForcedWhenAmend, value => AppSettings.CommitAndPushForcedWhenAmend = (bool)value!),
        ("CheckForUpdates", () => AppSettings.CheckForUpdates, value => AppSettings.CheckForUpdates = (bool)value!),
        ("CheckForReleaseCandidates", () => AppSettings.CheckForReleaseCandidates, value => AppSettings.CheckForReleaseCandidates = (bool)value!),
    ];

    protected override SettingsPageModel CreateModel() => new AdvancedPageModel();

    protected override void ApplyBaseline()
    {
        // the symbol must be a listed choice so a choice change lands on different storage
        AppSettings.AutoNormaliseSymbol = "_";
    }

    [Test]
    public void Page_structure_matches_the_settings_page()
    {
        AdvancedPageModel model = new();

        model.Title.Should().Be("Advanced");
        model.Groups.Select(group => group.Caption).Should().Equal("Checkout", "General", "Commit", "Updates");
        model.Entries.Should().HaveCount(Storage.Length);
    }

    [Test]
    public void Normalisation_symbol_none_choice_stores_empty()
    {
        AdvancedPageModel model = new();

        model.AutoNormaliseSymbol.Choices.Should().Equal("_", "-", "(none)");

        model.Load();
        model.AutoNormaliseSymbol.SelectedIndex = 2;
        model.Save();
        AppSettings.AutoNormaliseSymbol.Should().BeEmpty();

        model.Load();
        model.AutoNormaliseSymbol.SelectedIndex.Should().Be(2);
    }
}
