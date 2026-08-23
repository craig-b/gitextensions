using GitCommands;
using GitCommands.Settings.Pages;

namespace GitCommandsTests.Settings.Pages;

internal sealed class DiffViewerPageModelTests : SettingsPageModelTestBase
{
    protected override (string Name, Func<object?> Get, Action<object?> Set)[] Storage { get; } =
    [
        ("RememberIgnoreWhiteSpacePreference", () => AppSettings.RememberIgnoreWhiteSpacePreference, value => AppSettings.RememberIgnoreWhiteSpacePreference = (bool)value!),
        ("RememberShowNonPrintingCharsPreference", () => AppSettings.RememberShowNonPrintingCharsPreference, value => AppSettings.RememberShowNonPrintingCharsPreference = (bool)value!),
        ("RememberShowEntireFilePreference", () => AppSettings.RememberShowEntireFilePreference, value => AppSettings.RememberShowEntireFilePreference = (bool)value!),
        ("RememberDiffDisplayAppearance", () => AppSettings.RememberDiffDisplayAppearance.Value, value => AppSettings.RememberDiffDisplayAppearance.Value = (bool)value!),
        ("RememberNumberOfContextLines", () => AppSettings.RememberNumberOfContextLines, value => AppSettings.RememberNumberOfContextLines = (bool)value!),
        ("RememberShowSyntaxHighlightingInDiff", () => AppSettings.RememberShowSyntaxHighlightingInDiff, value => AppSettings.RememberShowSyntaxHighlightingInDiff = (bool)value!),
        ("OmitUninterestingDiff", () => AppSettings.OmitUninterestingDiff, value => AppSettings.OmitUninterestingDiff = (bool)value!),
        ("AutomaticContinuousScroll", () => AppSettings.AutomaticContinuousScroll, value => AppSettings.AutomaticContinuousScroll = (bool)value!),
        ("OpenSubmoduleDiffInSeparateWindow", () => AppSettings.OpenSubmoduleDiffInSeparateWindow, value => AppSettings.OpenSubmoduleDiffInSeparateWindow = (bool)value!),
        ("ShowDiffForAllParents", () => AppSettings.ShowDiffForAllParents, value => AppSettings.ShowDiffForAllParents = (bool)value!),
        ("ShowAvailableDiffTools", () => AppSettings.ShowAvailableDiffTools, value => AppSettings.ShowAvailableDiffTools = (bool)value!),
        ("DiffVerticalRulerPosition", () => AppSettings.DiffVerticalRulerPosition, value => AppSettings.DiffVerticalRulerPosition = (int)value!),
        ("UseGitColoring", () => AppSettings.UseGitColoring.Value, value => AppSettings.UseGitColoring.Value = (bool)value!),
        ("ReverseGitColoring", () => AppSettings.ReverseGitColoring.Value, value => AppSettings.ReverseGitColoring.Value = (bool)value!),
    ];

    protected override SettingsPageModel CreateModel() => new DiffViewerPageModel();

    [Test]
    public void Page_structure_matches_the_settings_page()
    {
        DiffViewerPageModel model = new();

        model.Title.Should().Be("Diff viewer");
        model.Groups.Select(group => group.Caption).Should().Equal("General", "Diff coloring");
        model.Entries.Should().HaveCount(Storage.Length);
    }
}
