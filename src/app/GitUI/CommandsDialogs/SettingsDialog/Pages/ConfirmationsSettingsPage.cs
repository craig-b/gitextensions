using GitCommands.Settings.Pages;
using GitExtensions.Extensibility.Settings;

namespace GitUI.CommandsDialogs.SettingsDialog.Pages;

public partial class ConfirmationsSettingsPage : SettingsPageWithHeader
{
    private readonly ConfirmationsPageModel _model = new();

    public ConfirmationsSettingsPage(IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        InitializeComponent();
        InitializeComplete();
    }

    private IEnumerable<(SettingsEntry Entry, CheckBox Control)> EntryControls =>
    [
        (_model.AmendLastCommit, chkAmend),
        (_model.UndoLastCommit, chkUndoLastCommitConfirmation),
        (_model.CommitWhenNoBranch, chkCommitIfNoBranch),
        (_model.RebaseOnTopOfSelectedCommit, chkRebaseOnTopOfSelectedCommit),
        (_model.FetchAndPruneBranches, chkFetchAndPruneAllConfirmation),
        (_model.PushNewBranch, chkPushNewBranch),
        (_model.AddTrackingReference, chkAddTrackingRef),
        (_model.DeleteUnmergedBranches, chkBranchDeleteUnmerged),
        (_model.CheckoutBranchUsingLeftPanel, chkBranchCheckoutConfirmation),
        (_model.ApplyStashAfterCheckout, chkAutoPopStashAfterCheckout),
        (_model.ApplyStashAfterPull, chkAutoPopStashAfterPull),
        (_model.DropStash, chkConfirmStashDrop),
        (_model.ResolveConflicts, chkResolveConflicts),
        (_model.CommitAfterConflictsResolved, chkCommitAfterConflictsResolved),
        (_model.SecondAbortConfirmation, chkSecondAbortConfirmation),
        (_model.UpdateSubmodulesOnCheckout, chkUpdateModules),
        (_model.SwitchWorktree, chkSwitchWorktree),
    ];

    protected override void SettingsToPage()
    {
        _model.Load();

        foreach ((SettingsEntry entry, CheckBox control) in EntryControls)
        {
            switch (entry)
            {
                case BoolSettingsEntry boolEntry:
                    control.Checked = boolEntry.Value;
                    break;
                case TriStateSettingsEntry triStateEntry:
                    control.CheckState = ToCheckState(triStateEntry.Value);
                    break;
            }
        }

        base.SettingsToPage();
    }

    protected override void PageToSettings()
    {
        foreach ((SettingsEntry entry, CheckBox control) in EntryControls)
        {
            switch (entry)
            {
                case BoolSettingsEntry boolEntry:
                    boolEntry.Value = control.Checked;
                    break;
                case TriStateSettingsEntry triStateEntry:
                    triStateEntry.Value = FromCheckState(control.CheckState);
                    break;
            }
        }

        _model.Save();

        base.PageToSettings();
    }

    public static SettingsPageReference GetPageReference()
    {
        return new SettingsPageReferenceByType(typeof(ConfirmationsSettingsPage));
    }

    private static CheckState ToCheckState(bool? value)
        => value switch
        {
            null => CheckState.Indeterminate,
            true => CheckState.Checked,
            false => CheckState.Unchecked,
        };

    private static bool? FromCheckState(CheckState state)
        => state switch
        {
            CheckState.Indeterminate => null,
            CheckState.Checked => true,
            _ => false,
        };
}
