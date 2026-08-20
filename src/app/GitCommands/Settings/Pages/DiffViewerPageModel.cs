using ResourceManager;

namespace GitCommands.Settings.Pages;

/// <summary>
///  Presentation model for the diff-viewer settings page. The "save current view settings
///  as default" button is view-side chrome (it snapshots the running grid's menu state)
///  and stays with the WinForms page.
/// </summary>
public sealed class DiffViewerPageModel : SettingsPageModel
{
    public DiffViewerPageModel()
        : base("Diff viewer")
    {
        Groups =
        [
            new SettingsGroup("General",
                RememberIgnoreWhiteSpacePreference = new BoolSettingsEntry("Remember the 'Ignore whitespaces' preference",
                    () => AppSettings.RememberIgnoreWhiteSpacePreference, value => AppSettings.RememberIgnoreWhiteSpacePreference = value),
                RememberShowNonPrintingCharsPreference = new BoolSettingsEntry("Remember the 'Show nonprinting characters' preference",
                    () => AppSettings.RememberShowNonPrintingCharsPreference, value => AppSettings.RememberShowNonPrintingCharsPreference = value),
                RememberShowEntireFilePreference = new BoolSettingsEntry("Remember the 'Show entire file' preference",
                    () => AppSettings.RememberShowEntireFilePreference, value => AppSettings.RememberShowEntireFilePreference = value),
                RememberDiffAppearancePreference = new BoolSettingsEntry("Remember the 'Diff appearance' preference",
                    () => AppSettings.RememberDiffDisplayAppearance.Value, value => AppSettings.RememberDiffDisplayAppearance.Value = value),
                RememberNumberOfContextLines = new BoolSettingsEntry("Remember the 'Number of context lines' preference",
                    () => AppSettings.RememberNumberOfContextLines, value => AppSettings.RememberNumberOfContextLines = value),
                RememberShowSyntaxHighlightingInDiff = new BoolSettingsEntry("Remember the 'Show syntax highlighting' preference",
                    () => AppSettings.RememberShowSyntaxHighlightingInDiff, value => AppSettings.RememberShowSyntaxHighlightingInDiff = value),
                OmitUninterestingDiff = new BoolSettingsEntry("Omit uninteresting changes from combined diff",
                    () => AppSettings.OmitUninterestingDiff, value => AppSettings.OmitUninterestingDiff = value),
                AutomaticContinuousScroll = new BoolSettingsEntry(TranslatedStrings.ContScrollToNextFileOnlyWithAlt,
                    () => AppSettings.AutomaticContinuousScroll, value => AppSettings.AutomaticContinuousScroll = value),
                OpenSubmoduleDiffInSeparateWindow = new BoolSettingsEntry("Open Submodule Diff in separate window",
                    () => AppSettings.OpenSubmoduleDiffInSeparateWindow, value => AppSettings.OpenSubmoduleDiffInSeparateWindow = value),
                ShowDiffForAllParents = new BoolSettingsEntry(TranslatedStrings.ShowDiffForAllParentsText,
                    () => AppSettings.ShowDiffForAllParents, value => AppSettings.ShowDiffForAllParents = value),
                ShowAllCustomDiffTools = new BoolSettingsEntry("Show all available difftools",
                    () => AppSettings.ShowAvailableDiffTools, value => AppSettings.ShowAvailableDiffTools = value),
                VerticalRulerPosition = new NumberSettingsEntry("Vertical ruler position [chars]",
                    minimum: 0, maximum: 1000, increment: 1,
                    () => AppSettings.DiffVerticalRulerPosition, value => AppSettings.DiffVerticalRulerPosition = value)),

            new SettingsGroup("Diff coloring",
                UseGitColoring = new BoolSettingsEntry("Git coloring",
                    () => AppSettings.UseGitColoring.Value, value => AppSettings.UseGitColoring.Value = value),
                ReverseGitColoring = new BoolSettingsEntry("Reverse background color",
                    () => AppSettings.ReverseGitColoring.Value, value => AppSettings.ReverseGitColoring.Value = value)),
        ];
    }

    public BoolSettingsEntry RememberIgnoreWhiteSpacePreference { get; }
    public BoolSettingsEntry RememberShowNonPrintingCharsPreference { get; }
    public BoolSettingsEntry RememberShowEntireFilePreference { get; }
    public BoolSettingsEntry RememberDiffAppearancePreference { get; }
    public BoolSettingsEntry RememberNumberOfContextLines { get; }
    public BoolSettingsEntry RememberShowSyntaxHighlightingInDiff { get; }
    public BoolSettingsEntry OmitUninterestingDiff { get; }
    public BoolSettingsEntry AutomaticContinuousScroll { get; }
    public BoolSettingsEntry OpenSubmoduleDiffInSeparateWindow { get; }
    public BoolSettingsEntry ShowDiffForAllParents { get; }
    public BoolSettingsEntry ShowAllCustomDiffTools { get; }
    public NumberSettingsEntry VerticalRulerPosition { get; }

    public BoolSettingsEntry UseGitColoring { get; }
    public BoolSettingsEntry ReverseGitColoring { get; }

    public override IReadOnlyList<SettingsGroup> Groups { get; }
}
