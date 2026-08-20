using GitCommands.Settings.Pages;
using GitExtensions.Extensibility.Settings;
using GitUI.UserControls.RevisionGrid;
using ResourceManager;

namespace GitUI.CommandsDialogs.SettingsDialog.Pages;

public partial class DiffViewerSettingsPage : SettingsPageWithHeader
{
    private readonly TranslationString _saveCurrentViewSettingsAsDefaultTooltip = new("""
        Saves all current view settings as the default for future sessions.
        Note: The checkboxes 'Remember the "xyz" preference' only affect the running instance
        as long as the default has not been saved. These preference values are held in memory
        and must be explicitly saved to become persistent defaults.
        """);

    private readonly DiffViewerPageModel _model = new();

    public DiffViewerSettingsPage(IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        InitializeComponent();
        InitializeComplete();

        chkShowDiffForAllParents.Text = ResourceManager.TranslatedStrings.ShowDiffForAllParentsText;
        chkShowDiffForAllParents.ToolTipText = ResourceManager.TranslatedStrings.ShowDiffForAllParentsTooltip;
        chkContScrollToNextFileOnlyWithAlt.Text = ResourceManager.TranslatedStrings.ContScrollToNextFileOnlyWithAlt;
    }

    private IEnumerable<(BoolSettingsEntry Entry, Control Control)> BoolEntryControls =>
    [
        (_model.RememberIgnoreWhiteSpacePreference, chkRememberIgnoreWhiteSpacePreference),
        (_model.RememberShowNonPrintingCharsPreference, chkRememberShowNonPrintingCharsPreference),
        (_model.RememberShowEntireFilePreference, chkRememberShowEntireFilePreference),
        (_model.RememberDiffAppearancePreference, chkRememberDiffAppearancePreference),
        (_model.RememberNumberOfContextLines, chkRememberNumberOfContextLines),
        (_model.RememberShowSyntaxHighlightingInDiff, chkRememberShowSyntaxHighlightingInDiff),
        (_model.OmitUninterestingDiff, chkOmitUninterestingDiff),
        (_model.AutomaticContinuousScroll, chkContScrollToNextFileOnlyWithAlt),
        (_model.OpenSubmoduleDiffInSeparateWindow, chkOpenSubmoduleDiffInSeparateWindow),
        (_model.ShowDiffForAllParents, chkShowDiffForAllParents),
        (_model.ShowAllCustomDiffTools, chkShowAllCustomDiffTools),
        (_model.UseGitColoring, chkUseGitColoring),
        (_model.ReverseGitColoring, chkUseGEThemeGitColoring),
    ];

    protected override void SettingsToPage()
    {
        _model.Load();

        foreach ((BoolSettingsEntry entry, Control control) in BoolEntryControls)
        {
            SettingsPageBindings.SetChecked(control, entry.Value);
        }

        VerticalRulerPosition.Value = _model.VerticalRulerPosition.Value;
        chkUseGEThemeGitColoring.Enabled = chkUseGitColoring.Checked;

        base.SettingsToPage();
    }

    protected override void PageToSettings()
    {
        foreach ((BoolSettingsEntry entry, Control control) in BoolEntryControls)
        {
            entry.Value = SettingsPageBindings.GetChecked(control);
        }

        _model.VerticalRulerPosition.Value = (int)VerticalRulerPosition.Value;

        _model.Save();

        base.PageToSettings();
    }

    private void chkUseGitColoring_CheckedChanged(object sender, EventArgs e)
        => chkUseGEThemeGitColoring.Enabled = chkUseGitColoring.Checked;

    private void btnSaveCurrentViewSettingsAsDefault_Click(object sender, EventArgs e)
    {
        RevisionGridMenuCommands.SaveCurrentViewSettingsAsDefault();
    }

    public static SettingsPageReference GetPageReference()
    {
        return new SettingsPageReferenceByType(typeof(DiffViewerSettingsPage));
    }
}
