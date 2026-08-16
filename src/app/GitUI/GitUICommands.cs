using System.Collections.Immutable;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Text;
using GitCommands;
using GitCommands.Git;
using GitCommands.Settings;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtensions.Extensibility.Plugins;
using GitExtensions.Extensibility.Settings;
using GitExtUtils;
using GitUI.CommandsDialogs;
using GitUI.CommandsDialogs.RepoHosting;
using GitUI.CommandsDialogs.SettingsDialog;
using GitUI.CommandsDialogs.WorktreeDialog;
using GitUI.HelperDialogs;
using GitUIPluginInterfaces;
using JetBrains.Annotations;
using UICmd = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI;

/// <summary>Contains methods to invoke GitEx forms, dialogs, etc.</summary>
public sealed partial class GitUICommands : IGitUICommands, IServiceProvider
{
    internal const string BlameHistoryCommand = "blamehistory";
    internal const string FileHistoryCommand = "filehistory";

    internal const string FilterByRevisionArg = "--filter-by-revision";
    internal const string PathFilterArg = "--pathFilter";

    private readonly IServiceProvider _serviceProvider;
    private readonly ICommitTemplateManager _commitTemplateManager;
    private readonly IFullPathResolver _fullPathResolver;
    private readonly IFindFilePredicateProvider _findFilePredicateProvider;

    public static IServiceProvider EmptyServiceProvider = new ServiceContainer();

    public IGitModule Module { get; private set; }
    public ILockableNotifier RepoChangedNotifier { get; }
    public IBrowseRepo? BrowseRepo { get; set; }

    public GitUICommands(IServiceProvider serviceProvider, IGitModule module)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(module);

        _serviceProvider = serviceProvider;
        Module = module;

        _commitTemplateManager = new CommitTemplateManager(() => module);
        RepoChangedNotifier = new ActionNotifier(
            () => InvokeEvent(null, PostRepositoryChanged));

        _fullPathResolver = new FullPathResolver(() => Module.WorkingDir);
        _findFilePredicateProvider = new FindFilePredicateProvider();
    }

    #region Events

    public event EventHandler<GitUIEventArgs>? PreCheckoutRevision;
    public event EventHandler<GitUIPostActionEventArgs>? PostCheckoutRevision;

    public event EventHandler<GitUIEventArgs>? PreCheckoutBranch;
    public event EventHandler<GitUIPostActionEventArgs>? PostCheckoutBranch;

    public event EventHandler<GitUIEventArgs>? PreCommit;
    public event EventHandler<GitUIPostActionEventArgs>? PostCommit;

    public event EventHandler<GitUIPostActionEventArgs>? PostEditGitIgnore;

    public event EventHandler<GitUIPostActionEventArgs>? PostSettings;

    public event EventHandler<GitUIPostActionEventArgs>? PostUpdateSubmodules;

    public event EventHandler<GitUIEventArgs>? PostBrowseInitialize;

    /// <summary>
    /// listeners for changes being made to repository
    /// </summary>
    public event EventHandler<GitUIEventArgs>? PostRepositoryChanged;

    public event EventHandler<GitUIEventArgs>? PostRegisterPlugin;

    // Field-like event delegates are only readable inside the declaring class; these internal
    // accessors hand them to the UI command handlers, which raise them through DoActionOnRepo.
    internal EventHandler<GitUIEventArgs>? PreCheckoutBranchEvent => PreCheckoutBranch;
    internal EventHandler<GitUIPostActionEventArgs>? PostCheckoutBranchEvent => PostCheckoutBranch;
    internal EventHandler<GitUIEventArgs>? PreCheckoutRevisionEvent => PreCheckoutRevision;
    internal EventHandler<GitUIPostActionEventArgs>? PostCheckoutRevisionEvent => PostCheckoutRevision;
    internal EventHandler<GitUIEventArgs>? PreCommitEvent => PreCommit;
    internal EventHandler<GitUIPostActionEventArgs>? PostCommitEvent => PostCommit;
    internal EventHandler<GitUIPostActionEventArgs>? PostEditGitIgnoreEvent => PostEditGitIgnore;
    internal EventHandler<GitUIPostActionEventArgs>? PostSettingsEvent => PostSettings;
    internal EventHandler<GitUIPostActionEventArgs>? PostUpdateSubmodulesEvent => PostUpdateSubmodules;

    /// <summary>Injectable settings for UI command handlers (M3.4 seam).</summary>
    internal ISettings Settings { get; } = new AppSettingsAdapter();

    #endregion

    public object? GetService(Type serviceType) => _serviceProvider.GetService(serviceType);

    private bool RequiresValidWorkingDir(object? owner)
    {
        if (!Module.IsValidGitWorkingDir())
        {
            MessageBoxes.NotValidGitDirectory(owner as IWin32Window);
            return false;
        }

        return true;
    }

    public void StartBatchFileProcessDialog(string batchFile)
        => Execute(new UICmd.BatchFileProcess(batchFile), owner: null);

    public bool StartCommandLineProcessDialog(IWin32Window? owner, IGitCommand command)
        => Execute(new UICmd.GitCommandLineProcess(command), owner);

    public bool StartCommandLineProcessDialog(IWin32Window? owner, string? command, ArgumentString arguments)
        => Execute(new UICmd.CommandLineProcess(command, arguments), owner);

    public bool StartGitCommandProcessDialog(IWin32Window? owner, ArgumentString arguments)
        => Execute(new UICmd.GitCommandProcess(arguments), owner);

    public bool StartDeleteBranchDialog(IWin32Window? owner, string branch)
        => Execute(new UICmd.DeleteBranches([branch]), owner);

    public bool StartDeleteBranchDialog(IWin32Window? owner, IEnumerable<string> branches)
        => Execute(new UICmd.DeleteBranches([.. branches]), owner);

    public bool StartDeleteRemoteBranchDialog(IWin32Window? owner, string remoteBranch)
        => Execute(new UICmd.DeleteRemoteBranch(remoteBranch), owner);

    public bool StartCheckoutRevisionDialog(IWin32Window? owner, string? revision = null)
        => Execute(new UICmd.CheckoutRevision(revision), owner);

    public bool StartResetCurrentBranchDialog(IWin32Window? owner, string branch)
        => Execute(new UICmd.ResetCurrentBranch(branch), owner);

    public bool StashSave(IWin32Window? owner, bool includeUntrackedFiles, bool keepIndex = false, string message = "", IReadOnlyList<string>? selectedFiles = null)
        => Execute(new UICmd.StashSave(includeUntrackedFiles, keepIndex, message, selectedFiles), owner);

    public bool StashStaged(IWin32Window? owner)
        => Execute(new UICmd.StashStaged(), owner);

    public bool StashPop(IWin32Window? owner, string stashName = "")
        => Execute(new UICmd.StashPop(stashName), owner);

    public bool StashDrop(IWin32Window? owner, string stashName)
        => Execute(new UICmd.StashDrop(stashName), owner);

    public bool StashApply(IWin32Window? owner, string stashName)
        => Execute(new UICmd.StashApply(stashName), owner);

    public bool WorktreeDelete(IWin32Window? owner, string worktreePath)
        => Execute(new UICmd.WorktreeDelete(worktreePath), owner);

    public bool WorktreeSwitch(IWin32Window? owner, string worktreePath)
        => Execute(new UICmd.WorktreeSwitch(worktreePath), owner);

    public bool WorktreeCreate(IWin32Window? owner, string mainWorktreePath)
        => Execute(new UICmd.WorktreeCreate(mainWorktreePath), owner);

    internal static FormBrowse? FindFormBrowse(IWin32Window? window)
    {
        if (window is FormBrowse browse)
        {
            return browse;
        }

        if (window is Form form)
        {
            while (form.Owner is not null)
            {
                if (form.Owner is FormBrowse ownerBrowse)
                {
                    return ownerBrowse;
                }

                form = form.Owner;
            }
        }

        return null;
    }

    public void ShowModelessForm(IWin32Window? owner, bool requiresValidWorkingDir,
        EventHandler<GitUIEventArgs>? preEvent, EventHandler<GitUIPostActionEventArgs>? postEvent, Func<Form> provideForm)
    {
        if (requiresValidWorkingDir && !RequiresValidWorkingDir(owner))
        {
            return;
        }

        if (!InvokeEvent(owner, preEvent))
        {
            return;
        }

        Form form = provideForm();

        void FormClosed(object? sender, FormClosedEventArgs e)
        {
            form.FormClosed -= FormClosed;
            InvokePostEvent(owner, true, postEvent);
        }

        form.FormClosed += FormClosed;
        form.ShowInTaskbar = true;

        if (Application.OpenForms.Count > 0)
        {
            form.Show();
        }
        else
        {
            form.ShowDialog();
        }
    }

    /// <param name="requiresValidWorkingDir">If action requires valid working directory.</param>
    /// <param name="owner">Owner window.</param>
    /// <param name="changesRepo">if successfully done action changes repo state.</param>
    /// <param name="preEvent">Event invoked before performing action.</param>
    /// <param name="postEvent">Event invoked after performing action.</param>
    /// <param name="action">Action to do. Return true to indicate that the action was successfully done.</param>
    /// <returns>true if action was successfully done, false otherwise.</returns>
    internal bool DoActionOnRepo(
        IWin32Window? owner,
        [InstantHandle] Func<bool> action,
        bool requiresValidWorkingDir = true,
        bool changesRepo = true,
        EventHandler<GitUIEventArgs>? preEvent = null,
        EventHandler<GitUIPostActionEventArgs>? postEvent = null)
    {
        bool actionDone = false;
        RepoChangedNotifier.Lock();
        try
        {
            if (requiresValidWorkingDir && !RequiresValidWorkingDir(owner))
            {
                return false;
            }

            if (!InvokeEvent(owner, preEvent))
            {
                return false;
            }

            try
            {
                actionDone = action();
            }
            finally
            {
                InvokePostEvent(owner, actionDone, postEvent);
            }
        }
        finally
        {
            // The action may not have required a valid working directory to run, but if there isn't one,
            // we shouldn't send a "repo changed" notify.
            bool requestNotify = actionDone && changesRepo && Module.IsValidGitWorkingDir();
            RepoChangedNotifier.UnLock(requestNotify);
        }

        return actionDone;
    }

    public bool DoActionOnRepo(Func<bool> action)
    {
        return DoActionOnRepo(owner: null, action, requiresValidWorkingDir: false);
    }

    #region Checkout

    public bool StartCheckoutBranch(IWin32Window? owner, string branch = "", bool remote = false, IReadOnlyList<ObjectId>? containObjectIds = null)
        => Execute(new UICmd.CheckoutBranch(branch, remote, containObjectIds), owner);

    public bool StartCheckoutBranch(IWin32Window? owner, IReadOnlyList<ObjectId>? containObjectIds)
        => Execute(new UICmd.CheckoutBranch(ContainObjectIds: containObjectIds), owner);

    public bool StartCheckoutRemoteBranch(IWin32Window? owner, string branch)
        => Execute(new UICmd.CheckoutRemoteBranch(branch), owner);

    #endregion

    /// <summary>
    /// Launches a new GE instance.
    /// </summary>
    /// <param name="arguments">The command line arguments.</param>
    /// <param name="workingDir">The working directory for the new process.</param>
    /// <returns>The <see cref="IProcess"/> object for controlling the launched instance.</returns>
    public static IProcess Launch(string arguments, string workingDir = "")
        => new Executable(Application.ExecutablePath, workingDir).Start(arguments);

    /// <summary>
    /// Launch FormBrowse in a new GE instance.
    /// </summary>
    /// <param name="workingDir">The working directory for the new process.</param>
    /// <param name="selectedId">The optional commit to be selected.</param>
    /// <param name="firstId">The first commit to be selected, the first commit in a diff.</param>
    internal static void LaunchBrowse(string workingDir = "", ObjectId selectedId = default, ObjectId firstId = default)
    {
        if (!Directory.Exists(workingDir))
        {
            MessageBoxes.GitExtensionsDirectoryDoesNotExist(owner: null, workingDir);
            return;
        }

        StringBuilder arguments = new("browse");

        if (selectedId.IsZero)
        {
            selectedId = firstId;
            firstId = default;
        }

        if (!selectedId.IsZero)
        {
            arguments.Append(" -commit=").Append(selectedId);
            if (!firstId.IsZero)
            {
                arguments.Append(',').Append(firstId);
            }
        }

        Launch(arguments.ToString(), workingDir);
    }

    public bool StartCompareRevisionsDialog(IWin32Window? owner = null)
        => Execute(new UICmd.CompareRevisions(), owner);

    public bool StartAddFilesDialog(IWin32Window? owner, string? addFiles = null)
        => Execute(new UICmd.AddFiles(addFiles), owner);

    public bool StartCreateBranchDialog(IWin32Window? owner, string? branch)
        => Execute(new UICmd.CreateBranchFrom(branch!), owner);

    public bool StartCreateBranchDialog(IWin32Window? owner = null, ObjectId objectId = default, string? newBranchNamePrefix = null)
        => Execute(new UICmd.CreateBranch(objectId, newBranchNamePrefix), owner);

    public bool StartCloneDialog(IWin32Window? owner, string? url = null, bool openedFromProtocolHandler = false, EventHandler<GitModuleEventArgs>? gitModuleChanged = null)
        => Execute(new UICmd.Clone(url, openedFromProtocolHandler, gitModuleChanged), owner);

    public bool StartCloneDialog(IWin32Window? owner, string url, EventHandler<GitModuleEventArgs> gitModuleChanged)
        => Execute(new UICmd.Clone(url, false, gitModuleChanged), owner);

    public bool StartCleanupRepositoryDialog(IWin32Window? owner = null, string? path = null)
        => Execute(new UICmd.CleanupRepository(path), owner);

    public bool StartSquashCommitDialog(IWin32Window? owner, GitRevision revision)
        => Execute(new UICmd.SquashCommit(revision), owner);

    public bool StartFixupCommitDialog(IWin32Window? owner, GitRevision revision)
        => Execute(new UICmd.FixupCommit(revision), owner);

    public bool StartAmendCommitDialog(IWin32Window? owner, GitRevision revision)
        => Execute(new UICmd.AmendCommit(revision), owner);

    public bool StartCommitDialog(IWin32Window? owner, string? commitMessage = null, bool showOnlyWhenChanges = false)
        => Execute(new UICmd.Commit(commitMessage, showOnlyWhenChanges), owner);

    public bool StartInitializeDialog(IWin32Window? owner = null, string? dir = null, EventHandler<GitModuleEventArgs>? gitModuleChanged = null)
        => Execute(new UICmd.InitializeRepository(dir, gitModuleChanged), owner);

    public bool StartPullDialogAndPullImmediately(IWin32Window? owner = null, string? remoteBranch = null, string? remote = null, GitPullAction pullAction = GitPullAction.None)
    {
        return StartPullDialogAndPullImmediately(out _, owner, remoteBranch, remote, pullAction);
    }

    /// <param name="pullCompleted">true if pull completed with no errors.</param>
    /// <returns>if revision grid should be refreshed.</returns>
    public bool StartPullDialogAndPullImmediately(out bool pullCompleted, IWin32Window? owner = null, string? remoteBranch = null, string? remote = null, GitPullAction pullAction = GitPullAction.None)
    {
        return new UICommandHandlers.PullHandler(this).Execute(new UICmd.PullImmediately(remoteBranch, remote, pullAction), owner, out pullCompleted);
    }

    public bool StartPullDialog(IWin32Window? owner = null, string? remoteBranch = null, string? remote = null, GitPullAction pullAction = GitPullAction.None)
        => Execute(new UICmd.Pull(remoteBranch, remote, pullAction), owner);

    public bool StartViewPatchDialog(IWin32Window? owner, string? patchFile = null)
        => Execute(new UICmd.ViewPatch(patchFile), owner);

    public bool StartFormCommitDiff(ObjectId objectId)
        => Execute(new UICmd.CommitDiff(objectId), owner: null);

    public bool StartViewPatchDialog(string patchFile)
        => Execute(new UICmd.ViewPatch(patchFile), owner: null);

    public bool StartSparseWorkingCopyDialog(IWin32Window? owner)
        => Execute(new UICmd.SparseWorkingCopy(), owner);

    public void AddCommitTemplate(string key, Func<string> addingText, Image? icon, bool isRegex)
    {
        _commitTemplateManager.Register(key, addingText, icon, isRegex);
    }

    public void RemoveCommitTemplate(string key)
    {
        _commitTemplateManager.Unregister(key);
    }

    public bool StartFormatPatchDialog(IWin32Window? owner = null)
        => Execute(new UICmd.FormatPatch(), owner);

    public bool StartStashDialog(IWin32Window? owner = null, bool manageStashes = true, string? initialStash = null)
        => Execute(new UICmd.Stash(manageStashes, initialStash), owner);

    /// <summary>
    /// Reset all changes to HEAD.
    /// </summary>
    /// <param name="owner">Owner window.</param>
    /// <param name="workTreeFiles">Worktree files, to determine the status for the popup dialog.</param>
    /// <param name="onlyWorkTree">Only reset worktree files.</param>
    /// <returns><see langword="true"/> if executed.</returns>
    public bool StartResetChangesDialog(IWin32Window? owner, IReadOnlyCollection<GitItemStatus> workTreeFiles, bool onlyWorkTree)
        => Execute(new UICmd.ResetChanges(workTreeFiles, onlyWorkTree), owner);

    /// <summary>
    ///  Resets changes of passed files or folders (with absolute or relative paths).<br/>
    ///  If no <paramref name="names"/> are passed all changes are reset.
    /// </summary>
    /// <returns><see langword="false"/> if cancelled or if no items match.</returns>
    private bool StartResetChangesDialog(string[] names)
    {
        ImmutableHashSet<string> relativeFilePaths = [.. names.Select(fileName => Path.GetRelativePath(Module.WorkingDir, fileName).ToPosixPath())];
        ImmutableHashSet<string> relativeFolderPaths = [.. relativeFilePaths.Where(name => Directory.Exists(Path.Join(Module.WorkingDir, name)))];
        bool allItems = relativeFolderPaths.Contains(".");
        GitItemStatus[] selectedItems = [.. Module.GetAllChangedFilesWithSubmodulesStatus(cancellationToken: default)
            .Where(item => allItems || relativeFilePaths.Contains(item.Name) || relativeFolderPaths.Any(folder => item.Path.Value.StartsWith(folder)))];

        // Show a form asking the user if they want to reset the changes.
        FormResetChanges.ActionEnum resetType = FormResetChanges.ShowResetDialog(null, hasExistingFiles: selectedItems.Any(item => item.IsTracked), hasNewFiles: selectedItems.Any(item => item.IsNew));

        if (resetType == FormResetChanges.ActionEnum.Cancel)
        {
            return false;
        }

        using (WaitCursorScope.Enter())
        {
            // Reset all changes.
            if (names.Length == 0)
            {
                return Module.ResetAllChanges(clean: resetType == FormResetChanges.ActionEnum.ResetAndDelete, onlyWorkTree: false);
            }

            if (selectedItems.Length == 0)
            {
                return false;
            }

            Module.ResetChanges(resetId: default, selectedItems, resetAndDelete: resetType == FormResetChanges.ActionEnum.ResetAndDelete, _fullPathResolver, out StringBuilder output, progressAction: null);
            if (output.Length > 0)
            {
                MessageBoxes.Show(owner: null, output.ToString(), TranslatedStrings.ResetChangesCaption, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        return true;
    }

    public bool StartRevertCommitDialog(IWin32Window? owner, GitRevision revision)
        => Execute(new UICmd.RevertCommit(revision), owner);

    public bool StartResolveConflictsDialog(IWin32Window? owner = null, bool offerCommit = true)
        => Execute(new UICmd.ResolveConflicts(offerCommit), owner);

    public bool StartCherryPickDialog(IWin32Window? owner = null, GitRevision? revision = null)
        => Execute(new UICmd.CherryPick(revision is null ? null : [revision]), owner);

    public bool StartCherryPickDialog(IWin32Window? owner, IEnumerable<GitRevision> revisions)
    {
        ArgumentNullException.ThrowIfNull(revisions);

        return Execute(new UICmd.CherryPick([.. revisions]), owner);
    }

    /// <summary>Start Merge dialog, using the specified branch.</summary>
    /// <param name="owner">Owner of the dialog.</param>
    /// <param name="branch">Branch to merge into the current branch.</param>
    public bool StartMergeBranchDialog(IWin32Window? owner, string? branch)
        => Execute(new UICmd.MergeBranch(branch), owner);

    public bool StartCreateTagDialog(IWin32Window? owner = null, GitRevision? revision = null)
        => Execute(new UICmd.CreateTag(revision), owner);

    public bool StartDeleteTagDialog(IWin32Window? owner, string? tag)
        => Execute(new UICmd.DeleteTag(tag), owner);

    public bool StartEditGitIgnoreDialog(IWin32Window? owner, bool localExcludes)
        => Execute(new UICmd.EditGitIgnore(localExcludes), owner);

    public bool StartAddToGitIgnoreDialog(IWin32Window? owner, bool localExclude, params string[] filePattern)
        => Execute(new UICmd.AddToGitIgnore(localExclude, filePattern), owner);

    public bool StartSettingsDialog(IWin32Window? owner, SettingsPageReference? initialPage = null)
        => Execute(new UICmd.OpenSettings(initialPage), owner);

    public bool StartSettingsDialog(IGitPlugin gitPlugin)
    {
        // TODO: how to pass the main dialog as owner of the SettingsDialog (first parameter):
        return StartSettingsDialog(null, new SettingsPageReferenceByPlugin(gitPlugin));
    }

    public bool StartSettingsDialog(Type pageType)
        => Execute(new UICmd.OpenSettings(new SettingsPageReferenceByType(pageType)), owner: null);

    /// <summary>
    /// Open the archive dialog.
    /// </summary>
    /// <param name="revision">Revision to create an archive from.</param>
    /// <param name="revision2">Revision for differential archive.</param>
    /// <param name="path">Files path for archive.</param>
    public bool StartArchiveDialog(IWin32Window? owner = null, GitRevision? revision = null, GitRevision? revision2 = null, string? path = null)
        => Execute(new UICmd.Archive(revision, revision2, path), owner);

    public bool StartMailMapDialog(IWin32Window? owner = null)
        => Execute(new UICmd.MailMap(), owner);

    public bool StartVerifyDatabaseDialog(IWin32Window? owner = null)
        => Execute(new UICmd.VerifyDatabase(), owner);

    /// <inheritdoc/>
    public bool StartRemotesDialog(IWin32Window? owner, string? preselectRemote = null, string? preselectLocal = null)
        => Execute(new UICmd.Remotes(preselectRemote, preselectLocal), owner);

    public bool StartRebase(IWin32Window? owner, string onto)
        => Execute(new UICmd.Rebase(onto, StartImmediately: true), owner);

    public bool StartTheContinueRebaseDialog(IWin32Window? owner)
        => Execute(new UICmd.ContinueRebase(), owner);

    public bool StartInteractiveRebase(IWin32Window? owner, string onto)
        => Execute(new UICmd.Rebase(onto, Interactive: true, StartImmediately: true), owner);

    public bool StartRebaseDialogWithAdvOptions(IWin32Window? owner, string onto, string from = "")
        => Execute(new UICmd.RebaseWithAdvancedOptions(onto, from), owner);

    public bool StartRebaseDialog(IWin32Window? owner, string? onto)
        => Execute(new UICmd.Rebase(onto), owner);

    public bool StartRebaseDialog(IWin32Window? owner, string? from, string? to, string? onto, bool interactive = false, bool startRebaseImmediately = true)
        => Execute(new UICmd.Rebase(onto, from, to, interactive, startRebaseImmediately), owner);

    public bool StartRenameDialog(IWin32Window? owner, string branch)
        => Execute(new UICmd.RenameBranch(branch), owner);

    public bool StartSubmodulesDialog(IWin32Window? owner)
        => Execute(new UICmd.Submodules(), owner);

    public bool StartUpdateSubmodulesDialog(IWin32Window? owner, string submoduleLocalPath = "")
        => Execute(new UICmd.UpdateSubmodulesDialog(submoduleLocalPath), owner);

    public bool StartUpdateSubmoduleDialog(IWin32Window? owner, string submoduleLocalPath, string submoduleParentPath)
        => Execute(new UICmd.UpdateSubmodule(submoduleLocalPath, submoduleParentPath), owner);

    public bool StartSyncSubmodulesDialog(IWin32Window? owner)
        => Execute(new UICmd.SyncSubmodules(), owner);

    public void UpdateSubmodules(IWin32Window? owner)
        => Execute(new UICmd.UpdateSubmodules(), owner);

    public bool StartGeneralSettingsDialog(IWin32Window? owner)
        => Execute(new UICmd.GeneralSettings(), owner);

    public bool StartPluginSettingsDialog(IWin32Window? owner)
        => Execute(new UICmd.PluginSettings(), owner);

    public bool StartRepoSettingsDialog(IWin32Window? owner)
        => Execute(new UICmd.RepoSettings(), owner);

    /// <summary>
    /// Open Browse - main GUI including dashboard.
    /// </summary>
    /// <param name="owner">current window owner.</param>
    /// <param name="args">The start up arguments.</param>
    public bool StartBrowseDialog(IWin32Window? owner, BrowseArguments? args = null)
        => Execute(new UICmd.Browse(args), owner);

    public void StartFileHistoryDialog(IWin32Window? owner, string fileName, GitRevision? revision = null, bool filterByRevision = false, bool showBlame = false)
        => Execute(new UICmd.FileHistory(fileName, revision, filterByRevision, showBlame), owner);

    public void OpenWithDifftool(IWin32Window? owner, IReadOnlyList<GitRevision?> revisions, string fileName, string? oldFileName, RevisionDiffKind diffKind, bool isTracked, string? customTool = null)
        => Execute(new UICmd.OpenWithDifftool(revisions, fileName, oldFileName, diffKind, isTracked, customTool), owner);

    public bool StartPushDialog(IWin32Window? owner, bool pushOnShow, bool forceWithLease, out bool pushCompleted, string? branchName = null)
        => new UICommandHandlers.PushHandler(this).Execute(new UICmd.Push(pushOnShow, forceWithLease, branchName), owner, out pushCompleted);

    public bool StartPushDialog(IWin32Window? owner, bool pushOnShow)
        => Execute(new UICmd.Push(pushOnShow), owner);

    public bool StartApplyPatchDialog(IWin32Window? owner, string? patchFile = null)
        => Execute(new UICmd.ApplyPatch(patchFile), owner);

    public bool StartEditGitAttributesDialog(IWin32Window? owner = null)
        => Execute(new UICmd.EditGitAttributes(), owner);

    private bool InvokeEvent(IWin32Window? ownerForm, EventHandler<GitUIEventArgs>? gitUIEventHandler)
    {
        if (gitUIEventHandler is not null)
        {
            try
            {
                GitUIEventArgs e = new(ownerForm, this);
                gitUIEventHandler.Invoke(this, e);
                return !e.Cancel;
            }
            catch (Exception ex)
            {
                MessageBoxes.Show(ex.Message, "Exception", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        return true;
    }

    private void InvokePostEvent(IWin32Window? ownerForm, bool actionDone, EventHandler<GitUIPostActionEventArgs>? gitUIEventHandler)
    {
        if (gitUIEventHandler is not null)
        {
            GitUIPostActionEventArgs e = new(ownerForm, this, actionDone);
            gitUIEventHandler(this, e);
        }
    }

    private void WrapRepoHostingCall(string name, IRepositoryHostPlugin gitHoster, Action<IRepositoryHostPlugin> call)
    {
        if (!gitHoster.ConfigurationOk)
        {
            GitUIEventArgs eventArgs = new(null, this);
            gitHoster.Execute(eventArgs);
        }

        if (gitHoster.ConfigurationOk)
        {
            try
            {
                call(gitHoster);
            }
            catch (Exception ex)
            {
                MessageBoxes.Show(
                    string.Format("ERROR: {0} failed. Message: {1}\r\n\r\n{2}", name, ex.Message, ex.StackTrace),
                    "Error! :(", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    public void StartCloneForkFromHoster(IWin32Window? owner, IRepositoryHostPlugin gitHoster, EventHandler<GitModuleEventArgs>? gitModuleChanged)
    {
        WrapRepoHostingCall(TranslatedStrings.ForkCloneRepo, gitHoster, gh =>
        {
            using ForkAndCloneForm frm = new(this, gh, gitModuleChanged);
            frm.ShowDialog(owner);
        });
    }

    public void StartPullRequestsDialog(IWin32Window? owner, IRepositoryHostPlugin gitHoster)
    {
        WrapRepoHostingCall(TranslatedStrings.ViewPullRequest, gitHoster,
                            gh =>
                            {
                                ViewPullRequestsForm frm = new(this, gh) { ShowInTaskbar = true };
                                frm.Show(owner);
                            });
    }

    public void AddUpstreamRemote(IWin32Window? owner, IRepositoryHostPlugin gitHoster)
    {
        WrapRepoHostingCall(TranslatedStrings.AddUpstreamRemote, gitHoster,
                            gh =>
                            {
                                ThreadHelper.FileAndForget(async () =>
                                {
                                    string? remoteName = await gh.AddUpstreamRemoteAsync();
                                    if (!string.IsNullOrEmpty(remoteName))
                                    {
                                        StartPullDialogAndPullImmediately(owner, remoteBranch: null, remoteName, GitPullAction.Fetch);
                                    }
                                });
                            });
    }

    public void StartCreatePullRequest(IWin32Window? owner)
    {
        List<IRepositoryHostPlugin> relevantHosts =
            [.. PluginRegistry.GitHosters.Where(gh => gh.GitModuleIsRelevantToMe())];

        if (relevantHosts.Count == 0)
        {
            MessageBoxes.Show(owner, "Could not find any repo hosts for current working directory", TranslatedStrings.Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        else if (relevantHosts.Count == 1)
        {
            StartCreatePullRequest(owner, relevantHosts[0]);
        }
        else
        {
            MessageBoxes.Show("StartCreatePullRequest:Selection not implemented!", TranslatedStrings.Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void StartCreatePullRequest(IWin32Window? owner, IRepositoryHostPlugin gitHoster, string? chooseRemote = null, string? chooseBranch = null)
    {
        WrapRepoHostingCall(
            TranslatedStrings.CreatePullRequest,
            gitHoster,
            gh =>
            {
                CreatePullRequestForm form = new(this, gh, chooseRemote, chooseBranch)
                {
                    ShowInTaskbar = true
                };

                form.Show(owner);
            });
    }

    public bool RunCommand(IReadOnlyList<string> args)
    {
        IReadOnlyDictionary<string, string?> arguments = InitializeArguments(args);

        if (args.Count <= 1)
        {
            return false;
        }

        string command = args[1];

        if (command == "blame" && args.Count <= 2)
        {
            MessageBoxes.Show("Cannot open blame, there is no file selected.", "Blame", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        if (command == "difftool" && args.Count <= 2)
        {
            MessageBoxes.Show("Cannot open difftool, there is no file selected.", "Difftool", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        if (command is (BlameHistoryCommand or FileHistoryCommand) && args.Count <= 2)
        {
            MessageBoxes.Show("Cannot open blame / file history, there is no file selected.", "Blame / file history", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        if (command == "fileeditor" && args.Count <= 2)
        {
            MessageBoxes.Show("Cannot open file editor, there is no file selected.", "File editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        if (command == "revert" && args.Count <= 2)
        {
            MessageBoxes.Show("Cannot open revert, there is no file selected.", "Revert", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        return RunCommandBasedOnArgument(args, arguments);
    }

    // Please update FormCommandlineHelp if you add or change commands
    private bool RunCommandBasedOnArgument(IReadOnlyList<string> args, IReadOnlyDictionary<string, string?> arguments)
    {
#pragma warning disable SA1025 // Code should not contain multiple whitespace in a row
        string command = args[1];
        switch (command)
        {
            case "about":
                Application.Run(new FormAbout
                {
                    StartPosition = FormStartPosition.CenterScreen
                });
                return true;
            case "add":
            case "addfiles":
                // If filenames have been specified, quote them and pass them to the dialog, else pass '.' for current dir.
                return StartAddFilesDialog(owner: null, addFiles: args.Count < 3 ? "." : string.Join(' ', args.Skip(2).Select(file => file.Quote())));
            case "apply":       // [filename]
            case "applypatch":
                return StartApplyPatchDialog(null, args.Count == 3 ? args[2] : "");
            case "blame":       // filename
                return RunBlameCommand(args);
            case "branch":
                return StartCreateBranchDialog();
            case "browse":      // [path] [--pathFilter=filname] [-filter] [-commit=selected[,first]]
                return RunBrowseCommand(args);
            case "checkout":
            case "checkoutbranch":
                return StartCheckoutBranch(null);
            case "checkoutrevision":
                return StartCheckoutRevisionDialog(null);
            case "cherry":
                return StartCherryPickDialog();
            case "cleanup":
                return StartCleanupRepositoryDialog();
            case "clone":       // [path]
                return RunCloneCommand(args);
            case "commit":      // [--quiet]
                return Commit(arguments);
            case "difftool":    // filename
                try
                {
                    Module.OpenWithDifftool(args[2]);
                    return true;
                }
                catch
                {
                    return false;
                }

            case BlameHistoryCommand:
            case FileHistoryCommand:
                // filename [revision [--filter-by-revision]]
                if (Module.WorkingDir.TrimEnd('\\') == Path.GetFullPath(args[2]) && Module.SuperprojectModule is not null)
                {
                    Module = Module.SuperprojectModule;
                }

                return RunFileHistoryCommand(args, showBlame: command == BlameHistoryCommand);
            case "fileeditor":  // filename
                return StartFileEditorDialog(args[2]);
            case "formatpatch":
                return StartFormatPatchDialog();
            case "gitignore":
                return StartEditGitIgnoreDialog(null, false);
            case "init":        // [path]
                return RunInitCommand(args);
            case "merge":       // [--branch name]
                return RunMergeCommand(arguments);
            case "mergeconflicts":
            case "mergetool":   // [--quiet]
                return RunMergeToolOrConflictCommand(arguments);
            case "openrepo":    // [path]
                return RunOpenRepoCommand(args);
            case "pull":        // [--rebase] [--merge] [--fetch] [--quiet] [--remotebranch name]
                return Pull(arguments);
            case "push":        // [--quiet]
                return Push(arguments);
            case "rebase":      // [--branch name]
                return RunRebaseCommand(arguments);
            case "remotes":
                return StartRemotesDialog(owner: null);
            case "revert":
            case "reset":
                // If names of files or folders have been specified, pass them
                return StartResetChangesDialog(names: [.. args.Skip(2)]);
            case "searchfile":
                return RunSearchFileCommand();
            case "settings":
                return StartSettingsDialog(owner: null);
            case "stash":
                return StartStashDialog();
            case "synchronize": // [--rebase] [--merge] [--fetch] [--quiet]
                return RunSynchronizeCommand(arguments);
            case "tag":
                return StartCreateTagDialog();
            case "viewdiff":
                return StartCompareRevisionsDialog();
            case "viewpatch":   // [filename]
                return StartViewPatchDialog(args.Count == 3 ? args[2] : "");
            case "uninstall":
                return UninstallEditor();
            default:
                if (args[1].StartsWith("git://") || args[1].StartsWith("http://") || args[1].StartsWith("https://"))
                {
                    return StartCloneDialog(null, args[1], true);
                }

                if (args[1].StartsWith("github-windows://openRepo/"))
                {
                    return StartCloneDialog(null, args[1].Replace("github-windows://openRepo/", ""), true);
                }

                if (args[1].StartsWith("github-mac://openRepo/"))
                {
                    return StartCloneDialog(null, args[1].Replace("github-mac://openRepo/", ""), true);
                }

                // User supplied a path. Open the repository if its a valid path
                string? dir = !string.IsNullOrWhiteSpace(command) && File.Exists(command) ? Path.GetDirectoryName(command) : command;
                if (args.Count == 2 && Directory.Exists(dir))
                {
                    LaunchBrowse(dir);
                    return true;
                }

                break;
        }
#pragma warning restore SA1025 // Code should not contain multiple whitespace in a row

        Application.Run(new FormCommandlineHelp { StartPosition = FormStartPosition.CenterScreen });
        return true;
    }

    private static bool UninstallEditor()
    {
        GitConfigSettings globalSettings = new(new Executable(AppSettings.GitCommand), GitSettingLevel.Global);
        string? coreEditor = globalSettings.GetValue("core.editor");
        string? path = AppSettings.GetInstallDir().ToPosixPath();
        if (path is not null && coreEditor?.Contains(path, StringComparison.InvariantCultureIgnoreCase) is true)
        {
            globalSettings.SetValue("core.editor", value: null);
            globalSettings.Save();
        }

        return true;
    }

    private bool RunMergeCommand(IReadOnlyDictionary<string, string?> arguments)
    {
        arguments.TryGetValue("branch", out string? branch);

        return StartMergeBranchDialog(null, branch);
    }

    private bool RunSearchFileCommand()
    {
        SearchWindow<string> searchWindow = new(FindFileMatches);
        Application.Run(searchWindow);
        if (searchWindow.SelectedItem is not null)
        {
            // We need to return the file that has been found, the visual studio plugin uses the return value
            // to open the selected file.
            Console.WriteLine(Path.Combine(Module.WorkingDir, searchWindow.SelectedItem));
            return true;
        }

        return false;
    }

    private bool RunBrowseCommand(IReadOnlyList<string> args)
    {
        string arg = GetParameterOrEmptyStringAsDefault(args, "-commit");
        if (arg == "")
        {
            return StartBrowseDialog(owner: null,
                new BrowseArguments
                {
                    RevFilter = GetParameterOrEmptyStringAsDefault(args, "-filter"),
                    PathFilter = GetParameterOrEmptyStringAsDefault(args, PathFilterArg),
                    IsFileHistoryMode = args.Any(arg => arg.StartsWith(PathFilterArg))
                });
        }

        if (TryGetObjectIds(arg, Module, out ObjectId selectedId, out ObjectId firstId))
        {
            return StartBrowseDialog(owner: null,
                new BrowseArguments
                {
                    RevFilter = GetParameterOrEmptyStringAsDefault(args, "-filter"),
                    PathFilter = GetParameterOrEmptyStringAsDefault(args, PathFilterArg),
                    SelectedId = selectedId,
                    FirstId = firstId,
                    IsFileHistoryMode = args.Any(arg => arg.StartsWith(PathFilterArg))
                });
        }

        Console.Error.WriteLine($"No commit found matching: {arg}");
        return false;

        static bool TryGetObjectIds(string arg, IGitModule module, out ObjectId selectedId, out ObjectId firstId)
        {
            selectedId = default;
            firstId = default;
            foreach (string part in arg.LazySplit(','))
            {
                if (!module.TryResolvePartialCommitId(part, out ObjectId objectId))
                {
                    return false;
                }

                if (selectedId.IsZero)
                {
                    selectedId = objectId;
                }
                else if (firstId.IsZero)
                {
                    firstId = objectId;

                    // just ignore further commits
                    break;
                }
            }

            return true;
        }
    }

    private static string GetParameterOrEmptyStringAsDefault(IReadOnlyList<string> args, string paramName)
    {
        string withEquals = paramName + "=";

        for (int i = 2; i < args.Count; i++)
        {
            string arg = args[i];
            if (arg.StartsWith(withEquals))
            {
                return arg.Replace(withEquals, "");
            }
        }

        return "";
    }

    private bool RunOpenRepoCommand(IReadOnlyList<string> args)
    {
        IGitUICommands c = this;
        if (args.Count > 2)
        {
            if (File.Exists(args[2]))
            {
                string? path = File.ReadAllText(args[2]).Trim().LazySplit('\n').FirstOrDefault();
                if (Directory.Exists(path))
                {
                    c = WithWorkingDirectory(path);
                }
            }
        }

        return c.StartBrowseDialog(owner: null,
            new BrowseArguments
            {
                RevFilter = GetParameterOrEmptyStringAsDefault(args, "-filter"),
                PathFilter = GetParameterOrEmptyStringAsDefault(args, PathFilterArg)
            });
    }

    private bool RunSynchronizeCommand(IReadOnlyDictionary<string, string?> arguments)
    {
        bool successful = true;
        successful = Commit(arguments) && successful;
        successful = Pull(arguments) && successful;
        successful = Push(arguments) && successful;
        return successful;
    }

    private bool RunRebaseCommand(IReadOnlyDictionary<string, string?> arguments)
    {
        arguments.TryGetValue("branch", out string? branch);

        return StartRebaseDialog(owner: null, onto: branch);
    }

    public bool StartFileEditorDialog(string? filename, bool showWarning = false, int? lineNumber = null)
        => Execute(new UICmd.EditFile(filename, showWarning, lineNumber), owner: null);

    /// <summary>
    /// Remove working directory from filename and convert to POSIX path.
    /// This is to prevent filenames that are too long while there is room left when the workingdir was not in the path.
    /// </summary>
    private string NormalizeFileName(string fileName)
    {
        fileName = fileName.ToPosixPath();
        return string.IsNullOrEmpty(Module.WorkingDir) ? fileName : fileName.Replace(Module.WorkingDir.ToPosixPath(), "");
    }

    /// <returns>false on error.</returns>
    private bool RunFileHistoryCommand(IReadOnlyList<string> args, bool showBlame)
    {
        // Use the capitalization of the filename as passed because filenames in Git may differ from Windows file system.
        string fileHistoryFileName = NormalizeFileName(args[2]);

        if (string.IsNullOrWhiteSpace(fileHistoryFileName))
        {
            return false;
        }

        GitRevision? revision = null;
        if (args.Count > 3)
        {
            if (!ObjectId.TryParse(args[3], out ObjectId objectId))
            {
                return false;
            }

            revision = new GitRevision(objectId);
        }

        bool filterByRevision = false;
        if (args.Count > 4)
        {
            if (args[4] != FilterByRevisionArg)
            {
                return false;
            }

            filterByRevision = true;
        }

        // Similar to StartFileHistoryDialog()
        if (AppSettings.UseBrowseForFileHistory.Value)
        {
            // NOTE: fileHistoryFileName doesn't need to be quoted, as it the filter will get quoted
            // when the filter gets set.

            ShowModelessForm(owner: null, requiresValidWorkingDir: true, preEvent: null, postEvent: null,
                             () => new FormBrowse(commands: this, new BrowseArguments
                             {
                                 RevFilter = filterByRevision ? revision?.ObjectId.ToString() : null,
                                 PathFilter = fileHistoryFileName,
                                 SelectedId = revision?.ObjectId ?? default,
                                 IsFileHistoryMode = true
                             }));
        }
        else
        {
            // NOTE: fileHistoryFileName must be quoted.

            ShowModelessForm(owner: null, requiresValidWorkingDir: true, preEvent: null, postEvent: null,
                             () => new FormFileHistory(this, fileHistoryFileName.QuoteNE(), revision, filterByRevision, showBlame));
        }

        return true;
    }

    private bool RunCloneCommand(IReadOnlyList<string> args)
        => StartCloneDialog(null, args.Count > 2 ? args[2] : null);

    private bool RunInitCommand(IReadOnlyList<string> args)
        => StartInitializeDialog(null, args.Count > 2 ? args[2] : null);

    /// <returns>false on error.</returns>
    private bool RunBlameCommand(IReadOnlyList<string> args)
    {
        string blameFileName = NormalizeFileName(args[2]);

        int? initialLine = null;
        if (args.Count > 3)
        {
            if (int.TryParse(args[3], out int temp))
            {
                initialLine = temp;
            }
        }

        return DoActionOnRepo(owner: null, action: () =>
        {
            using FormBlame frm = new(this, blameFileName, null, initialLine);
            frm.ShowDialog(null);
            return true;
        }, changesRepo: false);
    }

    private bool RunMergeToolOrConflictCommand(IReadOnlyDictionary<string, string?> arguments)
    {
        if (!arguments.ContainsKey("quiet") || Module.InTheMiddleOfConflictedMerge())
        {
            return StartResolveConflictsDialog();
        }

        return true;
    }

    private static IReadOnlyDictionary<string, string?> InitializeArguments(IReadOnlyList<string> args)
    {
        Dictionary<string, string?> arguments = [];

        for (int i = 2; i < args.Count; i++)
        {
            if (args[i].StartsWith("--") && i + 1 < args.Count && !args[i + 1].StartsWith("--"))
            {
                arguments.Add(args[i].TrimStart('-'), args[++i]);
            }
            else if (args[i].StartsWith("--"))
            {
                arguments.Add(args[i].TrimStart('-'), null);
            }
        }

        return arguments;
    }

    private IEnumerable<string> FindFileMatches(string name)
    {
        IReadOnlyList<string> candidates = Module.GetFullTree("HEAD");

        Func<string?, bool> predicate = _findFilePredicateProvider.Get(name, Module.WorkingDir);

        return candidates.Where(predicate);
    }

    private bool Commit(IReadOnlyDictionary<string, string?> arguments)
    {
        arguments.TryGetValue("message", out string? overridingMessage);
        bool showOnlyWhenChanges = arguments.ContainsKey("quiet");
        return StartCommitDialog(null, overridingMessage, showOnlyWhenChanges);
    }

    private bool Push(IReadOnlyDictionary<string, string?> arguments)
        => StartPushDialog(null, arguments.ContainsKey("quiet"));

    private bool Pull(IReadOnlyDictionary<string, string?> arguments)
    {
        UpdateSettingsBasedOnArguments(arguments);

        arguments.TryGetValue("remotebranch", out string? remoteBranch);

        bool isQuiet = arguments.ContainsKey("quiet");

        if (isQuiet)
        {
            return StartPullDialogAndPullImmediately(remoteBranch: remoteBranch);
        }

        return StartPullDialog(remoteBranch: remoteBranch);
    }

    private static void UpdateSettingsBasedOnArguments(IReadOnlyDictionary<string, string?> arguments)
    {
        if (arguments.ContainsKey("merge"))
        {
            AppSettings.DefaultPullAction = GitPullAction.Merge;
        }

        if (arguments.ContainsKey("rebase"))
        {
            AppSettings.DefaultPullAction = GitPullAction.Rebase;
        }

        if (arguments.ContainsKey("fetch"))
        {
            AppSettings.DefaultPullAction = GitPullAction.Fetch;
        }

        if (arguments.ContainsKey("autostash"))
        {
            AppSettings.AutoStash = true;
        }
    }

    public void RaisePostBrowseInitialize(IWin32Window? owner)
    {
        InvokeEvent(owner, PostBrowseInitialize);
    }

    public void RaisePostRegisterPlugin(IWin32Window? owner)
    {
        InvokeEvent(owner, PostRegisterPlugin);
    }

    public IGitRemoteCommand CreateRemoteCommand()
    {
        return new GitRemoteCommand(this);
    }

    /// <summary>
    ///  Creates a new instance of <see cref="IGitUICommands"/> for a git repository specified by <paramref name="module"/>.
    /// </summary>
    /// <param name="module">The git repository.</param>
    /// <returns>A new instance of <see cref="IGitUICommands"/>.</returns>
    public IGitUICommands WithGitModule(IGitModule module) => new GitUICommands(_serviceProvider, module);

    /// <summary>
    ///  Creates a new instance of <see cref="IGitUICommands"/> for a git repository specified by <paramref name="workingDirectory"/>.
    /// </summary>
    /// <param name="workingDirectory">The git repository working directory.</param>
    /// <returns>A new instance of <see cref="IGitUICommands"/>.</returns>
    public IGitUICommands WithWorkingDirectory(string? workingDirectory) => new GitUICommands(_serviceProvider, new GitModule(_serviceProvider.GetRequiredService<IGitExecutorProvider>(), workingDirectory));

    #region Nested class: GitRemoteCommand

    private sealed class GitRemoteCommand : IGitRemoteCommand
    {
        public object? OwnerForm { get; set; }
        public string? Remote { get; set; }
        public string? Title { get; set; }
        public string? CommandText { get; set; }
        public bool ErrorOccurred { get; private set; }
        public string? CommandOutput { get; private set; }

        private readonly IGitUICommands _commands;

        public event EventHandler<GitRemoteCommandCompletedEventArgs>? Completed;

        internal GitRemoteCommand(IGitUICommands commands)
        {
            _commands = commands;
        }

        public void Execute()
        {
            if (CommandText is null)
            {
                throw new InvalidOperationException("CommandText is required");
            }

            using FormRemoteProcess form = new(_commands, CommandText);
            if (Title is not null)
            {
                form.Text = Title;
            }

            if (Remote is not null)
            {
                form.Remote = Remote;
            }

            form.HandleOnExitCallback = HandleOnExit;

            form.ShowDialog(OwnerForm as IWin32Window);

            ErrorOccurred = form.ErrorOccurred();
            CommandOutput = form.GetOutputString();
        }

        private bool HandleOnExit(ref bool isError, FormProcess form)
        {
            CommandOutput = form.GetOutputString();

            GitRemoteCommandCompletedEventArgs e = new(this, isError, false);

            Completed?.Invoke(form, e);

            isError = e.IsError;

            return e.Handled;
        }
    }

    #endregion

    internal TestAccessor GetTestAccessor() => new(this);

    internal readonly struct TestAccessor
    {
        private readonly GitUICommands _commands;

        internal TestAccessor(GitUICommands commands)
        {
            _commands = commands;
        }

        internal readonly string NormalizeFileName(string fileName) => _commands.NormalizeFileName(fileName);

        internal readonly bool RunCommandBasedOnArgument(string[] args) => _commands.RunCommandBasedOnArgument(args, InitializeArguments(args));

        internal readonly void ShowFileHistoryDialog(string fileName)
            => _commands.RunFileHistoryCommand(args: new string[] { "", "", fileName }, showBlame: false);
    }
}
