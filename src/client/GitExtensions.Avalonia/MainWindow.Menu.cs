using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using GitCommands;
using GitCommands.UserRepositoryHistory;
using GitUIPluginInterfaces;

namespace GitExtensions.Avalonia;

/// <summary>
///  The complete menu bar (craig's direction, see client-parity-audit.md): EVERY piece of
///  functionality is present and discoverable here, organized like the WinForms menubar.
///  Implemented entries are wired; not-yet-implemented entries are visibly DISABLED with a
///  tooltip, so every remaining parity gap is a visible menu item instead of silent absence.
///  This is NOT profile-filtered - Simple/Normal/Custom applies to context menus only.
/// </summary>
public partial class MainWindow
{
    private MenuItem? _navigateMenu;
    private MenuItem? _viewMenu;
    private MenuItem? _repositoryMenu;
    private MenuItem? _commandsMenu;

    private void BuildMainMenu()
    {
        MainMenuBar.Items.Clear();
        MainMenuBar.Items.Add(BuildStartMenu());
        MainMenuBar.Items.Add(_navigateMenu = BuildNavigateMenu());
        MainMenuBar.Items.Add(_viewMenu = BuildViewMenu());
        MainMenuBar.Items.Add(_repositoryMenu = BuildRepositoryMenu());
        MainMenuBar.Items.Add(_commandsMenu = BuildCommandsMenu());
        MainMenuBar.Items.Add(BuildPluginsMenu());
        MainMenuBar.Items.Add(BuildToolsMenu());
        MainMenuBar.Items.Add(BuildHelpMenu());

        SetRepositoryMenusEnabled(_session.IsValidRepository);
    }

    private void SetRepositoryMenusEnabled(bool enabled)
    {
        if (_navigateMenu is null)
        {
            return;
        }

        _navigateMenu.IsEnabled = enabled;
        _viewMenu!.IsEnabled = enabled;
        _repositoryMenu!.IsEnabled = enabled;
        _commandsMenu!.IsEnabled = enabled;
    }

    // ----- item helpers -----

    private static MenuItem Item(string caption, Func<Task> run)
    {
        MenuItem item = new() { Header = Loc.T(caption) };
        item.Click += async (_, _) => await run();
        return item;
    }

    /// <summary>A visible parity gap: present so it can be discovered, disabled until implemented.</summary>
    private static MenuItem Todo(string caption)
    {
        MenuItem item = new() { Header = Loc.T(caption), IsEnabled = false };
        ToolTip.SetTip(item, Loc.T("Not implemented in the Linux client yet"));
        return item;
    }

    private static MenuItem Sub(string caption, params Control[] children)
    {
        MenuItem item = new() { Header = Loc.T(caption) };
        foreach (Control child in children)
        {
            item.Items.Add(child);
        }

        return item;
    }

    private static Separator Sep() => new();

    /// <summary>Runs a commit action against the current log selection, or explains that one is needed.</summary>
    private async Task WithSelectedRevisionAsync(Func<GitRevision, Task> run)
    {
        if (_selectedRevision is GitRevision revision)
        {
            await run(revision);
            return;
        }

        await ConfirmDialog.ErrorAsync(this, Loc.T("No commit selected"), Loc.T("Select a commit in the log first."));
    }

    /// <summary>The palette reused as a picker over the local branches.</summary>
    private async Task PickLocalBranchAsync(string title, Func<string, Task> run)
    {
        IReadOnlyList<string> branches = await Task.Run(_session.GetLocalBranchNames);
        List<(string Label, Func<Task> Execute)> entries = [];
        foreach (string branch in branches)
        {
            string captured = branch;
            entries.Add(($"{title}: {branch}", () => run(captured)));
        }

        await CommandPalette.ShowAsync(this, entries);
    }

    // ----- Start -----

    private MenuItem BuildStartMenu()
    {
        MenuItem recent = new() { Header = Loc.T("Recent repositories") };
        recent.Items.Add(Todo("(empty)"));
        recent.SubmenuOpened += async (_, _) => await PopulateRecentSubmenuAsync(recent);

        MenuItem favourites = new() { Header = Loc.T("Favourite repositories") };
        favourites.Items.Add(Todo("(empty)"));
        favourites.SubmenuOpened += async (_, _) => await PopulateFavouritesSubmenuAsync(favourites);

        return Sub("_Start",
            Item("Create new repository...", InitRepositoryAsync),
            Item("Clone repository...", CloneRepositoryAsync),
            Item("Open...", OpenRepositoryViaDialogAsync),
            Sep(),
            recent,
            favourites,
            Sep(),
            Item("Exit", () =>
            {
                Close();
                return Task.CompletedTask;
            }));
    }

    private async Task OpenRepositoryViaDialogAsync()
    {
        string? path = await OpenRepositoryDialog.ShowAsync(this, _session.IsValidRepository ? _session.WorkingDir : null);
        if (path is not null)
        {
            await SwitchRepositoryAsync(path);
        }
    }

    private async Task PopulateRecentSubmenuAsync(MenuItem root)
    {
        var recentHistory = await RepositoryHistoryManager.Locals.LoadRecentHistoryAsync();
        var favouriteHistory = await RepositoryHistoryManager.Locals.LoadFavouriteHistoryAsync();
        TriggerRecentBranchNameUpdate(onlyIfEmpty: true, recentHistory, favouriteHistory);

        RecentRepositoriesMenuModel model = RecentRepositoryMenu.BuildRecent(
            recentHistory, RecentRepoSplitterOptions.FromAppSettings(), _recentBranchNames.GetCachedBranchName);

        root.Items.Clear();
        foreach (RepoMenuEntry entry in model.Pinned)
        {
            root.Items.Add(MakeRepoItem(entry));
        }

        if (model.ShowSeparator)
        {
            root.Items.Add(Sep());
        }

        foreach (RepoMenuEntry entry in model.Recent)
        {
            root.Items.Add(MakeRepoItem(entry));
        }

        if (model.Pinned.Count == 0 && model.Recent.Count == 0)
        {
            root.Items.Add(Todo("(empty)"));
            return;
        }

        root.Items.Add(Sep());
        root.Items.Add(Item("Clear recent repositories list...", async () =>
        {
            if (await ConfirmDialog.ConfirmAsync(this, Loc.T("Clear recent repositories"), Loc.T("Remove all entries from the recent repositories list?")))
            {
                await RepositoryHistoryManager.Locals.SaveRecentHistoryAsync([]);
                if (StartPanel.IsVisible)
                {
                    await RefreshStartViewAsync();
                }
            }
        }));
    }

    private async Task PopulateFavouritesSubmenuAsync(MenuItem root)
    {
        var favouriteHistory = await RepositoryHistoryManager.Locals.LoadFavouriteHistoryAsync();
        IReadOnlyList<FavouriteCategoryGroup> groups = RecentRepositoryMenu.BuildFavourites(
            favouriteHistory, RecentRepoSplitterOptions.FromAppSettings(), _recentBranchNames.GetCachedBranchName);

        root.Items.Clear();
        if (groups.Count == 0)
        {
            root.Items.Add(Todo("(empty)"));
            return;
        }

        foreach (FavouriteCategoryGroup group in groups)
        {
            MenuItem category = new() { Header = group.Category ?? Loc.T("(no category)") };
            foreach (RepoMenuEntry entry in group.Entries)
            {
                category.Items.Add(MakeRepoItem(entry));
            }

            root.Items.Add(category);
        }
    }

    private MenuItem MakeRepoItem(RepoMenuEntry entry)
    {
        string pin = entry.IsPinned ? "\U0001F4CC " : "";
        string branch = entry.BranchName is null ? "" : $"  ({entry.BranchName})";
        MenuItem item = new() { Header = $"{pin}{entry.Number}: {entry.Caption}{branch}" };
        if (entry.Tooltip is not null)
        {
            ToolTip.SetTip(item, entry.Tooltip);
        }

        string path = entry.Repo.Path;
        item.Click += async (_, _) => await OpenRecentAsync(path);
        return item;
    }

    // ----- Navigate / View (tier-2 gaps, all visible) -----

    private MenuItem BuildNavigateMenu()
        => Sub("_Navigate",
            Todo("Go to commit..."),
            Todo("Go to parent commit"),
            Todo("Go to child commit"),
            Todo("Go to common ancestor (merge base)"),
            Sep(),
            Todo("Navigate backward"),
            Todo("Navigate forward"),
            Sep(),
            Todo("Quick search"));

    private MenuItem BuildViewMenu()
        => Sub("_View",
            Item("Show all branches", () =>
            {
                _filterBar.ShowAllBranches();
                return Task.CompletedTask;
            }),
            Item("Show current branch only", () =>
            {
                _filterBar.ShowCurrentBranchOnly();
                return Task.CompletedTask;
            }),
            Item("Show reflog references", () =>
            {
                _filterBar.ToggleReflog();
                return Task.CompletedTask;
            }),
            Item("Reset all filters", () =>
            {
                _filterBar.ClearAll();
                return Task.CompletedTask;
            }),
            Sep(),
            Todo("Show artificial commits"),
            Todo("Show stashes"),
            Todo("Show author date"),
            Todo("Show relative date"),
            Sep(),
            Todo("Advanced filter..."));

    // ----- Repository -----

    private MenuItem BuildRepositoryMenu()
        => Sub("_Repository",
            Item("Refresh", RefreshAllAsync),
            Item("Show in file manager", () =>
            {
                OsShellUtil.OpenWithFileExplorer(_session.WorkingDir);
                return Task.CompletedTask;
            }),
            Sep(),
            Item("Remote repositories...", OpenRemotesAsync),
            Sub("Submodules",
                Item("Add submodule...", AddSubmoduleFlowAsync),
                Item("Update all submodules", () => RunOperationAsync(Loc.T("Update submodules"), _session.UpdateSubmodulesAsync)),
                Item("Synchronize all submodules", () => RunOperationAsync(Loc.T("Synchronize submodules"), _session.SyncSubmodulesAsync))),
            Sub("Worktrees",
                Item("Create worktree...", CreateWorktreeFlowAsync),
                Item("Prune worktrees", () => RunOperationAsync(Loc.T("Prune worktrees"), _session.PruneWorktreesAsync))),
            Sep(),
            Item("Edit .gitignore", () => DotFileEditorWindow.ShowAsync(this, _session, RepoDotFile.GitIgnore)),
            Item("Edit .git/info/exclude", () => DotFileEditorWindow.ShowAsync(this, _session, RepoDotFile.LocalExclude)),
            Item("Edit .gitattributes", () => DotFileEditorWindow.ShowAsync(this, _session, RepoDotFile.GitAttributes)),
            Item("Edit .mailmap", () => DotFileEditorWindow.ShowAsync(this, _session, RepoDotFile.MailMap)),
            Todo("Sparse working copy..."),
            Sep(),
            Sub("Git maintenance",
                Todo("Compress git database (gc)"),
                Todo("Recover lost objects (fsck)..."),
                Todo("Delete index.lock"),
                Todo("Edit .git/config")),
            Todo("Repository settings..."),
            Sep(),
            Item("Close repository", CloseRepositoryAsync));

    private async Task RefreshAllAsync()
    {
        await ReloadLogAsync();
        await LoadRefPanelAsync();
    }

    /// <summary>Back to the start page: the client's equivalent of WinForms' Close → Dashboard.</summary>
    internal async Task CloseRepositoryAsync()
    {
        _logCts.Cancel();
        _logCts = new();
        await LogControl.ResetAsync();

        _session = new SliceSession("");
        _selectedRevision = null;
        _compareBaseRevision = null;
        _fileGroups.Clear();
        FileTree.ItemsSource = null;
        RefTree.ItemsSource = null;
        CommitHeader.Inlines?.Clear();
        CommitBody.Inlines?.Clear();
        BranchInfo.Text = "";
        OperationStatus.Text = "";
        ResolveConflictsButton.IsVisible = false;
        RebaseContinueButton.IsVisible = false;
        RebaseAbortButton.IsVisible = false;

        await ShowStartViewAsync();
    }

    private async Task AddSubmoduleFlowAsync()
    {
        string? url = await ConfirmDialog.InputAsync(this, Loc.T("Add submodule"), Loc.T("Remote path or URL:"));
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        string? localPath = await ConfirmDialog.InputAsync(this, Loc.T("Add submodule"), Loc.T("Local path:"), initialText: PathUtil.GetRepositoryName(url));
        if (!GitCommands.Worktree.SubmoduleAddModel.IsValid(url, localPath))
        {
            return;
        }

        await RunOperationAsync($"Add submodule {localPath}", () => _session.AddSubmoduleAsync(url, localPath!, branch: "", force: false));
    }

    private async Task CreateWorktreeFlowAsync()
    {
        IReadOnlyList<string> branches = await Task.Run(_session.GetLocalBranchNames);
        var choice = await CreateWorktreeDialog.ShowAsync(this, _session.WorkingDir.TrimEnd('/', '\\'), branches, _session.SelectedBranch);
        if (choice is var (directory, newBranchOption) && choice is not null)
        {
            await RunOperationAsync(Loc.T("Create worktree"), () => _session.CreateWorktreeAsync(directory, newBranchOption));
            await LoadRefPanelAsync();
        }
    }

    // ----- Commands -----

    private MenuItem BuildCommandsMenu()
        => Sub("_Commands",
            Item("Commit...", async () =>
            {
                CommitWindow commitWindow = new(_session);
                await commitWindow.ShowDialog(this);
                if (commitWindow.Committed)
                {
                    await ReloadLogAsync();
                }
            }),
            Todo("Undo last commit..."),
            Sep(),
            Item("Pull...", PullFlowAsync),
            Item("Fetch", () => RunOperationAsync(Loc.T("Fetch"), _session.FetchAsync)),
            Item("Push...", PushFlowAsync),
            Sep(),
            Sub("Stash",
                Item("Stash changes / manage stashes...", OpenStashWindowAsync),
                Item("Stash staged changes", () => RunOperationAsync(Loc.T("Stash staged"), _session.StashStagedAsync)),
                Item("Stash pop (latest)", () => RunOperationAsync(Loc.T("Stash pop"), _session.StashPopAsync))),
            Item("Reset changes...", ResetChangesFlowAsync),
            Todo("Clean working directory..."),
            Sep(),
            Item("Create branch...", CreateBranchFlowAsync),
            Item("Checkout branch...", () => PickLocalBranchAsync(Loc.T("Checkout"), CheckoutBranchInteractiveAsync)),
            Item("Merge branches...", () => PickLocalBranchAsync(Loc.T("Merge"), branch => MergeWithDialogAsync(branch, branch))),
            Item("Rebase...", () => PickLocalBranchAsync(Loc.T("Rebase onto"), branch => RebaseWithConfirmAsync(branch, branch))),
            Item("Solve merge conflicts...", ShowConflictsWindowAsync),
            Sep(),
            Item("Create tag...", () => WithSelectedRevisionAsync(revision => CommitActionHandlers["commit.createTag"](revision))),
            Item("Cherry pick selected commit...", () => WithSelectedRevisionAsync(revision => CommitActionHandlers["commit.cherryPick"](revision))),
            Item("Archive selected revision...", () => WithSelectedRevisionAsync(revision => CommitActionHandlers["commit.archive"](revision))),
            Item("Checkout selected revision (detached)...", () => WithSelectedRevisionAsync(revision => CommitActionHandlers["commit.checkoutDetached"](revision))),
            Sub("Bisect",
                Item("Start bisect", () => RunOperationAsync(Loc.T("Start bisect"), _session.StartBisectAsync)),
                Item("Mark selected good", () => WithSelectedRevisionAsync(revision => CommitActionHandlers["bisect.good"](revision))),
                Item("Mark selected bad", () => WithSelectedRevisionAsync(revision => CommitActionHandlers["bisect.bad"](revision))),
                Item("Skip selected", () => WithSelectedRevisionAsync(revision => CommitActionHandlers["bisect.skip"](revision))),
                Item("Stop bisect", () => WithSelectedRevisionAsync(revision => CommitActionHandlers["bisect.stop"](revision)))),
            Todo("Show reflog..."),
            Sep(),
            Todo("Format patch..."),
            Todo("Apply patch..."),
            Todo("View patch file..."));

    private async Task OpenStashWindowAsync()
    {
        StashWindow stashWindow = new(_session);
        await stashWindow.ShowDialog(this);
        if (stashWindow.StashesChanged)
        {
            await RefreshAllAsync();
        }
    }

    /// <summary>The WinForms reset-changes flow: reset tracked changes, optionally delete untracked too.</summary>
    internal async Task ResetChangesFlowAsync()
    {
        int choice = await ConfirmDialog.ShowAsync(
            this,
            Loc.T("Reset changes"),
            Loc.T("Reset all changes in the working directory?"),
            Loc.T("Reset tracked changes"), Loc.T("Reset and delete untracked"), Loc.T("Cancel"));

        if (choice is not (0 or 1))
        {
            return;
        }

        bool clean = choice == 1;
        await RunOperationAsync(Loc.T("Reset changes"), async () =>
        {
            bool success = await _session.ResetAllChangesAsync(clean);
            return (success, success ? "" : Loc.T("Reset failed."));
        });
    }

    // ----- Plugins / Tools / Help -----

    private static MenuItem BuildPluginsMenu()
        => Sub("_Plugins", Todo("(plugin system not yet available in the Linux client)"));

    private MenuItem BuildToolsMenu()
        => Sub("_Tools",
            Todo("Git command log"),
            Sep(),
            Item("Settings...", OpenSettingsAsync));

    private MenuItem BuildHelpMenu()
        => Sub("_Help",
            Item("User manual", () => OpenUrl("https://git-extensions-documentation.readthedocs.io/")),
            Item("Changelog", () => OpenUrl("https://github.com/gitextensions/gitextensions/blob/master/GitUI/Resources/ChangeLog.md")),
            Item("Report an issue", () => OpenUrl("https://github.com/gitextensions/gitextensions/issues")),
            Sep(),
            Item("Donate", () => OpenUrl("https://github.com/gitextensions/gitextensions/blob/master/README.md#donate")),
            Item("About", async () => await ConfirmDialog.ErrorAsync(this, Loc.T("About Git Extensions"),
                $"Git Extensions (Avalonia client)\n{typeof(MainWindow).Assembly.GetName().Version}\n\nThe cross-platform client of the Git Extensions fork.")));

    private static Task OpenUrl(string url)
    {
        OsShellUtil.OpenUrlInDefaultBrowser(url);
        return Task.CompletedTask;
    }
}
