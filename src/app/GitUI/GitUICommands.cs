using System.Collections.Immutable;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Text;
using GitCommands;
using GitCommands.Git;
using GitCommands.Settings;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtensions.Extensibility.Settings;
using GitExtUtils;
using GitUI.CommandsDialogs;
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

    /// <remarks>
    ///  Host-only bridge: takes a WinForms form factory, so it is not part of <see cref="IGitUICommands"/>.
    /// </remarks>
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

    /// <param name="pullCompleted">true if pull completed with no errors.</param>
    /// <returns>if revision grid should be refreshed.</returns>
    /// <remarks>
    ///  Host-only bridge: the <c>out</c> parameter has no place on the intent bus, so it is not
    ///  part of <see cref="IGitUICommands"/>.
    /// </remarks>
    public bool StartPullDialogAndPullImmediately(out bool pullCompleted, IWin32Window? owner = null, string? remoteBranch = null, string? remote = null, GitPullAction pullAction = GitPullAction.None)
    {
        return new UICommandHandlers.PullHandler(this).Execute(new UICmd.PullImmediately(remoteBranch, remote, pullAction), owner, out pullCompleted);
    }

    public void AddCommitTemplate(string key, Func<string> addingText, object? icon, bool isRegex)
    {
        _commitTemplateManager.Register(key, addingText, icon, isRegex);
    }

    public void RemoveCommitTemplate(string key)
    {
        _commitTemplateManager.Unregister(key);
    }

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

    /// <remarks>
    ///  Host-only bridge: the <c>out</c> parameter has no place on the intent bus, so it is not
    ///  part of <see cref="IGitUICommands"/>.
    /// </remarks>
    public bool StartPushDialog(IWin32Window? owner, bool pushOnShow, bool forceWithLease, out bool pushCompleted, string? branchName = null)
        => new UICommandHandlers.PushHandler(this).Execute(new UICmd.Push(pushOnShow, forceWithLease, branchName), owner, out pushCompleted);

    private bool InvokeEvent(object? ownerForm, EventHandler<GitUIEventArgs>? gitUIEventHandler)
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

    private void InvokePostEvent(object? ownerForm, bool actionDone, EventHandler<GitUIPostActionEventArgs>? gitUIEventHandler)
    {
        if (gitUIEventHandler is not null)
        {
            GitUIPostActionEventArgs e = new(ownerForm, this, actionDone);
            gitUIEventHandler(this, e);
        }
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
                return Execute(new UICmd.AddFiles(args.Count < 3 ? "." : string.Join(' ', args.Skip(2).Select(file => file.Quote()))), owner: null);
            case "apply":       // [filename]
            case "applypatch":
                return Execute(new UICmd.ApplyPatch(args.Count == 3 ? args[2] : ""), owner: null);
            case "blame":       // filename
                return RunBlameCommand(args);
            case "branch":
                return Execute(new UICmd.CreateBranch(), owner: null);
            case "browse":      // [path] [--pathFilter=filname] [-filter] [-commit=selected[,first]]
                return RunBrowseCommand(args);
            case "checkout":
            case "checkoutbranch":
                return Execute(new UICmd.CheckoutBranch(), owner: null);
            case "checkoutrevision":
                return Execute(new UICmd.CheckoutRevision(), owner: null);
            case "cherry":
                return Execute(new UICmd.CherryPick(), owner: null);
            case "cleanup":
                return Execute(new UICmd.CleanupRepository(), owner: null);
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
                return Execute(new UICmd.EditFile(args[2]), owner: null);
            case "formatpatch":
                return Execute(new UICmd.FormatPatch(), owner: null);
            case "gitignore":
                return Execute(new UICmd.EditGitIgnore(LocalExcludes: false), owner: null);
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
                return Execute(new UICmd.Remotes(), owner: null);
            case "revert":
            case "reset":
                // If names of files or folders have been specified, pass them
                return StartResetChangesDialog(names: [.. args.Skip(2)]);
            case "searchfile":
                return RunSearchFileCommand();
            case "settings":
                return Execute(new UICmd.OpenSettings(), owner: null);
            case "stash":
                return Execute(new UICmd.Stash(), owner: null);
            case "synchronize": // [--rebase] [--merge] [--fetch] [--quiet]
                return RunSynchronizeCommand(arguments);
            case "tag":
                return Execute(new UICmd.CreateTag(), owner: null);
            case "viewdiff":
                return Execute(new UICmd.CompareRevisions(), owner: null);
            case "viewpatch":   // [filename]
                return Execute(new UICmd.ViewPatch(args.Count == 3 ? args[2] : ""), owner: null);
            case "uninstall":
                return UninstallEditor();
            default:
                if (args[1].StartsWith("git://") || args[1].StartsWith("http://") || args[1].StartsWith("https://"))
                {
                    return Execute(new UICmd.Clone(args[1], OpenedFromProtocolHandler: true), owner: null);
                }

                if (args[1].StartsWith("github-windows://openRepo/"))
                {
                    return Execute(new UICmd.Clone(args[1].Replace("github-windows://openRepo/", ""), OpenedFromProtocolHandler: true), owner: null);
                }

                if (args[1].StartsWith("github-mac://openRepo/"))
                {
                    return Execute(new UICmd.Clone(args[1].Replace("github-mac://openRepo/", ""), OpenedFromProtocolHandler: true), owner: null);
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

        return Execute(new UICmd.MergeBranch(branch), owner: null);
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
            return Execute(
                new UICmd.Browse(new BrowseArguments
                {
                    RevFilter = GetParameterOrEmptyStringAsDefault(args, "-filter"),
                    PathFilter = GetParameterOrEmptyStringAsDefault(args, PathFilterArg),
                    IsFileHistoryMode = args.Any(arg => arg.StartsWith(PathFilterArg))
                }),
                owner: null);
        }

        if (TryGetObjectIds(arg, Module, out ObjectId selectedId, out ObjectId firstId))
        {
            return Execute(
                new UICmd.Browse(new BrowseArguments
                {
                    RevFilter = GetParameterOrEmptyStringAsDefault(args, "-filter"),
                    PathFilter = GetParameterOrEmptyStringAsDefault(args, PathFilterArg),
                    SelectedId = selectedId,
                    FirstId = firstId,
                    IsFileHistoryMode = args.Any(arg => arg.StartsWith(PathFilterArg))
                }),
                owner: null);
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

        return c.Execute(
            new UICmd.Browse(new BrowseArguments
            {
                RevFilter = GetParameterOrEmptyStringAsDefault(args, "-filter"),
                PathFilter = GetParameterOrEmptyStringAsDefault(args, PathFilterArg)
            }),
            ownerWindow: null);
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

        return Execute(new UICmd.Rebase(branch), owner: null);
    }

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
        => Execute(new UICmd.Clone(args.Count > 2 ? args[2] : null), owner: null);

    private bool RunInitCommand(IReadOnlyList<string> args)
        => Execute(new UICmd.InitializeRepository(args.Count > 2 ? args[2] : null), owner: null);

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
            return Execute(new UICmd.ResolveConflicts(), owner: null);
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
        return Execute(new UICmd.Commit(overridingMessage, showOnlyWhenChanges), owner: null);
    }

    private bool Push(IReadOnlyDictionary<string, string?> arguments)
        => Execute(new UICmd.Push(arguments.ContainsKey("quiet")), owner: null);

    private bool Pull(IReadOnlyDictionary<string, string?> arguments)
    {
        UpdateSettingsBasedOnArguments(arguments);

        arguments.TryGetValue("remotebranch", out string? remoteBranch);

        bool isQuiet = arguments.ContainsKey("quiet");

        if (isQuiet)
        {
            return Execute(new UICmd.PullImmediately(remoteBranch), owner: null);
        }

        return Execute(new UICmd.Pull(remoteBranch), owner: null);
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

    public void RaisePostBrowseInitialize(object? ownerWindow)
    {
        InvokeEvent(ownerWindow, PostBrowseInitialize);
    }

    public void RaisePostRegisterPlugin(object? ownerWindow)
    {
        InvokeEvent(ownerWindow, PostRegisterPlugin);
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
