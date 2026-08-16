using GitUIPluginInterfaces;
using Intent = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI;

// IUICommandBus dispatch: maps each intent record onto the existing Start* method it replaces.
// This is the M3.1 strangler seam - call sites migrate to Execute(intent) one by one, and the
// Start* family shrinks as they do. See cross-platform-plan.md section 13.
partial class GitUICommands
{
    public bool Execute(Intent.IUICommand command)
    {
        // Intents carry no owner; the host resolves it ambiently. Until M3.2 lands a real
        // resolution (e.g. the active form), the owner is null - every dialog accepts that.
        IWin32Window? owner = null;

        return command switch
        {
            Intent.AddFiles c => StartAddFilesDialog(owner, c.Files),
            Intent.AddToGitIgnore c => StartAddToGitIgnoreDialog(owner, c.LocalExclude, [.. c.FilePatterns]),
            Intent.AmendCommit c => StartAmendCommitDialog(owner, c.Revision),
            Intent.ApplyPatch c => StartApplyPatchDialog(owner, c.PatchFile),
            Intent.Archive c => StartArchiveDialog(owner, c.Revision, c.Revision2, c.Path),
            Intent.BatchFileProcess c => Run(() => StartBatchFileProcessDialog(c.BatchFile)),
            Intent.Browse c => StartBrowseDialog(owner, c.Args),
            Intent.CheckoutBranch c => StartCheckoutBranch(owner, c.Branch, c.Remote, c.ContainObjectIds),
            Intent.CheckoutRemoteBranch c => StartCheckoutRemoteBranch(owner, c.Branch),
            Intent.CheckoutRevision c => StartCheckoutRevisionDialog(owner, c.Revision),
            Intent.CherryPick c => CherryPick(c),
            Intent.CleanupRepository c => StartCleanupRepositoryDialog(owner, c.Path),
            Intent.Clone c => StartCloneDialog(owner, c.Url, c.OpenedFromProtocolHandler, c.GitModuleChanged),
            Intent.CommandLineProcess c => StartCommandLineProcessDialog(owner, c.Command, c.Arguments),
            Intent.Commit c => StartCommitDialog(owner, c.CommitMessage, c.ShowOnlyWhenChanges),
            Intent.CommitDiff c => StartFormCommitDiff(c.ObjectId),
            Intent.CompareRevisions => StartCompareRevisionsDialog(owner),
            Intent.ContinueRebase => StartTheContinueRebaseDialog(owner),
            Intent.CreateBranch c => StartCreateBranchDialog(owner, c.ObjectId, c.NewBranchNamePrefix),
            Intent.CreateBranchFrom c => StartCreateBranchDialog(owner, c.Branch),
            Intent.CreateTag c => StartCreateTagDialog(owner, c.Revision),
            Intent.DeleteBranches c => StartDeleteBranchDialog(owner, c.Branches),
            Intent.DeleteRemoteBranch c => StartDeleteRemoteBranchDialog(owner, c.RemoteBranch),
            Intent.DeleteTag c => StartDeleteTagDialog(owner, c.Tag),
            Intent.EditFile c => StartFileEditorDialog(c.FileName, c.ShowWarning, c.LineNumber),
            Intent.EditGitAttributes => StartEditGitAttributesDialog(owner),
            Intent.EditGitIgnore c => StartEditGitIgnoreDialog(owner, c.LocalExcludes),
            Intent.FileHistory c => Run(() => StartFileHistoryDialog(owner, c.FileName, c.Revision, c.FilterByRevision, c.ShowBlame)),
            Intent.FixupCommit c => StartFixupCommitDialog(owner, c.Revision),
            Intent.FormatPatch => StartFormatPatchDialog(owner),
            Intent.GeneralSettings => StartGeneralSettingsDialog(owner),
            Intent.GitCommandLineProcess c => StartCommandLineProcessDialog(owner, c.Command),
            Intent.GitCommandProcess c => StartGitCommandProcessDialog(owner, c.Arguments),
            Intent.InitializeRepository c => StartInitializeDialog(owner, c.Directory, c.GitModuleChanged),
            Intent.MailMap => StartMailMapDialog(owner),
            Intent.MergeBranch c => StartMergeBranchDialog(owner, c.Branch),
            Intent.OpenSettings c => StartSettingsDialog(owner, c.InitialPage),
            Intent.OpenWithDifftool c => Run(() => OpenWithDifftool(owner, c.Revisions, c.FileName, c.OldFileName, c.DiffKind, c.IsTracked, c.CustomTool)),
            Intent.PluginSettings => StartPluginSettingsDialog(owner),
            Intent.Pull c => StartPullDialog(owner, c.RemoteBranch, c.Remote, c.PullAction),
            Intent.PullImmediately c => StartPullDialogAndPullImmediately(owner, c.RemoteBranch, c.Remote, c.PullAction),
            Intent.Push c => StartPushDialog(owner, c.PushOnShow, c.ForceWithLease, out _, c.BranchName),
            Intent.Rebase c => StartRebaseDialog(owner, c.From, c.To, c.Onto, c.Interactive, c.StartImmediately),
            Intent.RebaseWithAdvancedOptions c => StartRebaseDialogWithAdvOptions(owner, c.Onto, c.From),
            Intent.Remotes c => StartRemotesDialog(owner, c.PreselectRemote, c.PreselectLocal),
            Intent.RenameBranch c => StartRenameDialog(owner, c.Branch),
            Intent.RepoSettings => StartRepoSettingsDialog(owner),
            Intent.ResetChanges c => StartResetChangesDialog(owner, c.WorkTreeFiles, c.OnlyWorkTree),
            Intent.ResetCurrentBranch c => StartResetCurrentBranchDialog(owner, c.Branch),
            Intent.ResolveConflicts c => StartResolveConflictsDialog(owner, c.OfferCommit),
            Intent.RevertCommit c => StartRevertCommitDialog(owner, c.Revision),
            Intent.SparseWorkingCopy => StartSparseWorkingCopyDialog(owner),
            Intent.SquashCommit c => StartSquashCommitDialog(owner, c.Revision),
            Intent.Stash c => StartStashDialog(owner, c.ManageStashes, c.InitialStash),
            Intent.StashApply c => StashApply(owner, c.StashName),
            Intent.StashDrop c => StashDrop(owner, c.StashName),
            Intent.StashPop c => StashPop(owner, c.StashName),
            Intent.StashSave c => StashSave(owner, c.IncludeUntrackedFiles, c.KeepIndex, c.Message, c.SelectedFiles),
            Intent.StashStaged => StashStaged(owner),
            Intent.Submodules => StartSubmodulesDialog(owner),
            Intent.SyncSubmodules => StartSyncSubmodulesDialog(owner),
            Intent.UpdateSubmodule c => StartUpdateSubmoduleDialog(owner, c.SubmoduleLocalPath, c.SubmoduleParentPath),
            Intent.UpdateSubmodules => Run(() => UpdateSubmodules(owner)),
            Intent.UpdateSubmodulesDialog c => StartUpdateSubmodulesDialog(owner, c.SubmoduleLocalPath),
            Intent.VerifyDatabase => StartVerifyDatabaseDialog(owner),
            Intent.ViewPatch c => StartViewPatchDialog(owner, c.PatchFile),
            Intent.WorktreeCreate c => WorktreeCreate(owner, c.MainWorktreePath),
            Intent.WorktreeDelete c => WorktreeDelete(owner, c.WorktreePath),
            Intent.WorktreeSwitch c => WorktreeSwitch(owner, c.WorktreePath),
            _ => throw new NotSupportedException($"Unhandled UI command intent: {command.GetType().Name}")
        };

        static bool Run(Action action)
        {
            action();
            return true;
        }

        bool CherryPick(Intent.CherryPick c)
            => c.Revisions switch
            {
                null or [] => StartCherryPickDialog(owner, revision: null),
                [GitRevision single] => StartCherryPickDialog(owner, single),
                _ => StartCherryPickDialog(owner, c.Revisions),
            };
    }
}
