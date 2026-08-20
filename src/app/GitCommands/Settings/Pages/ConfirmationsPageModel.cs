namespace GitCommands.Settings.Pages;

/// <summary>
///  Presentation model for the confirmations settings page. Entries speak the positive
///  "confirm/do this" sense the checkboxes show; the <c>Dont*</c> storage inversion (and the
///  inverted tri-states, where an unset value means "ask every time") is internalized here.
/// </summary>
public sealed class ConfirmationsPageModel : SettingsPageModel
{
    public ConfirmationsPageModel()
        : base("Confirmations")
    {
        Groups =
        [
            new SettingsGroup("Commits:",
                AmendLastCommit = new BoolSettingsEntry("Amend last commit",
                    () => !AppSettings.DontConfirmAmend, value => AppSettings.DontConfirmAmend = !value),
                UndoLastCommit = new BoolSettingsEntry("Undo last commit",
                    () => !AppSettings.DontConfirmUndoLastCommit, value => AppSettings.DontConfirmUndoLastCommit = !value),
                CommitWhenNoBranch = new BoolSettingsEntry("Commit when no branch is currently checked out (headless state)",
                    () => !AppSettings.DontConfirmCommitIfNoBranch, value => AppSettings.DontConfirmCommitIfNoBranch = !value),
                RebaseOnTopOfSelectedCommit = new BoolSettingsEntry("Rebase on top of selected commit",
                    () => !AppSettings.DontConfirmRebase, value => AppSettings.DontConfirmRebase = !value)),

            new SettingsGroup("Branches:",
                FetchAndPruneBranches = new BoolSettingsEntry("Fetch and prune branches",
                    () => !AppSettings.DontConfirmFetchAndPruneAll, value => AppSettings.DontConfirmFetchAndPruneAll = !value),
                PushNewBranch = new BoolSettingsEntry("Push a new branch for the remote",
                    () => !AppSettings.DontConfirmPushNewBranch, value => AppSettings.DontConfirmPushNewBranch = !value),
                AddTrackingReference = new BoolSettingsEntry("Add a tracking reference for newly pushed branch",
                    () => !AppSettings.DontConfirmAddTrackingRef, value => AppSettings.DontConfirmAddTrackingRef = !value),
                DeleteUnmergedBranches = new BoolSettingsEntry("Delete unmerged branches",
                    () => !AppSettings.DontConfirmDeleteUnmergedBranch, value => AppSettings.DontConfirmDeleteUnmergedBranch = !value),
                CheckoutBranchUsingLeftPanel = new BoolSettingsEntry("Checkout branch using left panel",
                    () => AppSettings.ConfirmBranchCheckout.Value, value => AppSettings.ConfirmBranchCheckout.Value = value)),

            new SettingsGroup("Stash:",
                ApplyStashAfterCheckout = new TriStateSettingsEntry("Apply stashed changes after successful checkout (else stash will be popped automatically)",
                    () => Invert(AppSettings.AutoPopStashAfterCheckoutBranch), value => AppSettings.AutoPopStashAfterCheckoutBranch = Invert(value)),
                ApplyStashAfterPull = new TriStateSettingsEntry("Apply stashed changes after successful pull (else stash will be popped automatically)",
                    () => Invert(AppSettings.AutoPopStashAfterPull), value => AppSettings.AutoPopStashAfterPull = Invert(value)),
                DropStash = new BoolSettingsEntry("Drop stash",
                    () => !AppSettings.DontConfirmStashDrop, value => AppSettings.DontConfirmStashDrop = !value)),

            new SettingsGroup("Rebase / conflict resolution:",
                ResolveConflicts = new BoolSettingsEntry("Resolve conflicts",
                    () => !AppSettings.DontConfirmResolveConflicts, value => AppSettings.DontConfirmResolveConflicts = !value),
                CommitAfterConflictsResolved = new BoolSettingsEntry("Commit changes after conflicts have been resolved",
                    () => !AppSettings.DontConfirmCommitAfterConflictsResolved, value => AppSettings.DontConfirmCommitAfterConflictsResolved = !value),
                SecondAbortConfirmation = new BoolSettingsEntry("Confirm for the second time to abort a merge",
                    () => !AppSettings.DontConfirmSecondAbortConfirmation, value => AppSettings.DontConfirmSecondAbortConfirmation = !value)),

            new SettingsGroup("Submodules:",
                UpdateSubmodulesOnCheckout = new TriStateSettingsEntry("Update submodules on checkout",
                    () => Invert(AppSettings.DontConfirmUpdateSubmodulesOnCheckout), value => AppSettings.DontConfirmUpdateSubmodulesOnCheckout = Invert(value))),

            new SettingsGroup("Worktrees:",
                SwitchWorktree = new BoolSettingsEntry("Switch worktree",
                    () => !AppSettings.DontConfirmSwitchWorktree, value => AppSettings.DontConfirmSwitchWorktree = !value)),
        ];
    }

    public BoolSettingsEntry AmendLastCommit { get; }
    public BoolSettingsEntry UndoLastCommit { get; }
    public BoolSettingsEntry CommitWhenNoBranch { get; }
    public BoolSettingsEntry RebaseOnTopOfSelectedCommit { get; }

    public BoolSettingsEntry FetchAndPruneBranches { get; }
    public BoolSettingsEntry PushNewBranch { get; }
    public BoolSettingsEntry AddTrackingReference { get; }
    public BoolSettingsEntry DeleteUnmergedBranches { get; }
    public BoolSettingsEntry CheckoutBranchUsingLeftPanel { get; }

    public TriStateSettingsEntry ApplyStashAfterCheckout { get; }
    public TriStateSettingsEntry ApplyStashAfterPull { get; }
    public BoolSettingsEntry DropStash { get; }

    public BoolSettingsEntry ResolveConflicts { get; }
    public BoolSettingsEntry CommitAfterConflictsResolved { get; }
    public BoolSettingsEntry SecondAbortConfirmation { get; }

    public TriStateSettingsEntry UpdateSubmodulesOnCheckout { get; }

    public BoolSettingsEntry SwitchWorktree { get; }

    public override IReadOnlyList<SettingsGroup> Groups { get; }

    private static bool? Invert(bool? value) => value is null ? null : !value;
}
