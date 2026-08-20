using GitExtensions.Extensibility.Git;
using GitExtUtils;
using GitUI.UICommandHandlers;
using Intent = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI;

// IUICommandBus dispatch: this switch is the composition root for UI command handlers - it
// constructs each handler with its dependencies and stays exhaustively compile-checked against
// the intent set. The handlers own the dialog logic; the bus is the only path - the Start*
// facade family is gone, and only the host-only bridges (ShowModelessForm and the two
// out-parameter dialogs) remain on the concrete class. See cross-platform-plan.md section 13.
partial class GitUICommands
{
    /// <summary>
    ///  The owner window used when an intent arrives through the portable bus, which carries
    ///  no owner: the active form, matching what call sites pass explicitly today.
    /// </summary>
    internal static IWin32Window? AmbientOwner => Form.ActiveForm;

    public bool Execute(Intent.IUICommand command)
        => Execute(command, AmbientOwner);

    public bool Execute(Intent.IUICommand command, object? ownerWindow)
        => Execute(command, ownerWindow as IWin32Window);

    internal bool Execute(Intent.IUICommand command, IWin32Window? owner)
    {
        return command switch
        {
            Intent.AddFiles c => new AddFilesHandler(this).Execute(c, owner),
            Intent.AddToGitIgnore c => new AddToGitIgnoreHandler(this).Execute(c, owner),
            Intent.AddUpstreamRemote c => new AddUpstreamRemoteHandler(this).Execute(c, owner),
            Intent.AmendCommit c => new AmendCommitHandler(this).Execute(c, owner),
            Intent.ApplyPatch c => new ApplyPatchHandler(this).Execute(c, owner),
            Intent.Archive c => new ArchiveHandler(this).Execute(c, owner),
            Intent.BatchFileProcess c => new BatchFileProcessHandler(this).Execute(c, owner),
            Intent.Browse c => new BrowseHandler(this).Execute(c, owner),
            Intent.CheckoutBranch c => new CheckoutBranchHandler(this).Execute(c, owner),
            Intent.CheckoutRemoteBranch c => new CheckoutRemoteBranchHandler(this).Execute(c, owner),
            Intent.CheckoutRevision c => new CheckoutRevisionHandler(this).Execute(c, owner),
            Intent.CherryPick c => new CherryPickHandler(this).Execute(c, owner),
            Intent.CleanupRepository c => new CleanupRepositoryHandler(this).Execute(c, owner),
            Intent.Clone c => new CloneHandler(this).Execute(c, owner),
            Intent.CloneForkFromHoster c => new CloneForkFromHosterHandler(this).Execute(c, owner),
            Intent.CommandLineProcess c => new CommandLineProcessHandler(this).Execute(c, owner),
            Intent.Commit c => new CommitHandler(this).Execute(c, owner),
            Intent.CommitDiff c => new CommitDiffHandler(this).Execute(c, owner),
            Intent.CompareRevisions c => new CompareRevisionsHandler(this).Execute(c, owner),
            Intent.ContinueRebase c => new ContinueRebaseHandler(this).Execute(c, owner),
            Intent.CreateBranch c => new CreateBranchHandler(this).Execute(c, owner),
            Intent.CreateBranchFrom c => new CreateBranchFromHandler(this).Execute(c, owner),
            Intent.CreatePullRequest c => new CreatePullRequestHandler(this).Execute(c, owner),
            Intent.CreateTag c => new CreateTagHandler(this).Execute(c, owner),
            Intent.DeleteBranches c => new DeleteBranchesHandler(this).Execute(c, owner),
            Intent.DeleteRemoteBranch c => new DeleteRemoteBranchHandler(this).Execute(c, owner),
            Intent.DeleteTag c => new DeleteTagHandler(this).Execute(c, owner),
            Intent.EditFile c => new EditFileHandler(this).Execute(c, owner),
            Intent.EditGitAttributes c => new EditGitAttributesHandler(this).Execute(c, owner),
            Intent.EditGitIgnore c => new EditGitIgnoreHandler(this).Execute(c, owner),
            Intent.FileHistory c => new FileHistoryHandler(this, Settings).Execute(c, owner),
            Intent.FixupCommit c => new FixupCommitHandler(this).Execute(c, owner),
            Intent.FormatPatch c => new FormatPatchHandler(this).Execute(c, owner),
            Intent.GeneralSettings c => new GeneralSettingsHandler(this).Execute(c, owner),
            Intent.GitCommandLineProcess c => new GitCommandLineProcessHandler(this).Execute(c, owner),
            Intent.GitCommandProcess c => new GitCommandProcessHandler(this).Execute(c, owner),
            Intent.InitializeRepository c => new InitializeRepositoryHandler(this).Execute(c, owner),
            Intent.MailMap c => new MailMapHandler(this).Execute(c, owner),
            Intent.MergeBranch c => new MergeBranchHandler(this).Execute(c, owner),
            Intent.OpenPluginSettings c => new OpenPluginSettingsHandler(this).Execute(c, owner),
            Intent.OpenRepository c => new OpenRepositoryHandler(this).Execute(c, owner),
            Intent.OpenSettings c => new OpenSettingsHandler(this).Execute(c, owner),
            Intent.OpenWithDifftool c => new OpenWithDifftoolHandler(this).Execute(c, owner),
            Intent.PluginSettings c => new PluginSettingsHandler(this).Execute(c, owner),
            Intent.Pull c => new PullHandler(this).Execute(c, owner),
            Intent.PullImmediately c => new PullHandler(this).Execute(c, owner),
            Intent.PullRequests c => new PullRequestsHandler(this).Execute(c, owner),
            Intent.Push c => new PushHandler(this).Execute(c, owner),
            Intent.Rebase c => new RebaseHandler(this).Execute(c, owner),
            Intent.RebaseWithAdvancedOptions c => new RebaseWithAdvancedOptionsHandler(this).Execute(c, owner),
            Intent.Remotes c => new RemotesHandler(this).Execute(c, owner),
            Intent.RenameBranch c => new RenameBranchHandler(this).Execute(c, owner),
            Intent.RepoSettings c => new RepoSettingsHandler(this).Execute(c, owner),
            Intent.ResetChanges c => new ResetChangesHandler(this).Execute(c, owner),
            Intent.ResetCurrentBranch c => new ResetCurrentBranchHandler(this).Execute(c, owner),
            Intent.ResolveConflicts c => new ResolveConflictsHandler(this).Execute(c, owner),
            Intent.RevertCommit c => new RevertCommitHandler(this).Execute(c, owner),
            Intent.SparseWorkingCopy c => new SparseWorkingCopyHandler(this).Execute(c, owner),
            Intent.SquashCommit c => new SquashCommitHandler(this).Execute(c, owner),
            Intent.Stash c => new StashHandler(this).Execute(c, owner),
            Intent.StashApply c => new StashApplyHandler(this).Execute(c, owner),
            Intent.StashDrop c => new StashDropHandler(this).Execute(c, owner),
            Intent.StashPop c => new StashPopHandler(this).Execute(c, owner),
            Intent.StashSave c => new StashSaveHandler(this).Execute(c, owner),
            Intent.StashStaged c => new StashStagedHandler(this).Execute(c, owner),
            Intent.Submodules c => new SubmodulesHandler(this).Execute(c, owner),
            Intent.SyncSubmodules c => new SyncSubmodulesHandler(this).Execute(c, owner),
            Intent.UpdateSubmodule c => new UpdateSubmoduleHandler(this).Execute(c, owner),
            Intent.UpdateSubmodules c => new UpdateSubmodulesHandler(this, Settings).Execute(c, owner),
            Intent.UpdateSubmodulesDialog c => new UpdateSubmodulesDialogHandler(this).Execute(c, owner),
            Intent.VerifyDatabase c => new VerifyDatabaseHandler(this).Execute(c, owner),
            Intent.ViewPatch c => new ViewPatchHandler(this).Execute(c, owner),
            Intent.WorktreeCreate c => new WorktreeCreateHandler(this, _serviceProvider.GetRequiredService<IGitExecutorProvider>()).Execute(c, owner),
            Intent.WorktreeDelete c => new WorktreeDeleteHandler(this).Execute(c, owner),
            Intent.WorktreeSwitch c => new WorktreeSwitchHandler(Settings).Execute(c, owner),
            _ => throw new NotSupportedException($"Unhandled UI command intent: {command.GetType().Name}")
        };
    }
}
