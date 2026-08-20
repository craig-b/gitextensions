using GitCommands.Settings.Pages;

namespace GitUI.CommandsDialogs.SettingsDialog.Pages;

public partial class CommitDialogSettingsPage : SettingsPageWithHeader
{
    private readonly CommitDialogPageModel _model = new();

    public CommitDialogSettingsPage(IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        InitializeComponent();
        InitializeComplete();
    }

    private IEnumerable<(BoolSettingsEntry Entry, CheckBox Control)> EntryControls =>
    [
        (_model.Autocomplete, chkAutocomplete),
        (_model.ShowErrorsWhenStagingFiles, chkShowErrorsWhenStagingFiles),
        (_model.EnsureSecondLineEmpty, chkEnsureCommitMessageSecondLineEmpty),
        (_model.ComposeMessageInDialog, chkWriteCommitMessageInCommitWindow),
        (_model.RememberAmendCommitState, cbRememberAmendCommitState),
        (_model.ShowCommitAndPush, chkShowCommitAndPush),
        (_model.ShowResetWorkTreeChanges, chkShowResetWorkTreeChanges),
        (_model.ShowResetAllChanges, chkShowResetAllChanges),
    ];

    protected override void SettingsToPage()
    {
        _model.Load();

        foreach ((BoolSettingsEntry entry, CheckBox control) in EntryControls)
        {
            control.Checked = entry.Value;
        }

        _NO_TRANSLATE_CommitDialogNumberOfPreviousMessages.Value = _model.NumberOfPreviousMessages.Value;

        base.SettingsToPage();
    }

    protected override void PageToSettings()
    {
        foreach ((BoolSettingsEntry entry, CheckBox control) in EntryControls)
        {
            entry.Value = control.Checked;
        }

        _model.NumberOfPreviousMessages.Value = (int)_NO_TRANSLATE_CommitDialogNumberOfPreviousMessages.Value;

        _model.Save();

        base.PageToSettings();
    }
}
