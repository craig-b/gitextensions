using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GitExtensions.Avalonia.Rendering;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;
using GitUI.Editor.Diff;

namespace GitExtensions.Avalonia;

public partial class MainWindow : Window
{
    private SliceSession _session;
    private GitRevision? _selectedRevision;
    private CancellationTokenSource? _selectionCts;
    private CancellationTokenSource _logCts = new();

    public MainWindow(string repositoryPath)
    {
        InitializeComponent();

        _session = new SliceSession(repositoryPath);
        Loc.Reload();
        ApplyThemeVariant();
        ApplyToolbarTranslations();
        Title = $"Git Extensions - {_session.WorkingDir}";

        LogControl.RevisionSelected += (_, revision) =>
        {
            _selectedRevision = revision;
            _ = ShowRevisionAsync(revision);
        };
        LogControl.ContextRequested += OnLogContextRequested;
        RefTree.ContextRequested += OnRefTreeContextRequested;
        FileTree.ContextRequested += OnFileTreeContextRequested;
        RebuildHotkeyMap();
        KeyDown += (_, keyArgs) =>
        {
            if (keyArgs.Key == global::Avalonia.Input.Key.P
                && keyArgs.KeyModifiers == (global::Avalonia.Input.KeyModifiers.Control | global::Avalonia.Input.KeyModifiers.Shift))
            {
                keyArgs.Handled = true;
                _ = ShowCommandPaletteAsync();
                return;
            }

            HandleActionHotkey(keyArgs);
        };

        Loaded += (_, _) => StartLogStream();
        Closed += (_, _) => _logCts.Cancel();

        // Verification hook: switch to the given repository after startup and report whether the
        // switch landed (session re-targeted, MRU promoted, log streamed).
        if (Environment.GetEnvironmentVariable("GE_SPIKE_OPENTEST") is { Length: > 0 } openTarget)
        {
            Loaded += async (_, _) =>
            {
                await Task.Delay(1500);
                await SwitchRepositoryAsync(openTarget);
                await Task.Delay(2500);

                string expected = System.IO.Path.GetFullPath(openTarget).TrimEnd('/', '\\');
                bool switched = _session.WorkingDir.TrimEnd('/', '\\') == expected;
                var history = await GitCommands.UserRepositoryHistory.RepositoryHistoryManager.Locals.LoadRecentHistoryAsync();
                bool promoted = history.Count > 0 && history[0].Path.TrimEnd('/', '\\') == expected;
                int commits = LogControl.Count;
                Console.Error.WriteLine($"[open] switched:{switched} mru:{promoted} commits:{commits} dir:{_session.WorkingDir}");
                Environment.Exit(switched && promoted && commits > 0 ? 0 : 1);
            };
        }

        // Verification hook: clone the given source repo into a fresh directory through the
        // session's clone path, then init a fresh repo, and report both.
        if (Environment.GetEnvironmentVariable("GE_SPIKE_CLONETEST") is { Length: > 0 } cloneSource)
        {
            Loaded += async (_, _) =>
            {
                string baseDir = Environment.GetEnvironmentVariable("GE_SPIKE_CLONETEST_DEST") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ge-clonetest");
                string cloneTarget = System.IO.Path.Combine(baseDir, "cloned");
                string initTarget = System.IO.Path.Combine(baseDir, "inited");

                System.IO.Directory.CreateDirectory(cloneTarget);
                (bool cloneOk, string cloneOut) = await _session.CloneAsync(cloneSource, cloneTarget, bare: false, initSubmodules: false, branch: "", depth: null, isSingleBranch: null);
                bool cloneValid = GitCommands.GitModule.IsValidGitWorkingDir(cloneTarget);

                (bool initOk, string initOut) = await _session.InitAsync(initTarget, bare: false, shared: false);
                bool initValid = GitCommands.GitModule.IsValidGitWorkingDir(initTarget);

                bool switched = false;
                if (cloneValid)
                {
                    await SwitchRepositoryAsync(cloneTarget);
                    await Task.Delay(1500);
                    switched = _session.WorkingDir.TrimEnd('/', '\\') == System.IO.Path.GetFullPath(cloneTarget).TrimEnd('/', '\\') && LogControl.Count > 0;
                }

                Console.Error.WriteLine($"[clone] clone:{cloneOk}/{cloneValid} init:{initOk}/{initValid} switched:{switched} commits:{LogControl.Count}");
                if (!cloneOk)
                {
                    Console.Error.WriteLine($"[clone] clone output: {cloneOut.Replace("\n", " / ")}");
                }

                Environment.Exit(cloneOk && cloneValid && initOk && initValid && switched ? 0 : 1);
            };
        }

        // Verification hook: build the recents menu model from the real history store and report
        // its shape (pinned/recent/favourite-group counts and the first caption).
        if (Environment.GetEnvironmentVariable("GE_SPIKE_RECENTTEST") == "1")
        {
            Loaded += async (_, _) =>
            {
                var recent = await GitCommands.UserRepositoryHistory.RepositoryHistoryManager.Locals.LoadRecentHistoryAsync();
                var favourites = await GitCommands.UserRepositoryHistory.RepositoryHistoryManager.Locals.LoadFavouriteHistoryAsync();
                var options = GitCommands.UserRepositoryHistory.RecentRepoSplitterOptions.FromAppSettings();
                var model = GitCommands.UserRepositoryHistory.RecentRepositoryMenu.BuildRecent(recent, options, _ => null);
                var groups = GitCommands.UserRepositoryHistory.RecentRepositoryMenu.BuildFavourites(favourites, options, _ => null);
                string first = model.Pinned.Concat(model.Recent).FirstOrDefault()?.Caption ?? "<none>";
                Console.Error.WriteLine($"[recent] pinned:{model.Pinned.Count} recent:{model.Recent.Count} favGroups:{groups.Count} first:{first}");
                Environment.Exit(model.Pinned.Count + model.Recent.Count > 0 ? 0 : 1);
            };
        }

        if (Environment.GetEnvironmentVariable("GE_SPIKE_OPSTEST") == "1")
        {
            Loaded += async (_, _) =>
            {
                async Task Report(string name, Task<(bool Success, string Output)> operation)
                {
                    (bool success, string output) = await operation;
                    Console.Error.WriteLine($"[ops] {name}: {(success ? "OK" : "FAIL")} | {output.Replace("\n", " / ").Trim()}");
                }

                await Report("create-branch", _session.CreateBranchAsync("harness-branch", checkout: true));
                await Report("push", _session.PushAsync(forceWithLease: false));
                await Report("push-options", _session.PushWithOptionsAsync("origin", "harness-branch", "harness-branch", GitCommands.Git.ForcePushOptions.ForceWithLease, track: false));
                await Report("checkout", _session.CheckoutBranchAsync("main"));
                await Report("fetch", _session.FetchAsync());
                await Report("pull", _session.PullAsync(rebase: false));
                Environment.Exit(0);
            };
        }

        if (Environment.GetEnvironmentVariable("GE_SPIKE_FILEHISTORYTEST") is string fileHistoryFile)
        {
            Loaded += (_, _) =>
            {
                FileHistoryWindow fileHistoryWindow = new(_session, fileHistoryFile);
                fileHistoryWindow.Loaded += (_, _) => _ = fileHistoryWindow.RunHarnessAsync();
                fileHistoryWindow.Show(this);
            };
        }

        if (Environment.GetEnvironmentVariable("GE_SPIKE_WORKTREETEST") == "1")
        {
            Loaded += async (_, _) =>
            {
                await Task.Delay(2000);
                string directory = System.IO.Path.Combine(_session.WorkingDir, "..", "harness-worktree");
                (bool createOk, string createOut) = await _session.CreateWorktreeAsync(directory, "-b harness-wt-branch");
                int countAfterCreate = _session.GetWorktreePanel().Count;
                (bool removeOk, _) = await _session.RemoveWorktreeAsync(directory, force: true);
                await _session.PruneWorktreesAsync();
                int countAfterRemove = _session.GetWorktreePanel().Count;
                Console.Error.WriteLine($"[worktree] create: {(createOk ? "OK" : $"FAIL {createOut}")} ({countAfterCreate} worktrees) | remove: {(removeOk ? "OK" : "FAIL")} ({countAfterRemove} left)");

                (bool subOk, string subOut) = await _session.AddSubmoduleAsync("../opsremote.git", "harness-submodule", branch: "", force: false);
                Console.Error.WriteLine($"[worktree] add-submodule: {(subOk ? "OK" : $"FAIL {subOut.Replace("\n", " / ")}")}");
                Environment.Exit(0);
            };
        }

        if (Environment.GetEnvironmentVariable("GE_SPIKE_L10NTEST") is string l10nLanguage)
        {
            Loaded += (_, _) =>
            {
                GitCommands.Localization.SourceJoinTranslator translator =
                    GitCommands.Localization.SourceJoinTranslator.Load(Loc.TranslationDir, l10nLanguage);
                string[] samples = ["Fetch", "Pull", "Push", "Delete...", "Checkout", "Rename...", "Cherry-pick this commit..."];
                Console.Error.WriteLine($"[l10n] {l10nLanguage}: {translator.Count} joined strings | " +
                    string.Join(" | ", samples.Select(sample => $"{sample} -> {translator.T(sample)}")));
                Console.Error.WriteLine($"[l10n] languages available: {GitCommands.Localization.SourceJoinTranslation.FindLanguages(Loc.TranslationDir).Count}");
                Environment.Exit(0);
            };
        }

        if (Environment.GetEnvironmentVariable("GE_SPIKE_SCRIPTSTEST") == "1")
        {
            Loaded += async (_, _) =>
            {
                await Task.Delay(3000);
                LogControl.SelectRow(0);
                await Task.Delay(300);

                GitCommands.Scripts.ScriptDefinition script = new(
                    "harness-echo", "Harness echo", "shell",
                    "echo branch={current.branch} repo={repo.name} hash={selected.hash} legacy={cBranch}",
                    Surfaces: GitCommands.Scripts.ScriptSurfaces.CommitMenu | GitCommands.Scripts.ScriptSurfaces.RefMenu);
                GitCommands.Scripts.ScriptStorage.Save([script], GitCommands.AppSettings.SetString);
                RebuildHotkeyMap();

                var menuActions = GitCommands.Scripts.ScriptActions.ToDescriptors(_scripts, GitCommands.Scripts.ScriptSurfaces.CommitMenu);
                Console.Error.WriteLine($"[scripts] loaded {_scripts.Count}, commit-menu actions: {string.Join(", ", menuActions.Select(a => a.Caption))}");

                await RunScriptAsync(_scripts[0], _selectedRevision, refName: null);
                Console.Error.WriteLine($"[scripts] ran: {OperationStatus.Text}");

                // Injection probe: the selected subject carries shell metacharacters; the safe
                // expansion must deliver it verbatim without executing anything.
                GitCommands.Scripts.ExpandedScript probe = GitCommands.Scripts.ScriptTokenSubstitution.ExpandSafe(
                    "echo subject={selected.subject}",
                    new GitCommands.Scripts.ScriptTokenContext(SelectedSubject: _selectedRevision?.Subject),
                    new Dictionary<string, string>(),
                    GitCommands.Scripts.ScriptInterpreterKind.PosixShell);
                (string probeFile, string probeArguments) = GitCommands.Scripts.ScriptInterpreter.Resolve("shell", probe.Command, OperatingSystem.IsWindows());
                (bool probeOk, string probeOut) = await _session.RunProcessAsync(probeFile, probeArguments, probe.Environment);
                bool injected = System.IO.File.Exists(System.IO.Path.Combine(_session.WorkingDir, "injected"));
                Console.Error.WriteLine($"[scripts] injection probe: ok={probeOk} output={probeOut.Trim()} | injected file created: {injected}");

                IReadOnlyList<(string Name, string Expansion)> aliases = await _session.GetGitAliasesAsync();
                Console.Error.WriteLine($"[scripts] aliases: {string.Join(", ", aliases.Select(alias => $"{alias.Name}={alias.Expansion}"))}");
                Environment.Exit(0);
            };
        }

        if (Environment.GetEnvironmentVariable("GE_SPIKE_CONFLICTSTEST") == "1")
        {
            Loaded += async (_, _) =>
            {
                await Task.Delay(2000);
                Console.Error.WriteLine($"[conflicts] in conflicted merge: {_session.InConflictedMerge}");
                ConflictsWindow conflictsWindow = new(_session);
                TaskCompletionSource<string> harness = new();
                conflictsWindow.Loaded += async (_, _) => harness.SetResult(await conflictsWindow.RunHarnessAsync(GitCommands.Conflicts.ConflictOutcome.TakeRemote));
                conflictsWindow.Show(this);
                string report = await harness.Task;
                Console.Error.WriteLine($"[conflicts] {report} | still conflicted: {_session.InConflictedMerge}");
                Environment.Exit(0);
            };
        }

        if (Environment.GetEnvironmentVariable("GE_SPIKE_REWRITETEST") == "1")
        {
            Loaded += async (_, _) =>
            {
                await Task.Delay(2500);
                ObjectId target = _session.ResolveRef("HEAD~1")!.Value;
                GitRevision revision = _session.GetRevision(target);
                (bool rewordOk, string rewordOut) = await _session.RewriteCommitAsync(revision, GitCommands.Rewrite.RewriteTodoAction.Reword, "reworded subject\n\nnew body from harness");
                GitRevision reworded = _session.GetRevision(_session.ResolveRef("HEAD~1")!.Value);
                Console.Error.WriteLine($"[rewrite] reword: {(rewordOk ? "OK" : "FAIL")} | new subject: {reworded.Subject}");

                await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(_session.WorkingDir, "fixup-file"), "fixup change");
                await _session.RunGitWithEnvAsync("add fixup-file", new Dictionary<string, string>());
                await _session.RunGitWithEnvAsync($"commit -m \"fixup! {reworded.Subject}\"", new Dictionary<string, string>());
                string countBefore = (await _session.RunGitWithEnvAsync("rev-list --count HEAD", new Dictionary<string, string>())).Output.Trim();
                (bool foldOk, string foldOut) = await _session.AutosquashFoldAsync(reworded);
                string countAfter = (await _session.RunGitWithEnvAsync("rev-list --count HEAD", new Dictionary<string, string>())).Output.Trim();
                Console.Error.WriteLine($"[rewrite] autosquash fold: {(foldOk ? "OK" : "FAIL")} | commits {countBefore} -> {countAfter}");
                Environment.Exit(0);
            };
        }

        if (Environment.GetEnvironmentVariable("GE_SPIKE_COMPARETEST") == "1")
        {
            Loaded += async (_, _) =>
            {
                await Task.Delay(4000);
                ObjectId head = _session.CurrentCheckout;
                ObjectId? older = _session.ResolveRef("HEAD~5");
                if (older is null)
                {
                    Console.Error.WriteLine("[compare] HEAD~5 unresolvable");
                    Environment.Exit(1);
                    return;
                }

                CompareWindow compareWindow = new(_session, older.Value, "HEAD~5", head, "HEAD");
                compareWindow.Show(this);
                var (files, inlines) = await compareWindow.ProbeAsync();
                Console.Error.WriteLine($"[compare] HEAD~5..HEAD: {files} files, first diff {inlines} inlines");
                Environment.Exit(0);
            };
        }

        if (Environment.GetEnvironmentVariable("GE_SPIKE_REMOTESTEST") == "1")
        {
            Loaded += (_, _) =>
            {
                RemotesWindow remotesWindow = new(_session);
                remotesWindow.Loaded += (_, _) => _ = remotesWindow.RunHarnessAsync();
                remotesWindow.Show(this);
            };
        }

        if (Environment.GetEnvironmentVariable("GE_SPIKE_MENUTEST") == "1")
        {
            Loaded += (_, _) => _ = RunMenuHarnessAsync();
        }

        if (Environment.GetEnvironmentVariable("GE_SPIKE_SETTINGSTEST") is string settingsSnapshotDirectory)
        {
            Loaded += async (_, _) =>
            {
                SettingsWindow settingsWindow = new(_session);
                Task harness = null!;
                settingsWindow.Loaded += (_, _) => harness = settingsWindow.RunHarnessAsync(settingsSnapshotDirectory);
                await settingsWindow.ShowDialog(this);
                await harness;
                Environment.Exit(0);
            };
        }

        if (Environment.GetEnvironmentVariable("GE_SPIKE_COMMITTEST") is string snapshotDirectory)
        {
            Loaded += async (_, _) =>
            {
                CommitWindow commitWindow = new(_session);
                Task harness = null!;
                commitWindow.Loaded += (_, _) => harness = commitWindow.RunHarnessAsync(snapshotDirectory);
                await commitWindow.ShowDialog(this);
                await harness;
                Environment.Exit(0);
            };
        }
    }

    /// <summary>
    ///  The commit-info links use LinkFactory's targets: gitext:// links jump within the log,
    ///  anything else opens in the browser.
    /// </summary>
    private void HandleCommitInfoLink(string target)
    {
        const string internalPrefix = "gitext://";
        if (!target.StartsWith(internalPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // Link targets come from commit-message content, i.e. from whoever authored the
            // repository's history - only well-known schemes may reach the shell.
            if (Uri.TryCreate(target, UriKind.Absolute, out Uri? uri)
                && uri.Scheme is "http" or "https" or "mailto")
            {
                GitCommands.OsShellUtil.OpenUrlInDefaultBrowser(target);
            }

            return;
        }

        string[] parts = target[internalPrefix.Length..].Split('/', 2);
        if (parts.Length != 2)
        {
            return;
        }

        ObjectId? objectId = parts[0].ToLowerInvariant() switch
        {
            "gotocommit" => ObjectId.TryParse(parts[1], out ObjectId id) ? id : null,
            "gotobranch" or "gototag" => _session.ResolveRef(parts[1]),
            _ => null,
        };

        if (objectId is ObjectId target1)
        {
            LogControl.TryJumpTo(target1);
        }
    }

    private async Task LoadRefPanelAsync()
    {
        var (branches, remotes, tags) = await Task.Run(_session.GetRefPanel);
        IReadOnlyList<GitCommands.LeftPanel.StashTreeNode> stashes = await Task.Run(_session.GetStashPanel);
        IReadOnlyList<GitCommands.LeftPanel.WorktreeTreeNode> worktrees = await Task.Run(_session.GetWorktreePanel);

        GitCommands.LeftPanel.RefTreeNode Section(string name, IEnumerable<GitCommands.LeftPanel.RefTreeNode> children)
        {
            GitCommands.LeftPanel.RefTreeNode section = new() { Name = name, FullPath = "" };
            section.Children.AddRange(children);
            return section;
        }

        // the builder leaves the inactive group's caption to the views
        IEnumerable<GitCommands.LeftPanel.RefTreeNode> remoteNodes = remotes.Select(node =>
        {
            if (node.Kind is not GitCommands.LeftPanel.RefTreeNodeKind.InactiveGroup)
            {
                return node;
            }

            GitCommands.LeftPanel.RefTreeNode inactive = new() { Name = "Inactive", FullPath = "" };
            inactive.Children.AddRange(node.Children);
            return inactive;
        });

        RefTree.ItemsSource = new[]
        {
            Section($"Branches ({branches.Count})", branches),
            Section($"Remotes ({remotes.Count})", remoteNodes),
            Section($"Tags ({tags.Count})", tags),
            Section($"Stashes ({stashes.Count})", stashes.Select(stash => new GitCommands.LeftPanel.RefTreeNode
            {
                Name = stash.DisplayName,
                FullPath = stash.FullPath,
                ObjectId = stash.ObjectId,
            })),
            Section($"Worktrees ({worktrees.Count})", worktrees.Select(worktree => new GitCommands.LeftPanel.RefTreeNode
            {
                Name = worktree.IsCurrent ? $"{worktree.DisplayPath} (current)" : worktree.DisplayPath,
                FullPath = worktree.Worktree.Path,
                Kind = GitCommands.LeftPanel.RefTreeNodeKind.Worktree,
            })),
        };
    }

    private void OnRefTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (RefTree.SelectedItem is GitCommands.LeftPanel.RefTreeNode { ObjectId: ObjectId objectId })
        {
            LogControl.TryJumpTo(objectId);
        }
    }

    private async void OnRefTreeDoubleTapped(object? sender, global::Avalonia.Input.TappedEventArgs e)
    {
        if (RefTree.SelectedItem is not GitCommands.LeftPanel.RefTreeNode { Kind: GitCommands.LeftPanel.RefTreeNodeKind.LocalBranch, IsCurrent: false } branch)
        {
            return;
        }

        // the checkout dialog's local-changes choice, backed by the portable policy
        GitCommands.LocalChangesAction localChanges = GitCommands.LocalChangesAction.DontChange;
        bool stashThenReapply = false;
        if (await _session.IsDirtyAsync())
        {
            int choice = await ConfirmDialog.ShowAsync(
                this,
                "Checkout branch",
                $"You have uncommitted changes. How should they be handled when checking out {branch.FullPath}?",
                "Stash & reapply", "Merge", "Discard (reset)", "Leave as-is", "Cancel");

            switch (choice)
            {
                case 0:
                    localChanges = GitCommands.LocalChangesAction.Stash;
                    stashThenReapply = true;
                    break;
                case 1:
                    localChanges = GitCommands.LocalChangesAction.Merge;
                    break;
                case 2:
                    localChanges = GitCommands.LocalChangesAction.Reset;
                    break;
                case 3:
                    localChanges = GitCommands.LocalChangesAction.DontChange;
                    break;
                default:
                    return;
            }
        }

        if (stashThenReapply)
        {
            await RunOperationAsync($"Checkout {branch.FullPath}", async () =>
            {
                (bool stashed, string stashOutput) = await _session.StashSaveAsync();
                if (!stashed)
                {
                    return (false, stashOutput);
                }

                (bool success, string output) = await _session.CheckoutBranchAsync(branch.FullPath);
                if (!success)
                {
                    return (false, output);
                }

                return await _session.StashPopAsync();
            });
            return;
        }

        await RunOperationAsync($"Checkout {branch.FullPath}", () => _session.CheckoutBranchAsync(branch.FullPath, localChanges));
    }

    private async void OnFetchClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        => await RunOperationAsync("Fetch", _session.FetchAsync);

    private async void OnPullClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        // honor the "Default pull action" setting; ask when it is unset
        GitCommands.Pull.PullActionKind? action = GitCommands.AppSettings.DefaultPullAction switch
        {
            GitExtensions.Extensibility.Git.GitPullAction.Merge => GitCommands.Pull.PullActionKind.Merge,
            GitExtensions.Extensibility.Git.GitPullAction.Rebase => GitCommands.Pull.PullActionKind.Rebase,
            GitExtensions.Extensibility.Git.GitPullAction.Fetch
                or GitExtensions.Extensibility.Git.GitPullAction.FetchAll
                or GitExtensions.Extensibility.Git.GitPullAction.FetchPruneAll => GitCommands.Pull.PullActionKind.Fetch,
            _ => null,
        };

        if (action is null)
        {
            int choice = await ConfirmDialog.ShowAsync(this, "Pull", "How should the remote changes be integrated?", "Merge", "Rebase", "Fetch only", "Cancel");
            action = choice switch
            {
                0 => GitCommands.Pull.PullActionKind.Merge,
                1 => GitCommands.Pull.PullActionKind.Rebase,
                2 => GitCommands.Pull.PullActionKind.Fetch,
                _ => null,
            };

            if (action is null)
            {
                return;
            }
        }

        bool fetchAll = GitCommands.AppSettings.DefaultPullAction
            is GitExtensions.Extensibility.Git.GitPullAction.FetchAll
            or GitExtensions.Extensibility.Git.GitPullAction.FetchPruneAll;

        System.Collections.Generic.IReadOnlyList<string> remotes = await Task.Run(_session.GetRemoteNames);
        if (remotes.Count == 0)
        {
            await ConfirmDialog.ErrorAsync(this, "Pull", "No remote is configured.");
            return;
        }

        (string? defaultRemote, _, _) = _session.GetPushDefaults();
        GitCommands.Pull.PullOptions? options = await PullDialog.ShowAsync(
            this,
            remotes,
            defaultRemote,
            action.Value,
            defaultAllRemotes: fetchAll,
            defaultPrune: GitCommands.AppSettings.DefaultPullAction is GitExtensions.Extensibility.Git.GitPullAction.FetchPruneAll);
        if (options is null)
        {
            return;
        }

        await RunOperationAsync(options.Action is GitCommands.Pull.PullActionKind.Fetch ? "Fetch" : "Pull", () => _session.PullWithOptionsAsync(options));
    }

    private async void OnPushClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        System.Collections.Generic.IReadOnlyList<string> remotes = await Task.Run(_session.GetRemoteNames);
        if (remotes.Count == 0)
        {
            await ConfirmDialog.ErrorAsync(this, "Push", "No remote is configured.");
            return;
        }

        (string? defaultRemote, string? defaultRemoteBranch, bool defaultTrack) = _session.GetPushDefaults();
        PushChoice? choice = await PushDialog.ShowAsync(
            this, remotes, defaultRemote ?? remotes[0], _session.SelectedBranch, defaultRemoteBranch, defaultTrack);
        if (choice is null)
        {
            return;
        }

        (bool success, string output) = await _session.PushWithOptionsAsync(
            choice.Remote, choice.LocalBranch, choice.RemoteBranch, choice.Force, choice.Track);

        if (success)
        {
            OperationStatus.Text = "Push: done";
            await ReloadLogAsync();
            return;
        }

        // The rejection analyzer recognizes a non-fast-forward refusal and offers the safe retry.
        if (choice.Force is GitCommands.Git.ForcePushOptions.DoNotForce
            && GitCommands.Push.PushRejectionAnalyzer.Analyze(output, choice.LocalBranch).IsRejected
            && await ConfirmDialog.ConfirmAsync(this, "Push rejected", "The remote rejected the push (non-fast-forward).\nRetry with --force-with-lease?"))
        {
            await RunOperationAsync("Push (force with lease)", () => _session.PushWithOptionsAsync(
                choice.Remote, choice.LocalBranch, choice.RemoteBranch, GitCommands.Git.ForcePushOptions.ForceWithLease, choice.Track));
            return;
        }

        OperationStatus.Text = "Push failed";
        await ConfirmDialog.ErrorAsync(this, "Push failed", string.IsNullOrWhiteSpace(output) ? "The push failed." : output);
    }

    private async void OnNewBranchClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        string? name = await ConfirmDialog.InputAsync(this, "Create branch", "Branch name (created at the current checkout):", "feature/my-branch");
        if (name is null)
        {
            return;
        }

        await RunOperationAsync($"Create branch {name}", () => _session.CreateBranchAsync(name, checkout: true));
    }

    /// <summary>
    ///  Runs a git operation with the toolbar disabled, then refreshes everything history-shaped
    ///  (log, refs, branch info) - all four operations can move HEAD or the remote refs.
    /// </summary>
    private async Task RunOperationAsync(string title, Func<Task<(bool Success, string Output)>> operation)
    {
        SetToolbarEnabled(false);
        OperationStatus.Text = $"{title}…";
        try
        {
            (bool success, string output) = await operation();

            if (!success)
            {
                OperationStatus.Text = $"{title} failed";
                await ConfirmDialog.ErrorAsync(this, $"{title} failed", string.IsNullOrWhiteSpace(output) ? "The operation failed." : output);
                return;
            }

            OperationStatus.Text = $"{title}: done";
            await ReloadLogAsync();
        }
        catch (Exception ex)
        {
            OperationStatus.Text = $"{title} failed";
            await ConfirmDialog.ErrorAsync(this, $"{title} failed", ex.Message);
        }
        finally
        {
            SetToolbarEnabled(true);
        }
    }

    private void SetToolbarEnabled(bool enabled)
    {
        CommitToolButton.IsEnabled = enabled;
        FetchButton.IsEnabled = enabled;
        PullButton.IsEnabled = enabled;
        PushButton.IsEnabled = enabled;
        NewBranchButton.IsEnabled = enabled;
    }

    private void ShowBranchInfo()
    {
        GitCommands.Commit.BranchPushTarget pushTarget = _session.PushTarget;
        string pushTo = pushTarget.Kind switch
        {
            GitCommands.Commit.PushTargetKind.Tracked => pushTarget.Target!,
            GitCommands.Commit.PushTargetKind.DefaultRemoteUntracked => $"{pushTarget.Target} (untracked)",
            _ => "(remote not configured)",
        };

        BranchInfo.Text = $"{_session.SelectedBranch} → {pushTo}";
    }

    private async void OnOpenCommitClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        CommitWindow commitWindow = new(_session);
        await commitWindow.ShowDialog(this);

        if (commitWindow.Committed)
        {
            await ReloadLogAsync();
        }
    }

    private async void OnSettingsClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        SettingsWindow settingsWindow = new(_session);
        await settingsWindow.ShowDialog(this);
        RebuildHotkeyMap();
        Loc.Reload();
        ApplyThemeVariant();
        ApplyToolbarTranslations();
    }

    private async void OnOpenRepositoryClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        string? path = await OpenRepositoryDialog.ShowAsync(this, _session.IsValidRepository ? _session.WorkingDir : null);
        if (path is not null)
        {
            await SwitchRepositoryAsync(path);
        }
    }

    /// <summary>
    ///  The client's repository-switch transaction, driven by the same RepoSwitchPlan FormBrowse
    ///  binds: persist the new working dir + MRU on a valid switch, reset repository-scoped view
    ///  state only when the path actually changed, then restart the log stream.
    /// </summary>
    internal async Task SwitchRepositoryAsync(string path)
    {
        SliceSession newSession = new(path);
        if (!newSession.IsValidRepository)
        {
            await ConfirmDialog.ErrorAsync(this, Loc.T("Open repository"), Loc.T("The selected directory is not a valid git repository."));
            return;
        }

        GitCommands.Open.RepoSwitchPlan plan = GitCommands.Open.RepoSwitchPlan.Create(_session.WorkingDir, newSession.WorkingDir, isValidWorkingDir: true);
        _session = newSession;

        if (plan.PersistRecentWorkingDir)
        {
            GitCommands.AppSettings.RecentWorkingDir = newSession.WorkingDir;
            await GitCommands.UserRepositoryHistory.RepositoryHistoryManager.Locals.AddAsMostRecentAsync(newSession.WorkingDir);
            GitCommands.AppSettings.SaveSettings();
        }

        if (plan.ResetRepositoryScopedViewState)
        {
            _selectedRevision = null;
            _compareBaseRevision = null;
            _fileGroups.Clear();
            FileTree.ItemsSource = null;
            CommitHeader.Inlines?.Clear();
            CommitBody.Inlines?.Clear();
            RefTree.ItemsSource = null;
        }

        Title = $"Git Extensions - {_session.WorkingDir}";
        await ReloadLogAsync();
    }

    // Branch names for the recents menu, shared portable cache + the WinForms dedup policy.
    private readonly GitUI.IRepositoryCurrentBranchNameCache _recentBranchNames =
        new GitUI.RepositoryCurrentBranchNameCache(
            new GitUI.RepositoryCurrentBranchNameProvider(new GitCommands.GitExecutorProvider(new GitCommands.Git.GitDirectoryResolver())));
    private GitCommands.UserRepositoryHistory.BranchNameCacheUpdatePolicy? _recentBranchNamePolicy;

    private async void OnRecentReposClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        System.Collections.Generic.IList<GitCommands.UserRepositoryHistory.Repository> recent =
            await GitCommands.UserRepositoryHistory.RepositoryHistoryManager.Locals.LoadRecentHistoryAsync();
        System.Collections.Generic.IList<GitCommands.UserRepositoryHistory.Repository> favourites =
            await GitCommands.UserRepositoryHistory.RepositoryHistoryManager.Locals.LoadFavouriteHistoryAsync();

        TriggerRecentBranchNameUpdate(onlyIfEmpty: true, recent, favourites);

        GitCommands.UserRepositoryHistory.RecentRepoSplitterOptions options =
            GitCommands.UserRepositoryHistory.RecentRepoSplitterOptions.FromAppSettings();
        GitCommands.UserRepositoryHistory.RecentRepositoriesMenuModel model =
            GitCommands.UserRepositoryHistory.RecentRepositoryMenu.BuildRecent(recent, options, _recentBranchNames.GetCachedBranchName);
        System.Collections.Generic.IReadOnlyList<GitCommands.UserRepositoryHistory.FavouriteCategoryGroup> favouriteGroups =
            GitCommands.UserRepositoryHistory.RecentRepositoryMenu.BuildFavourites(favourites, options, _recentBranchNames.GetCachedBranchName);

        MenuFlyout flyout = new();

        if (favouriteGroups.Count > 0)
        {
            MenuItem favouritesRoot = new() { Header = Loc.T("Favourites") };
            foreach (GitCommands.UserRepositoryHistory.FavouriteCategoryGroup group in favouriteGroups)
            {
                MenuItem category = new() { Header = group.Category ?? Loc.T("(no category)") };
                foreach (GitCommands.UserRepositoryHistory.RepoMenuEntry entry in group.Entries)
                {
                    category.Items.Add(MakeItem(entry));
                }

                favouritesRoot.Items.Add(category);
            }

            flyout.Items.Add(favouritesRoot);
            flyout.Items.Add(new Separator());
        }

        foreach (GitCommands.UserRepositoryHistory.RepoMenuEntry entry in model.Pinned)
        {
            flyout.Items.Add(MakeItem(entry));
        }

        if (model.ShowSeparator)
        {
            flyout.Items.Add(new Separator());
        }

        foreach (GitCommands.UserRepositoryHistory.RepoMenuEntry entry in model.Recent)
        {
            flyout.Items.Add(MakeItem(entry));
        }

        if (model.Pinned.Count > 0 || model.Recent.Count > 0)
        {
            flyout.Items.Add(new Separator());
        }

        MenuItem openItem = new() { Header = Loc.T("Open repository...") };
        openItem.Click += OnOpenRepositoryClick;
        flyout.Items.Add(openItem);

        MenuItem cloneItem = new() { Header = Loc.T("Clone repository...") };
        cloneItem.Click += async (_, _) => await CloneRepositoryAsync();
        flyout.Items.Add(cloneItem);

        MenuItem initItem = new() { Header = Loc.T("Create new repository...") };
        initItem.Click += async (_, _) => await InitRepositoryAsync();
        flyout.Items.Add(initItem);

        flyout.ShowAt((Control)sender!);

        MenuItem MakeItem(GitCommands.UserRepositoryHistory.RepoMenuEntry entry)
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
    }

    /// <summary>Opens a repository from the recents menu, offering the WinForms invalid-repository cleanup when it went stale.</summary>
    internal async Task OpenRecentAsync(string path)
    {
        if (GitCommands.Open.OpenRepositoryModel.TryGetOpenablePath(path, System.IO.Directory.Exists, GitCommands.GitModule.IsValidGitWorkingDir) is string openablePath)
        {
            await SwitchRepositoryAsync(openablePath);
            return;
        }

        System.Collections.Generic.IList<GitCommands.UserRepositoryHistory.Repository> history =
            await GitCommands.UserRepositoryHistory.RepositoryHistoryManager.Locals.LoadRecentHistoryAsync();
        GitCommands.Open.InvalidRepositoryPromptOptions options = GitCommands.Open.InvalidRepositoryPromptOptions.Evaluate(
            history.Select(r => r.Path), GitCommands.GitModule.IsValidGitWorkingDir);

        if (!await ConfirmDialog.ConfirmAsync(this, Loc.T("Open repository"),
                string.Format(Loc.T("'{0}' is not a valid git repository. Remove it from the recent repositories?"), path)))
        {
            return;
        }

        await GitCommands.UserRepositoryHistory.RepositoryHistoryManager.Locals.RemoveRecentAsync(path);

        if (options.OfferRemoveAll
            && await ConfirmDialog.ConfirmAsync(this, Loc.T("Open repository"),
                string.Format(Loc.T("Remove all {0} invalid repositories from the recent list?"), options.InvalidCount)))
        {
            await GitCommands.UserRepositoryHistory.RepositoryHistoryManager.Locals.RemoveInvalidRepositoriesAsync(
                repoPath => GitCommands.GitModule.IsValidGitWorkingDir(repoPath));
        }
    }

    /// <summary>The clone flow: dialog over the portable models, run the clone, offer to open the result.</summary>
    internal async Task CloneRepositoryAsync()
    {
        CloneRequest? request = await CloneDialog.ShowAsync(this, _session);
        if (request is null)
        {
            return;
        }

        await RunOperationAsync($"Clone {request.From}", () => _session.CloneAsync(
            request.From, request.TargetDirectory, request.Bare, request.InitSubmodules, request.Branch, request.Depth, request.IsSingleBranch));

        await OfferToOpenAsync(request.TargetDirectory);
    }

    /// <summary>The init flow: dialog, git init (bare+shared when central), offer to open the result.</summary>
    internal async Task InitRepositoryAsync()
    {
        (string Directory, bool Central)? request = await InitDialog.ShowAsync(this, _session);
        if (request is null)
        {
            return;
        }

        (bool bare, bool shared) = GitCommands.Init.InitRepositoryModel.Options(request.Value.Central);
        (bool success, string output) = await _session.InitAsync(request.Value.Directory, bare, shared);
        if (!success)
        {
            await ConfirmDialog.ErrorAsync(this, Loc.T("Create new repository"), output);
            return;
        }

        OperationStatus.Text = output.Trim();
        await OfferToOpenAsync(request.Value.Directory);
    }

    private async Task OfferToOpenAsync(string directory)
    {
        if (GitCommands.Open.OpenRepositoryModel.TryGetOpenablePath(directory, System.IO.Directory.Exists, GitCommands.GitModule.IsValidGitWorkingDir) is string openablePath
            && await ConfirmDialog.ConfirmAsync(this, Loc.T("Open repository"), string.Format(Loc.T("Open the repository at '{0}' now?"), directory)))
        {
            await SwitchRepositoryAsync(openablePath);
        }
    }

    private void TriggerRecentBranchNameUpdate(
        bool onlyIfEmpty,
        System.Collections.Generic.IList<GitCommands.UserRepositoryHistory.Repository> recent,
        System.Collections.Generic.IList<GitCommands.UserRepositoryHistory.Repository> favourites)
    {
        _recentBranchNamePolicy ??= new GitCommands.UserRepositoryHistory.BranchNameCacheUpdatePolicy(_recentBranchNames);
        if (!_recentBranchNamePolicy.ShouldUpdate(onlyIfEmpty))
        {
            return;
        }

        string[] paths = [.. recent.Concat(favourites).Select(r => r.Path).Distinct(StringComparer.InvariantCulture)];
        if (paths.Length > 0)
        {
            _ = Task.Run(() => GitCommands.UserRepositoryHistory.BranchNameCacheUpdater.UpdateBranchNames(paths, _recentBranchNames, CancellationToken.None));
        }
    }

    /// <summary>The Colors page's variant choice, applied application-wide (blank follows the system).</summary>
    private static void ApplyThemeVariant()
    {
        if (global::Avalonia.Application.Current is { } application)
        {
            application.RequestedThemeVariant = GitCommands.AppSettings.ClientThemeVariant switch
            {
                "Light" => global::Avalonia.Styling.ThemeVariant.Light,
                "Dark" => global::Avalonia.Styling.ThemeVariant.Dark,
                _ => global::Avalonia.Styling.ThemeVariant.Default,
            };
        }
    }

    /// <summary>The static toolbar captions go through the §11 join (menus re-render per open).</summary>
    private void ApplyToolbarTranslations()
    {
        CommitToolButton.Content = Loc.T("Commit...");
        FetchButton.Content = Loc.T("Fetch");
        PullButton.Content = Loc.T("Pull");
        PushButton.Content = Loc.T("Push");
        NewBranchButton.Content = Loc.T("New branch...");
        RemotesButton.Content = Loc.T("Remotes...");
        SettingsButton.Content = Loc.T("Settings...");
        ResolveConflictsButton.Content = Loc.T("Resolve conflicts...");
    }

    /// <summary>
    ///  Restarts the log stream after history changed (a commit): stop the reader, let any
    ///  in-flight batch drain, park the lane pump, clear, stream again.
    /// </summary>
    private async Task ReloadLogAsync()
    {
        _logCts.Cancel();
        _logCts = new CancellationTokenSource();
        await Task.Delay(200);
        UpdateRebaseBar();
        await LogControl.ResetAsync();
        StartLogStream();
    }

    /// <summary>The continue/abort affordances appear while a rebase (e.g. an "Edit commit" stop) is in flight.</summary>
    private void UpdateRebaseBar()
    {
        bool inRebase = _session.InRebase;
        RebaseContinueButton.IsVisible = inRebase;
        RebaseAbortButton.IsVisible = inRebase;
        ResolveConflictsButton.IsVisible = _session.InConflictedMerge;
    }

    private async void OnResolveConflictsClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        => await ShowConflictsWindowAsync();

    /// <summary>The conflicts window; on the all-resolved close it offers the commit (the WinForms flow).</summary>
    private async Task ShowConflictsWindowAsync()
    {
        ConflictsWindow conflictsWindow = new(_session);
        await conflictsWindow.ShowDialog(this);
        UpdateRebaseBar();

        if (conflictsWindow.ShouldOfferCommit
            && await ConfirmDialog.ConfirmAsync(this, "Conflicts resolved", "All conflicts are resolved. Commit the merge now?"))
        {
            CommitWindow commitWindow = new(_session);
            await commitWindow.ShowDialog(this);
        }

        await ReloadLogAsync();
    }

    private async void OnRebaseContinueClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        => await RunOperationAsync("Rebase continue", _session.ContinueRebaseAsync);

    private async void OnRebaseAbortClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        => await RunOperationAsync("Rebase abort", _session.AbortRebaseAsync);

    private void StartLogStream()
    {
        if (!_session.IsValidRepository)
        {
            CommitBody.Text = $"Not a git repository: {_session.WorkingDir}";
            return;
        }

        ShowBranchInfo();

        Stopwatch loadStopwatch = Stopwatch.StartNew();
        CancellationToken cancellationToken = _logCts.Token;

        _ = Task.Run(() =>
        {
            // Must precede Add: relativity (lane coloring) propagates from the checked-out node.
            LogControl.Graph.HeadId = _session.CurrentCheckout;

            try
            {
                _session.StreamLog(
                    onBatch: revisions =>
                    {
                        foreach (GitRevision revision in revisions)
                        {
                            LogControl.Graph.Add(revision);
                        }

                        Dispatcher.UIThread.Post(LogControl.NotifyRowsChanged, DispatcherPriority.Background);
                    },
                    onCompleted: () =>
                    {
                        loadStopwatch.Stop();
                        LogControl.Graph.LoadingCompleted();

                        // Build the whole lane layout ahead of time through the control's
                        // serialized pump so interactive jumps never wait on lane building.
                        _ = LogControl.EnsureCachedToAsync(LogControl.Graph.Count - 1);
                        Dispatcher.UIThread.Post(() =>
                        {
                            LogControl.NotifyRowsChanged();
                            Title = $"Git Extensions - {_session.WorkingDir} - {LogControl.Count:n0} commits in {loadStopwatch.Elapsed.TotalSeconds:0.0}s";
                            _ = LoadRefPanelAsync();

                            if (Environment.GetEnvironmentVariable("GE_SPIKE_BENCH") == "1")
                            {
                                _ = RunScrollBenchmarkAsync();
                            }

                            // Verification hook: resolve a ref and jump to it, reporting whether
                            // the commit has a row (regression check for the log's ref coverage).
                            if (Environment.GetEnvironmentVariable("GE_SPIKE_JUMPTEST") is string jumpRef)
                            {
                                ObjectId? jumpId = _session.ResolveRef(jumpRef);
                                bool jumped = jumpId is ObjectId id && LogControl.TryJumpTo(id);
                                Console.Error.WriteLine($"[jump] {jumpRef} -> {jumpId?.ToShortString() ?? "unresolved"}: {(jumped ? "OK" : "NO ROW")}");
                                Environment.Exit(jumped ? 0 : 1);
                            }

                            // Verification hook: park the view at a fixed row so the rendering
                            // can be compared against the reference topology at that position.
                            if (int.TryParse(Environment.GetEnvironmentVariable("GE_SPIKE_SCROLLTO"), out int scrollTo))
                            {
                                LogControl.ScrollToRow(scrollTo);
                            }

                            // Render the log control to a PNG in-process and exit - XWayland
                            // gates external X screen grabs, and this is deterministic anyway.
                            if (Environment.GetEnvironmentVariable("GE_SPIKE_SNAPSHOT") is string snapshotPath)
                            {
                                _ = SaveSnapshotAsync(snapshotPath);
                            }
                        });
                    },
                    onError: ex => Dispatcher.UIThread.Post(() => CommitBody.Text = ex.ToString()),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    /// <summary>
    ///  The spike's acceptance harness (GE_SPIKE_BENCH=1): page through the whole log, then jump
    ///  randomly, sampling per-frame render cost; prints the stats and exits.
    /// </summary>
    private async Task RunScrollBenchmarkAsync()
    {
        List<double> frameMillis = [];
        int rowsPerPage = (int)(LogControl.Bounds.Height / 24);
        int count = LogControl.Count;

        // Full lane-layout build cost, measured up front (the model builds lazily otherwise).
        Stopwatch cacheStopwatch = Stopwatch.StartNew();
        await LogControl.EnsureCachedToAsync(count - 1);
        cacheStopwatch.Stop();

        static long RssMb() => Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024;
        long rssAfterBuild = RssMb();

        Stopwatch scrollStopwatch = Stopwatch.StartNew();
        for (int row = 0; row < count; row += rowsPerPage)
        {
            LogControl.ScrollToRow(row);
            await LogControl.NextRenderAsync();
            frameMillis.Add(LogControl.LastRenderMillis);
        }

        scrollStopwatch.Stop();
        long rssAfterScroll = RssMb();

        Random random = new(1234);
        List<double> jumpMillis = [];
        for (int i = 0; i < 200; i++)
        {
            LogControl.ScrollToRow(random.Next(count));
            await LogControl.NextRenderAsync();
            jumpMillis.Add(LogControl.LastRenderMillis);
        }

        long rssAfterJumps = RssMb();

        frameMillis.Sort();
        jumpMillis.Sort();
        double Pct(List<double> values, double p) => values[(int)(values.Count * p)];

        Console.Error.WriteLine($"SPIKE_BENCH commits={count} lanesBuild={cacheStopwatch.Elapsed.TotalSeconds:0.00}s " +
            $"pages={frameMillis.Count} pageScroll={scrollStopwatch.Elapsed.TotalSeconds:0.00}s " +
            $"frame_p50={Pct(frameMillis, 0.5):0.00}ms frame_p95={Pct(frameMillis, 0.95):0.00}ms frame_max={frameMillis[^1]:0.00}ms " +
            $"jump_p50={Pct(jumpMillis, 0.5):0.00}ms jump_p95={Pct(jumpMillis, 0.95):0.00}ms " +
            $"rss build/scroll/jumps={rssAfterBuild}/{rssAfterScroll}/{rssAfterJumps}MB");

        ExitAfterHarness();
    }

    private async Task SaveSnapshotAsync(string path)
    {
        int lastVisible = LogControl.FirstVisibleRow + (int)(LogControl.Bounds.Height / 24) + 1;
        await LogControl.EnsureCachedToAsync(Math.Min(LogControl.Count - 1, lastVisible));

        // Select the top visible revision so the snapshot also exercises the commit-info and
        // diff panels, then give their async loads a moment to land.
        await Dispatcher.UIThread.InvokeAsync(() => LogControl.SelectRow(LogControl.FirstVisibleRow));
        await Task.Delay(2500);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            PixelSize size = new((int)Bounds.Width, (int)Bounds.Height);
            using RenderTargetBitmap bitmap = new(size);
            bitmap.Render(this);
            bitmap.Save(path);
        });

        ExitAfterHarness();
    }

    // Environment.Exit tears the process down mid-composite and intermittently segfaults
    // (losing the harness output); a lifetime shutdown drains the render loop first.
    private static void ExitAfterHarness()
        => Dispatcher.UIThread.Post(() =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
            else
            {
                Environment.Exit(0);
            }
        });

    private readonly Dictionary<GitItemStatus, GitUI.FileStatusWithDescription> _fileGroups = new(ReferenceEqualityComparer.Instance);
    private CancellationTokenSource? _fileDiffCts;

    private async Task ShowRevisionAsync(GitRevision revision)
    {
        _selectionCts?.Cancel();
        _selectionCts = new CancellationTokenSource();
        CancellationToken cancellationToken = _selectionCts.Token;

        try
        {
            var (header, body) = await Task.Run(() => _session.GetCommitInfo(revision), cancellationToken);
            IReadOnlyList<GitUI.FileStatusWithDescription> groups =
                await Task.Run(() => _session.GetRevisionFileGroups(revision, cancellationToken), cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                CommitHeader.Inlines!.Clear();
                CommitHeader.Inlines.AddRange(InlineRendering.ToInlines(header, HandleCommitInfoLink));

                CommitBody.Inlines!.Clear();
                CommitBody.Inlines.AddRange(InlineRendering.ToInlines(body, HandleCommitInfoLink));

                ShowFileGroups(groups);
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CommitBody.Text = ex.ToString();
        }
    }

    private void ShowFileGroups(IReadOnlyList<GitUI.FileStatusWithDescription> groups)
    {
        _fileGroups.Clear();
        foreach (GitUI.FileStatusWithDescription group in groups)
        {
            foreach (GitItemStatus status in group.Statuses)
            {
                _fileGroups[status] = group;
            }
        }

        // The same policy as the WinForms list: show group headers only for multiple groups.
        (bool showDiffGroups, _, _, _) = GitCommands.FileStatus.FileStatusGroupPolicy.ComputeFlags(groups, groupByRevision: false, GitCommands.FileStatus.GitGrepState.None);

        List<StatusNode> roots = [];
        foreach (GitUI.FileStatusWithDescription group in groups)
        {
            StatusNode groupTree = StatusNode.BuildTree(group.Statuses);
            if (showDiffGroups)
            {
                groupTree.Text = GitCommands.FileStatus.FileStatusGroupPolicy.GetGroupName(group, group.Statuses.Count);
                roots.Add(groupTree);
            }
            else
            {
                roots.AddRange(groupTree.Children);
            }
        }

        FileTree.ItemsSource = roots;

        StatusNode? firstLeaf = roots.SelectMany(Leaves).FirstOrDefault();
        if (firstLeaf is not null)
        {
            FileTree.SelectedItems!.Clear();
            FileTree.SelectedItems.Add(firstLeaf);
        }
        else
        {
            DiffText.Inlines!.Clear();
            DiffGutter.Text = "";
        }

        static IEnumerable<StatusNode> Leaves(StatusNode node)
            => node.Status is not null ? [node] : node.Children.SelectMany(Leaves);
    }

    /// <summary>The file tree's menu projects from the registry's file-status surface.</summary>
    private void OnFileTreeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        GitItemStatus? file = FileTree.SelectedItems!.OfType<StatusNode>().FirstOrDefault()?.Status;
        if (file is null)
        {
            return;
        }

        e.Handled = true;
        Dictionary<string, Func<GitItemStatus, Task>> handlers = new()
        {
            ["file.history"] = status =>
            {
                new FileHistoryWindow(_session, status.Name, showBlame: false).Show(this);
                return Task.CompletedTask;
            },
            ["file.blame"] = status =>
            {
                new FileHistoryWindow(_session, status.Name, showBlame: true).Show(this);
                return Task.CompletedTask;
            },
            ["file.copyPath"] = status => CopyToClipboardAsync(status.Name),
        };

        ContextMenu menu = BuildMenu(
            GitCommands.Actions.GridMenuRegistry.FileActions,
            action => handlers.ContainsKey(action.Id),
            _ => true,
            action => handlers[action.Id](file));

        if (menu.Items.Count > 0)
        {
            menu.Open(FileTree);
        }
    }

    private async void OnRemotesClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        RemotesWindow remotesWindow = new(_session);
        await remotesWindow.ShowDialog(this);
        await LoadRefPanelAsync();
    }

    private void OnFileTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        GitItemStatus? file = FileTree.SelectedItems!.OfType<StatusNode>().FirstOrDefault()?.Status;
        if (file is null || !_fileGroups.TryGetValue(file, out GitUI.FileStatusWithDescription? group))
        {
            return;
        }

        _fileDiffCts?.Cancel();
        _fileDiffCts = new CancellationTokenSource();
        _ = ShowRevisionFileDiffAsync(group.FirstRev?.ObjectId, group.SecondRev.ObjectId, file, _fileDiffCts.Token);
    }

    private async Task ShowRevisionFileDiffAsync(ObjectId? firstId, ObjectId secondId, GitItemStatus file, CancellationToken cancellationToken)
    {
        try
        {
            var (diffText, spans, lineNumbers) = await Task.Run(() => _session.GetRevisionFileDiff(firstId, secondId, file), cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                DiffText.Inlines!.Clear();
                DiffText.Inlines.AddRange(InlineRendering.ToInlines(diffText, spans));
                DiffGutter.Text = LineNumberGutter.Build(diffText, lineNumbers);
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            DiffText.Text = ex.ToString();
        }
    }
}
