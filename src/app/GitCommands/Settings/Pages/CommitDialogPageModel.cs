namespace GitCommands.Settings.Pages;

/// <summary>Presentation model for the commit-dialog settings page.</summary>
public sealed class CommitDialogPageModel : SettingsPageModel
{
    public CommitDialogPageModel()
        : base("Commit dialog")
    {
        Groups =
        [
            new SettingsGroup("Behaviour",
                Autocomplete = new BoolSettingsEntry("Provide auto-completion in commit dialog",
                    () => AppSettings.ProvideAutocompletion, value => AppSettings.ProvideAutocompletion = value),
                ShowErrorsWhenStagingFiles = new BoolSettingsEntry("Show errors when staging files",
                    () => AppSettings.ShowErrorsWhenStagingFiles, value => AppSettings.ShowErrorsWhenStagingFiles = value),
                EnsureSecondLineEmpty = new BoolSettingsEntry("Ensure the second line of commit message is empty",
                    () => AppSettings.EnsureCommitMessageSecondLineEmpty, value => AppSettings.EnsureCommitMessageSecondLineEmpty = value),
                ComposeMessageInDialog = new BoolSettingsEntry("Compose commit messages in Commit dialog\n(otherwise the message will be requested during commit)",
                    () => AppSettings.UseFormCommitMessage, value => AppSettings.UseFormCommitMessage = value),
                NumberOfPreviousMessages = new NumberSettingsEntry("Number of previous messages in commit dialog",
                    minimum: 1, maximum: 999, increment: 1,
                    () => AppSettings.CommitDialogNumberOfPreviousMessages, value => AppSettings.CommitDialogNumberOfPreviousMessages = value),
                RememberAmendCommitState = new BoolSettingsEntry("Remember 'Amend commit' checkbox on commit form close",
                    () => AppSettings.RememberAmendCommitState, value => AppSettings.RememberAmendCommitState = value)),

            new SettingsGroup("Show additional buttons in commit button area",
                ShowCommitAndPush = new BoolSettingsEntry("Commit & Push",
                    () => AppSettings.ShowCommitAndPush, value => AppSettings.ShowCommitAndPush = value),
                ShowResetWorkTreeChanges = new BoolSettingsEntry("Reset Unstaged Changes",
                    () => AppSettings.ShowResetWorkTreeChanges, value => AppSettings.ShowResetWorkTreeChanges = value),
                ShowResetAllChanges = new BoolSettingsEntry("Reset All Changes",
                    () => AppSettings.ShowResetAllChanges, value => AppSettings.ShowResetAllChanges = value)),
        ];
    }

    public BoolSettingsEntry Autocomplete { get; }
    public BoolSettingsEntry ShowErrorsWhenStagingFiles { get; }
    public BoolSettingsEntry EnsureSecondLineEmpty { get; }
    public BoolSettingsEntry ComposeMessageInDialog { get; }
    public NumberSettingsEntry NumberOfPreviousMessages { get; }
    public BoolSettingsEntry RememberAmendCommitState { get; }

    public BoolSettingsEntry ShowCommitAndPush { get; }
    public BoolSettingsEntry ShowResetWorkTreeChanges { get; }
    public BoolSettingsEntry ShowResetAllChanges { get; }

    public override IReadOnlyList<SettingsGroup> Groups { get; }
}
