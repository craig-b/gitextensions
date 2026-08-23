using GitCommands.Settings.Pages;
using GitExtensions.Extensibility.Settings;

namespace GitUI.CommandsDialogs.SettingsDialog.Pages;

public partial class AdvancedSettingsPage : SettingsPageWithHeader
{
    private readonly AdvancedPageModel _model = new();

    public AdvancedSettingsPage(IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        InitializeComponent();
        InitializeComplete();

        // the model owns the choice order and captions ("(none)" stores empty)
        cboAutoNormaliseSymbol.DataSource = _model.AutoNormaliseSymbol.Choices.ToList();
        cboAutoNormaliseSymbol.SelectedIndex = 0;
    }

    private IEnumerable<(BoolSettingsEntry Entry, Control Control)> EntryControls =>
    [
        (_model.AlwaysShowCheckoutDialog, chkAlwaysShowCheckoutDlg),
        (_model.UseLastChosenLocalChangesAction, chkUseLocalChangesAction),
        (_model.DontShowHelpImages, chkDontSHowHelpImages),
        (_model.AlwaysShowAdvancedOptions, chkAlwaysShowAdvOpt),
        (_model.CheckForUpdates, chkCheckForUpdates),
        (_model.CheckForReleaseCandidates, chkCheckForRCVersions),
        (_model.UseConsoleEmulator, chkConsoleEmulator),
        (_model.AutoNormaliseBranchName, chkAutoNormaliseBranchName),
        (_model.CommitAndPushForcedWhenAmend, chkCommitAndPushForcedWhenAmend),
    ];

    protected override void SettingsToPage()
    {
        _model.Load();

        foreach ((BoolSettingsEntry entry, Control control) in EntryControls)
        {
            SettingsPageBindings.SetChecked(control, entry.Value);
        }

        cboAutoNormaliseSymbol.Enabled = chkAutoNormaliseBranchName.Checked;
        cboAutoNormaliseSymbol.SelectedIndex = _model.AutoNormaliseSymbol.SelectedIndex;

        base.SettingsToPage();
    }

    protected override void PageToSettings()
    {
        foreach ((BoolSettingsEntry entry, Control control) in EntryControls)
        {
            entry.Value = SettingsPageBindings.GetChecked(control);
        }

        _model.AutoNormaliseSymbol.SelectedIndex = cboAutoNormaliseSymbol.SelectedIndex;

        _model.Save();

        base.PageToSettings();
    }

    public static SettingsPageReference GetPageReference()
    {
        return new SettingsPageReferenceByType(typeof(AdvancedSettingsPage));
    }

    private void chkAutoNormaliseBranchName_CheckedChanged(object sender, EventArgs e)
    {
        cboAutoNormaliseSymbol.Enabled = chkAutoNormaliseBranchName.Checked;
    }
}
