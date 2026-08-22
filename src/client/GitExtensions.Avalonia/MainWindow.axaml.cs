using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
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
        BuildMainMenu();
        Title = $"Git Extensions - {_session.WorkingDir}";

        LogControl.RevisionSelected += (_, revision) =>
        {
            _selectedRevision = revision;
            _ = ShowRevisionAsync(revision);
        };
        LogControl.ContextRequested += OnLogContextRequested;
        RefTree.ContextRequested += OnRefTreeContextRequested;
        FileTree.ContextRequested += OnFileTreeContextRequested;
        DiffPaneMenu.Attach(
            DiffText,
            () => _browseDiffText,
            getPatchTarget: () => _browseDiffFile is { IsNew: false } ? GitCommands.Actions.DiffLineTarget.Committed : GitCommands.Actions.DiffLineTarget.None,
            runPatchVerb: RunCommittedLinePatchAsync);
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

        StartCloneButton.Click += async (_, _) => await CloneRepositoryAsync();
        StartInitButton.Click += async (_, _) => await InitRepositoryAsync();
        StartSearchBox.TextChanged += async (_, _) =>
        {
            if (StartPanel.IsVisible)
            {
                await RefreshStartViewAsync();
            }
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

        // Verification hook: started on an invalid directory, the start page must appear with the
        // grouped repositories; opening the first recent entry must switch into it.
        if (Environment.GetEnvironmentVariable("GE_SPIKE_STARTTEST") == "1")
        {
            Loaded += async (_, _) =>
            {
                await Task.Delay(1000);
                bool startVisible = StartPanel.IsVisible;
                int rows = StartGroupsPanel.Children.Count;

                var history = await GitCommands.UserRepositoryHistory.RepositoryHistoryManager.Locals.LoadRecentHistoryAsync();
                string? firstValid = history.Select(r => r.Path).FirstOrDefault(GitCommands.GitModule.IsValidGitWorkingDir);
                bool switched = false;
                if (firstValid is not null)
                {
                    await OpenRecentAsync(firstValid);
                    await Task.Delay(2000);
                    switched = _session.IsValidRepository && !StartPanel.IsVisible && LogControl.Count > 0;
                }

                Console.Error.WriteLine($"[start] visible:{startVisible} rows:{rows} switched:{switched} commits:{LogControl.Count}");
                Environment.Exit(startVisible && rows > 0 && switched ? 0 : 1);
            };
        }

        // Verification hook: the stash lifecycle through the session - save with message/options,
        // list, apply, drop (run against a scratch repo with a dirty file).
        if (Environment.GetEnvironmentVariable("GE_SPIKE_STASHTEST") == "1")
        {
            Loaded += async (_, _) =>
            {
                (bool saveOk, string saveOut) = await _session.StashSaveAsync("harness stash", keepIndex: false, includeUntracked: true);
                int countAfterSave = _session.GetStashPanel().Count;
                string? selector = _session.GetStashPanel().FirstOrDefault()?.ReflogSelector;
                (bool showOk, string showOut) = selector is null ? (false, "") : await _session.StashShowAsync(selector);
                (bool applyOk, _) = selector is null ? (false, "") : await _session.StashApplyAsync(selector);
                (bool dropOk, _) = selector is null ? (false, "") : await _session.StashDropAsync(selector);
                int countAfterDrop = _session.GetStashPanel().Count;

                Console.Error.WriteLine($"[stash] save:{saveOk} count:{countAfterSave} show:{showOk}({showOut.Trim().Replace("\n", ";")}) apply:{applyOk} drop:{dropOk} countAfterDrop:{countAfterDrop}");
                Environment.Exit(saveOk && countAfterSave > 0 && showOk && applyOk && dropOk && countAfterDrop == countAfterSave - 1 ? 0 : 1);
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

        if (Environment.GetEnvironmentVariable("GE_SPIKE_LINEPATCHTEST") == "1")
        {
            // Exercises the line-patch engine end-to-end: stage/unstage/reset selected lines
            // on a two-hunk worktree diff, then revert lines from a committed diff.
            // MUTATES the repo - scratch repos only.
            Loaded += async (_, _) =>
            {
                void Report(string name, bool ok, string detail = "")
                    => Console.Error.WriteLine($"[linepatch] {name}: {(ok ? "OK" : "FAIL")}{(detail.Length > 0 ? $" | {detail}" : "")}");

                async Task<(bool, string)> Git(GitExtUtils.GitArgumentBuilder args) => await _session.RunBatchRefCommandAsync(args);

                await Task.Delay(2500);
                string path = System.IO.Path.Combine(_session.WorkingDir, "lp.txt");
                string[] numbers = ["one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
                    "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty"];
                System.IO.File.WriteAllText(path, string.Join("\n", numbers) + "\n");
                await Git(new GitExtUtils.GitArgumentBuilder("add") { "lp.txt" });
                await Git(new GitExtUtils.GitArgumentBuilder("commit") { "-m", "lp-base".Quote() });

                string[] modified = [.. numbers];
                modified[1] = "two-changed";
                modified[17] = "eighteen-changed";
                System.IO.File.WriteAllText(path, string.Join("\n", modified) + "\n");

                (IReadOnlyList<GitItemStatus> unstaged, _) = _session.GetWorkTreeStatus(CancellationToken.None);
                GitItemStatus file = unstaged.First(status => status.Name == "lp.txt");

                async Task<bool> RunVerb(GitCommands.Patches.LinePatchVerb verb, string diffText, string firstMarker, string lastMarker)
                {
                    int start = diffText.IndexOf(firstMarker, StringComparison.Ordinal);
                    int end = diffText.IndexOf(lastMarker, StringComparison.Ordinal) + lastMarker.Length;
                    GitCommands.Patches.LinePatchPlan? plan = GitCommands.Patches.LinePatchPlanner.Plan(
                        verb, diffText, start, end - start, _session.FilesEncoding);
                    if (plan is null)
                    {
                        return false;
                    }

                    (bool ok, string output) = await _session.ApplyLinePatchAsync(plan);
                    if (!ok)
                    {
                        Console.Error.WriteLine($"[linepatch] {verb} apply output: {output}");
                    }

                    return ok;
                }

                // Stage only the first hunk's lines.
                (string wtDiff, _, _) = _session.GetWorkTreeFileDiff(file, staged: false);
                bool staged1 = await RunVerb(GitCommands.Patches.LinePatchVerb.Stage, wtDiff, "-two\n", "+two-changed\n");
                (_, string cached) = await Git(new GitExtUtils.GitArgumentBuilder("diff") { "--cached", "--", "lp.txt" });
                Report("stage-lines", staged1 && cached.Contains("two-changed") && !cached.Contains("eighteen-changed"));

                // Unstage them again from the index diff.
                (string indexDiff, _, _) = _session.GetWorkTreeFileDiff(file, staged: true);
                bool unstaged1 = await RunVerb(GitCommands.Patches.LinePatchVerb.Unstage, indexDiff, "-two\n", "+two-changed\n");
                (_, string cachedAfter) = await Git(new GitExtUtils.GitArgumentBuilder("diff") { "--cached", "--", "lp.txt" });
                Report("unstage-lines", unstaged1 && !cachedAfter.Contains("two-changed"));

                // Reset the first hunk's lines in the worktree; the second change must survive.
                (string wtDiff2, _, _) = _session.GetWorkTreeFileDiff(file, staged: false);
                bool reset1 = await RunVerb(GitCommands.Patches.LinePatchVerb.ResetWorkTree, wtDiff2, "-two\n", "+two-changed\n");
                string afterReset = System.IO.File.ReadAllText(path);
                Report("reset-lines", reset1 && afterReset.Contains("\ntwo\n") && afterReset.Contains("eighteen-changed"));

                // Commit the remaining change, then revert its lines from the committed diff.
                await Git(new GitExtUtils.GitArgumentBuilder("add") { "lp.txt" });
                await Git(new GitExtUtils.GitArgumentBuilder("commit") { "-m", "lp-second".Quote() });
                ObjectId head = _session.ResolveRef("HEAD")!.Value;
                ObjectId parent = _session.ResolveRef("HEAD~1")!.Value;
                (string committedDiff, _, _) = _session.GetRevisionFileDiff(parent, head, new GitItemStatus("lp.txt") { IsTracked = true });
                bool reverted = await RunVerb(GitCommands.Patches.LinePatchVerb.Revert, committedDiff, "-eighteen\n", "+eighteen-changed\n");
                string afterRevert = System.IO.File.ReadAllText(path);
                Report("revert-committed-lines", reverted && afterRevert.Contains("\neighteen\n") && !afterRevert.Contains("eighteen-changed"));

                Environment.Exit(0);
            };
        }

        if (Environment.GetEnvironmentVariable("GE_SPIKE_REFOPSTEST") == "1")
        {
            // Exercises the batch-ref operations engine end-to-end (seed, force gate, delete,
            // remote-counterpart delete, push with per-row porcelain results).
            // MUTATES the repo and its remote - scratch repos only.
            Loaded += async (_, _) =>
            {
                void Report(string name, bool ok, string detail = "")
                    => Console.Error.WriteLine($"[refops] {name}: {(ok ? "OK" : "FAIL")}{(detail.Length > 0 ? $" | {detail}" : "")}");

                await Task.Delay(2500);

                await _session.RunBatchRefCommandAsync(new GitExtUtils.GitArgumentBuilder("branch") { "bat/merged" });
                await _session.RunBatchRefCommandAsync(new GitExtUtils.GitArgumentBuilder("tag") { "bat-tag" });
                await _session.RunBatchRefCommandAsync(new GitExtUtils.GitArgumentBuilder("push") { "-u", "origin", "bat/merged" });
                await _session.RunBatchRefCommandAsync(new GitExtUtils.GitArgumentBuilder("checkout") { "-b", "bat/unmerged" });
                System.IO.File.WriteAllText(System.IO.Path.Combine(_session.WorkingDir, "bat.txt"), "x\n");
                await _session.RunBatchRefCommandAsync(new GitExtUtils.GitArgumentBuilder("add") { "bat.txt" });
                await _session.RunBatchRefCommandAsync(new GitExtUtils.GitArgumentBuilder("commit") { "-m", "bat-commit".Quote() });
                await _session.RunBatchRefCommandAsync(new GitExtUtils.GitArgumentBuilder("checkout") { "-" });

                IReadOnlyList<GitCommands.Refs.BatchRefRow> rows = await _session.GetBatchRefRowsAsync(
                [
                    ("bat/merged", GitCommands.Refs.BatchRefKind.LocalBranch),
                    ("bat/unmerged", GitCommands.Refs.BatchRefKind.LocalBranch),
                    ("bat-tag", GitCommands.Refs.BatchRefKind.Tag),
                ]);
                Report("seed", rows.Count == 3, string.Join("; ", rows.Select(row => $"{row.Name} merged={row.MergedIntoCurrent} up={row.Upstream}")));
                Report("force-gate", GitCommands.Refs.BatchRefOperations.RequiresForceDelete(rows));

                GitCommands.Refs.BatchRefCommand push = GitCommands.Refs.BatchRefOperations.BuildPushCommand(
                    [.. rows.Where(row => row.Name != "bat/merged")], "origin", forceWithLease: false);
                (bool pushOk, string pushOut) = await _session.RunBatchRefCommandAsync(push.Arguments);
                var pushResults = GitCommands.Refs.BatchRefOperations.ParseResults(push, pushOk, pushOut);
                Report("push-batch", pushResults.All(result => result.Outcome is GitCommands.Refs.BatchRefOutcome.Succeeded),
                    string.Join("; ", pushResults.Select(result => $"{result.Name}={result.Outcome}")));

                var deleteCommands = GitCommands.Refs.BatchRefOperations.BuildDeleteCommands(
                    [.. rows], force: true, deleteRemoteCounterparts: true);
                List<GitCommands.Refs.BatchRefRowResult> deleteResults = [];
                foreach (GitCommands.Refs.BatchRefCommand command in deleteCommands)
                {
                    (bool ok, string output) = await _session.RunBatchRefCommandAsync(command.Arguments);
                    deleteResults.AddRange(GitCommands.Refs.BatchRefOperations.ParseResults(command, ok, output));
                }

                Report("delete-batch", deleteResults.All(result => result.Outcome is GitCommands.Refs.BatchRefOutcome.Succeeded),
                    string.Join("; ", deleteResults.Select(result => $"{result.Name}={result.Outcome}")));
                Report("counterpart-covered", deleteCommands.Any(command => command.Kind is GitCommands.Refs.BatchRefCommandKind.DeleteRemoteCounterparts));

                if (Environment.GetEnvironmentVariable("GE_SPIKE_REFOPSTEST_SNAPSHOT") is string snapshotPath)
                {
                    RefOperationsWindow window = new(_session, rows, rows.Select(row => row.Name).ToHashSet());
                    window.Show(this);
                    await Task.Delay(700);
                    global::Avalonia.PixelSize size = new((int)window.Bounds.Width, (int)window.Bounds.Height);
                    using RenderTargetBitmap bitmap = new(size);
                    bitmap.Render(window);
                    bitmap.Save(snapshotPath);
                    Report("snapshot", true, snapshotPath);
                }

                Environment.Exit(0);
            };
        }

        if (Environment.GetEnvironmentVariable("GE_SPIKE_FILEOPSTEST") == "1")
        {
            // Exercises the per-file session operations behind the file context menu.
            // MUTATES the repo (files, index, .gitignore) - scratch repos only.
            Loaded += async (_, _) =>
            {
                async Task Report(string name, Task<(bool Success, string Output)> operation)
                {
                    (bool success, string output) = await operation;
                    Console.Error.WriteLine($"[fileops] {name}: {(success ? "OK" : "FAIL")} | {output.Replace("\n", " / ").Trim()}");
                }

                string Abs(string name) => System.IO.Path.Combine(_session.WorkingDir, name);

                await Report("ignore-append", _session.AddToGitIgnoreAsync(["harness-ignored.tmp"], localExclude: false));
                await Report("exclude-append", _session.AddToGitIgnoreAsync(["harness-excluded.tmp"], localExclude: true));

                System.IO.File.WriteAllText(Abs("harness-file.txt"), "one\n");
                (IReadOnlyList<GitItemStatus> unstaged, _) = _session.GetWorkTreeStatus(CancellationToken.None);
                GitItemStatus? newFile = unstaged.FirstOrDefault(status => status.Name == "harness-file.txt");
                Console.Error.WriteLine($"[fileops] status-sees-new: {(newFile is not null ? "OK" : "FAIL")}");
                if (newFile is not null)
                {
                    await Report("stage", Task.Run(() => _session.StageFiles([newFile])));
                    (_, IReadOnlyList<GitItemStatus> staged) = _session.GetWorkTreeStatus(CancellationToken.None);
                    if (staged.FirstOrDefault(status => status.Name == "harness-file.txt") is GitItemStatus stagedFile)
                    {
                        // GitModule filters on the item's current flag, so keep it in sync between
                        // toggles (a fresh status query would carry the updated flag).
                        await Report("skip-worktree-on", _session.SetSkipWorktreeAsync([stagedFile], skipWorktree: true));
                        stagedFile.IsSkipWorktree = true;
                        await Report("skip-worktree-off", _session.SetSkipWorktreeAsync([stagedFile], skipWorktree: false));
                        stagedFile.IsSkipWorktree = false;
                        await Report("assume-unchanged-on", _session.SetAssumeUnchangedAsync([stagedFile], assumeUnchanged: true));
                        stagedFile.IsAssumeUnchanged = true;
                        await Report("assume-unchanged-off", _session.SetAssumeUnchangedAsync([stagedFile], assumeUnchanged: false));
                    }

                    await Report("rename", _session.RenameFileAsync(isFolder: false, "harness-file.txt", "harness-renamed.txt"));
                    await Report("stop-tracking", _session.StopTrackingFileAsync("harness-renamed.txt"));
                    (IReadOnlyList<GitItemStatus> afterStop, _) = _session.GetWorkTreeStatus(CancellationToken.None);
                    if (afterStop.FirstOrDefault(status => status.Name == "harness-renamed.txt") is GitItemStatus leftover)
                    {
                        await Report("reset-delete-new", _session.ResetFileChangesAsync([leftover], toHead: true, resetAndDelete: true));
                    }

                    Console.Error.WriteLine($"[fileops] file-gone: {(!System.IO.File.Exists(Abs("harness-renamed.txt")) ? "OK" : "FAIL")}");
                }

                byte[]? headBytes = await _session.GetFileBytesAtRevisionAsync("README.md", _session.CurrentCheckout);
                Console.Error.WriteLine($"[fileops] bytes-at-head(README.md): {(headBytes is null ? "null (no README at HEAD)" : $"OK {headBytes.Length}B")}");
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
        // Visibility + order come from the Left panel settings page.
        IReadOnlyList<GitCommands.LeftPanel.LeftPanelSection> visibleSections =
            GitCommands.Settings.Pages.LeftPanelPageModel.VisibleSectionsInOrder();

        bool NeedsRefs(GitCommands.LeftPanel.LeftPanelSection section)
            => visibleSections.Contains(section);

        var (branches, remotes, tags) =
            NeedsRefs(GitCommands.LeftPanel.LeftPanelSection.Branches)
            || NeedsRefs(GitCommands.LeftPanel.LeftPanelSection.Remotes)
            || NeedsRefs(GitCommands.LeftPanel.LeftPanelSection.Tags)
                ? await Task.Run(_session.GetRefPanel)
                : ((IReadOnlyList<GitCommands.LeftPanel.RefTreeNode>)[], [], []);
        IReadOnlyList<GitCommands.LeftPanel.StashTreeNode> stashes =
            NeedsRefs(GitCommands.LeftPanel.LeftPanelSection.Stashes) ? await Task.Run(_session.GetStashPanel) : [];
        IReadOnlyList<GitCommands.LeftPanel.WorktreeTreeNode> worktrees =
            NeedsRefs(GitCommands.LeftPanel.LeftPanelSection.Worktrees) ? await Task.Run(_session.GetWorktreePanel) : [];
        IReadOnlyList<GitCommands.LeftPanel.RefTreeNode> submodules =
            NeedsRefs(GitCommands.LeftPanel.LeftPanelSection.Submodules) ? await Task.Run(_session.GetSubmodulePanel) : [];

        GitCommands.LeftPanel.RefTreeNode Section(string name, GitCommands.LeftPanel.RefTreeNodeKind kind, IEnumerable<GitCommands.LeftPanel.RefTreeNode> children)
        {
            GitCommands.LeftPanel.RefTreeNode section = new() { Name = name, FullPath = "", Kind = kind };
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

        List<GitCommands.LeftPanel.RefTreeNode> sections = [];
        foreach (GitCommands.LeftPanel.LeftPanelSection section in visibleSections)
        {
            switch (section)
            {
                case GitCommands.LeftPanel.LeftPanelSection.Branches:
                    sections.Add(Section($"Branches ({branches.Count})", GitCommands.LeftPanel.RefTreeNodeKind.BranchesSection, branches));
                    break;

                case GitCommands.LeftPanel.LeftPanelSection.Remotes:
                    sections.Add(Section($"Remotes ({remotes.Count})", GitCommands.LeftPanel.RefTreeNodeKind.RemotesSection, remoteNodes));
                    break;

                case GitCommands.LeftPanel.LeftPanelSection.Tags:
                    sections.Add(Section($"Tags ({tags.Count})", GitCommands.LeftPanel.RefTreeNodeKind.TagsSection, tags));
                    break;

                case GitCommands.LeftPanel.LeftPanelSection.Stashes:
                    sections.Add(Section($"Stashes ({stashes.Count})", GitCommands.LeftPanel.RefTreeNodeKind.StashesSection, stashes.Select(stash => new GitCommands.LeftPanel.RefTreeNode
                    {
                        Name = stash.DisplayName,
                        FullPath = stash.FullPath,
                        ObjectId = stash.ObjectId,
                        Kind = GitCommands.LeftPanel.RefTreeNodeKind.Stash,
                    })));
                    break;

                case GitCommands.LeftPanel.LeftPanelSection.Worktrees:
                    sections.Add(Section($"Worktrees ({worktrees.Count})", GitCommands.LeftPanel.RefTreeNodeKind.WorktreesSection, worktrees.Select(worktree => new GitCommands.LeftPanel.RefTreeNode
                    {
                        Name = worktree.IsCurrent ? $"{worktree.DisplayPath} (current)" : worktree.DisplayPath,
                        FullPath = worktree.Worktree.Path,
                        IsCurrent = worktree.IsCurrent,
                        Kind = GitCommands.LeftPanel.RefTreeNodeKind.Worktree,
                    })));
                    break;

                case GitCommands.LeftPanel.LeftPanelSection.Submodules when submodules.Count > 0:
                    sections.Add(Section($"Submodules ({submodules.Count})", GitCommands.LeftPanel.RefTreeNodeKind.SubmodulesSection, submodules));
                    break;
            }
        }

        RefTree.ItemsSource = sections;
    }

    private void OnRefTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (RefTree.SelectedItems is { Count: 1 }
            && RefTree.SelectedItem is GitCommands.LeftPanel.RefTreeNode { ObjectId: ObjectId objectId })
        {
            LogControl.TryJumpTo(objectId);
        }
    }

    private async void OnRefTreeDoubleTapped(object? sender, global::Avalonia.Input.TappedEventArgs e)
    {
        switch (RefTree.SelectedItem)
        {
            case GitCommands.LeftPanel.RefTreeNode { Kind: GitCommands.LeftPanel.RefTreeNodeKind.LocalBranch, IsCurrent: false } branch:
                await CheckoutBranchInteractiveAsync(branch.FullPath);
                return;

            case GitCommands.LeftPanel.RefTreeNode { Kind: GitCommands.LeftPanel.RefTreeNodeKind.Stash } stash:
                await OpenStashManagerAsync(stash.FullPath);
                return;

            case GitCommands.LeftPanel.RefTreeNode { Kind: GitCommands.LeftPanel.RefTreeNodeKind.Worktree, IsCurrent: false } worktree
                when System.IO.Directory.Exists(worktree.FullPath):
                await SwitchRepositoryAsync(worktree.FullPath);
                return;

            case GitCommands.LeftPanel.RefTreeNode { Kind: GitCommands.LeftPanel.RefTreeNodeKind.Submodule } submodule:
                await SwitchRepositoryAsync(submodule.FullPath);
                return;
        }
    }

    /// <summary>Checkout with the dialog's local-changes choice (stash&reapply/merge/discard/leave), shared by the sidebar and the menu bar.</summary>
    internal async Task CheckoutBranchInteractiveAsync(string branchName)
    {
        // the checkout dialog's local-changes choice, backed by the portable policy
        GitCommands.LocalChangesAction localChanges = GitCommands.LocalChangesAction.DontChange;
        bool stashThenReapply = false;
        if (await _session.IsDirtyAsync())
        {
            int choice = await ConfirmDialog.ShowAsync(
                this,
                "Checkout branch",
                $"You have uncommitted changes. How should they be handled when checking out {branchName}?",
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
            await RunOperationAsync($"Checkout {branchName}", async () =>
            {
                (bool stashed, string stashOutput) = await _session.StashSaveAsync();
                if (!stashed)
                {
                    return (false, stashOutput);
                }

                (bool success, string output) = await _session.CheckoutBranchAsync(branchName);
                if (!success)
                {
                    return (false, output);
                }

                return await _session.StashPopAsync();
            });
            return;
        }

        await RunOperationAsync($"Checkout {branchName}", () => _session.CheckoutBranchAsync(branchName, localChanges));
    }

    private async void OnFetchClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        => await RunOperationAsync("Fetch", _session.FetchAsync);

    private async void OnPullClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        => await PullFlowAsync();

    internal async Task PullFlowAsync()
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
        => await PushFlowAsync();

    internal async Task PushFlowAsync()
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

    internal async Task CreateBranchFlowAsync()
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

    internal async Task OpenSettingsAsync()
    {
        SettingsWindow settingsWindow = new(_session);
        await settingsWindow.ShowDialog(this);
        RebuildHotkeyMap();
        Loc.Reload();
        ApplyThemeVariant();
        ApplyToolbarTranslations();
        await LoadRefPanelAsync();
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

    /// <summary>The start page: the dashboard's repository groups over DashboardList, shown when no repository is open.</summary>
    private async Task ShowStartViewAsync()
    {
        Title = "Git Extensions";
        StartPanel.IsVisible = true;
        SetRepositoryMenusEnabled(false);
        await RefreshStartViewAsync();
    }

    private async Task RefreshStartViewAsync()
    {
        string pattern = StartSearchBox.Text ?? "";
        var recentHistory = await GitCommands.UserRepositoryHistory.RepositoryHistoryManager.Locals.LoadRecentHistoryAsync();
        var favouriteHistory = await GitCommands.UserRepositoryHistory.RepositoryHistoryManager.Locals.LoadFavouriteHistoryAsync();

        GitCommands.UserRepositoryHistory.RecentRepoSplitterOptions options =
            GitCommands.UserRepositoryHistory.RecentRepoSplitterOptions.FromAppSettings();
        var recent = GitCommands.Dashboard.DashboardList.SplitAndMerge(
            GitCommands.Dashboard.DashboardList.Filter(recentHistory, pattern), options);
        var favourites = GitCommands.Dashboard.DashboardList.SplitAndMerge(
            GitCommands.Dashboard.DashboardList.Filter(favouriteHistory, pattern), options);

        System.Collections.Generic.IReadOnlyList<string> categories = GitCommands.Dashboard.DashboardList.CategoryHeaders(recent, favourites);

        StartGroupsPanel.Children.Clear();
        foreach (GitCommands.Dashboard.DashboardRepositoryGroup group in GitCommands.Dashboard.DashboardList.Groups(recent, favourites))
        {
            if (group.Items.Count == 0)
            {
                continue;
            }

            TextBlock header = new()
            {
                Text = group.IsRecent ? Loc.T("Recent repositories") : group.Category,
                FontSize = 14,
                FontWeight = global::Avalonia.Media.FontWeight.SemiBold,
                Margin = new global::Avalonia.Thickness(0, 8, 0, 2),
            };
            AttachStartHeaderMenu(header, group);
            StartGroupsPanel.Children.Add(header);

            foreach (GitCommands.Dashboard.DashboardRepositoryItem item in group.Items)
            {
                Button repoButton = new()
                {
                    Content = (item.IsFavourite ? "⭐ " : "") + item.Caption,
                    HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
                    Background = global::Avalonia.Media.Brushes.Transparent,
                    BorderThickness = new global::Avalonia.Thickness(0),
                };
                ToolTip.SetTip(repoButton, item.Repo.Path);
                string path = item.Repo.Path;
                repoButton.Click += async (_, _) =>
                {
                    await OpenRecentAsync(path);
                    if (StartPanel.IsVisible)
                    {
                        await RefreshStartViewAsync();
                    }
                };
                AttachStartTileMenu(repoButton, item, categories);
                StartGroupsPanel.Children.Add(repoButton);
            }
        }
    }

    /// <summary>The dashboard tile menu: categorize (favourite), remove from the list, show in the file manager.</summary>
    private void AttachStartTileMenu(Button tile, GitCommands.Dashboard.DashboardRepositoryItem item, System.Collections.Generic.IReadOnlyList<string> categories)
    {
        tile.ContextRequested += (_, e) =>
        {
            e.Handled = true;
            ContextMenu menu = new();

            MenuItem categoryRoot = new() { Header = Loc.T("Category") };
            MenuItem none = new() { Header = Loc.T("(none)"), IsEnabled = !string.IsNullOrWhiteSpace(item.Repo.Category) };
            none.Click += async (_, _) => await AssignStartCategoryAsync(item.Repo, null);
            categoryRoot.Items.Add(none);
            foreach (string category in categories)
            {
                MenuItem categoryItem = new() { Header = category, IsEnabled = category != item.Repo.Category };
                string captured = category;
                categoryItem.Click += async (_, _) => await AssignStartCategoryAsync(item.Repo, captured);
                categoryRoot.Items.Add(categoryItem);
            }

            categoryRoot.Items.Add(new Separator());
            MenuItem addNew = new() { Header = Loc.T("Add new category...") };
            addNew.Click += async (_, _) =>
            {
                if (await PromptCategoryNameAsync(categories, originalName: null) is string name)
                {
                    await AssignStartCategoryAsync(item.Repo, name);
                }
            };
            categoryRoot.Items.Add(addNew);
            menu.Items.Add(categoryRoot);

            MenuItem remove = new() { Header = item.IsFavourite ? Loc.T("Remove from favourites") : Loc.T("Remove from recent list") };
            remove.Click += async (_, _) =>
            {
                if (item.IsFavourite)
                {
                    await GitCommands.UserRepositoryHistory.RepositoryHistoryManager.Locals.RemoveFavouriteAsync(item.Repo.Path);
                }
                else
                {
                    await GitCommands.UserRepositoryHistory.RepositoryHistoryManager.Locals.RemoveRecentAsync(item.Repo.Path);
                }

                await RefreshStartViewAsync();
            };
            menu.Items.Add(remove);

            MenuItem showInFolder = new() { Header = Loc.T("Show in file manager") };
            showInFolder.Click += (_, _) => GitCommands.OsShellUtil.OpenWithFileExplorer(item.Repo.Path);
            menu.Items.Add(showInFolder);

            menu.Open(tile);
        };
    }

    /// <summary>The group-header menu: rename/delete a category, clear the recent list.</summary>
    private void AttachStartHeaderMenu(TextBlock header, GitCommands.Dashboard.DashboardRepositoryGroup group)
    {
        header.ContextRequested += (_, e) =>
        {
            e.Handled = true;
            ContextMenu menu = new();

            if (group.IsRecent)
            {
                MenuItem clear = new() { Header = Loc.T("Clear recent repositories list...") };
                clear.Click += async (_, _) =>
                {
                    if (await ConfirmDialog.ConfirmAsync(this, Loc.T("Clear recent repositories"), Loc.T("Remove all entries from the recent repositories list?")))
                    {
                        await GitCommands.UserRepositoryHistory.RepositoryHistoryManager.Locals.SaveRecentHistoryAsync([]);
                        await RefreshStartViewAsync();
                    }
                };
                menu.Items.Add(clear);
            }
            else if (group.Category is string category)
            {
                MenuItem rename = new() { Header = Loc.T("Rename category...") };
                rename.Click += async (_, _) =>
                {
                    var favouriteHistory = await GitCommands.UserRepositoryHistory.RepositoryHistoryManager.Locals.LoadFavouriteHistoryAsync();
                    System.Collections.Generic.IReadOnlyList<string> others = [.. favouriteHistory
                        .Select(repo => repo.Category)
                        .Where(other => !string.IsNullOrWhiteSpace(other) && other != category)
                        .Cast<string>()
                        .Distinct()];
                    if (await PromptCategoryNameAsync(others, category) is string newName)
                    {
                        await GitCommands.Dashboard.CategoryCommands.RenameAsync(
                            GitCommands.UserRepositoryHistory.RepositoryHistoryManager.Locals, favouriteHistory, category, newName);
                        await RefreshStartViewAsync();
                    }
                };
                menu.Items.Add(rename);

                MenuItem delete = new() { Header = Loc.T("Delete category...") };
                delete.Click += async (_, _) =>
                {
                    if (await ConfirmDialog.ConfirmAsync(this, Loc.T("Delete category"),
                            string.Format(Loc.T("Delete the category '{0}'? Its repositories stay in the recent list."), category)))
                    {
                        var favouriteHistory = await GitCommands.UserRepositoryHistory.RepositoryHistoryManager.Locals.LoadFavouriteHistoryAsync();
                        await GitCommands.Dashboard.CategoryCommands.DeleteAsync(
                            GitCommands.UserRepositoryHistory.RepositoryHistoryManager.Locals, favouriteHistory, category);
                        await RefreshStartViewAsync();
                    }
                };
                menu.Items.Add(delete);
            }

            if (menu.Items.Count > 0)
            {
                menu.Open(header);
            }
        };
    }

    private async Task AssignStartCategoryAsync(GitCommands.UserRepositoryHistory.Repository repo, string? category)
    {
        await GitCommands.Dashboard.CategoryCommands.AssignAsync(GitCommands.UserRepositoryHistory.RepositoryHistoryManager.Locals, repo, category);
        await RefreshStartViewAsync();
    }

    /// <summary>Asks for a category name until it validates (the dashboard's name rules) or is cancelled.</summary>
    private async Task<string?> PromptCategoryNameAsync(System.Collections.Generic.IEnumerable<string> existingCategories, string? originalName)
    {
        while (true)
        {
            string? name = await ConfirmDialog.InputAsync(this, Loc.T("Category name"), Loc.T("Name:"), originalName ?? "");
            if (name is null)
            {
                return null;
            }

            switch (GitCommands.Dashboard.CategoryNameValidator.Validate(name, existingCategories))
            {
                case GitCommands.Dashboard.CategoryNameValidation.Ok:
                    return name;

                case GitCommands.Dashboard.CategoryNameValidation.Empty:
                    await ConfirmDialog.ErrorAsync(this, Loc.T("Category name"), Loc.T("Category name is required."));
                    break;

                case GitCommands.Dashboard.CategoryNameValidation.Duplicate:
                    await ConfirmDialog.ErrorAsync(this, Loc.T("Category name"), Loc.T("Category name already exists."));
                    break;
            }
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
            _ = ShowStartViewAsync();
            return;
        }

        StartPanel.IsVisible = false;
        SetRepositoryMenusEnabled(true);
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
        if (file is null || _selectedRevision is not GitRevision revision)
        {
            return;
        }

        e.Handled = true;
        GitCommands.Actions.FileMenuContext context = FileMenuContextFor(file, revision);
        Dictionary<string, Func<GitItemStatus, Task>> handlers = FileMenuHandlers(file, revision);

        ContextMenu menu = MainWindow.BuildMenu(
            GitCommands.Actions.FileMenuRegistry.FileMenuFor(context),
            action => handlers.ContainsKey(action.Id),
            action => GitCommands.Actions.FileMenuRegistry.IsApplicable(action, context),
            action => handlers[action.Id](file),
            GitCommands.Actions.FileMenuRegistry.FileSubmenuGroups);

        if (menu.Items.Count > 0)
        {
            menu.Open(FileTree);
        }
    }

    internal GitCommands.Actions.FileMenuContext FileMenuContextFor(GitItemStatus file, GitRevision revision)
        => new(
            SelectedCount: 1,
            AnyWorkTree: file.Staged is StagedStatus.WorkTree,
            AnyIndex: file.Staged is StagedStatus.Index,
            AnyTracked: file.IsTracked,
            AnySubmodule: file.IsSubmodule,
            AnyConflicted: file.IsUnmerged,
            IsArtificialRevision: revision.IsArtificial,
            IsBareRepository: _session.IsBareRepository,
            SelectionOnDisk: System.IO.File.Exists(System.IO.Path.Combine(_session.WorkingDir, file.Name)),
            IsDiffGridSurface: true);

    internal Dictionary<string, Func<GitItemStatus, Task>> FileMenuHandlers(GitItemStatus file, GitRevision revision)
    {
        string absolutePath = System.IO.Path.Combine(_session.WorkingDir, file.Name);
        return new()
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
            ["file.copyPath"] = status => CopyToClipboardAsync(System.IO.Path.Combine(_session.WorkingDir, status.Name)),
            ["file.copyRelativePath"] = status => CopyToClipboardAsync(status.Name),
            ["file.open"] = _ =>
            {
                GitCommands.OsShellUtil.Open(absolutePath);
                return Task.CompletedTask;
            },
            ["file.showInFolder"] = _ =>
            {
                // OsShellUtil.SelectPathInFileExplorer is explorer.exe-shaped; xdg-open on the
                // directory is the portable equivalent.
                GitCommands.OsShellUtil.Open(System.IO.Path.GetDirectoryName(absolutePath)!);
                return Task.CompletedTask;
            },
            ["file.saveAs"] = status => SaveFileAtRevisionAsAsync(status, revision),
            ["file.openTemp"] = status => OpenFileAtRevisionTempAsync(status, revision),
            ["file.openDifftool"] = status =>
            {
                if (!revision.HasParent)
                {
                    return ConfirmDialog.ErrorAsync(this, "Difftool", "The root commit has no parent to diff against.");
                }

                return _session.OpenFileDifftoolAsync(status.Name, status.OldName, revision.FirstParentId!.ToString(), revision.ObjectId.ToString());
            },
            ["submodule.open"] = status =>
            {
                string path = System.IO.Path.Combine(_session.WorkingDir, status.Name);
                if (System.IO.Directory.Exists(path))
                {
                    new MainWindow(path).Show();
                }

                return Task.CompletedTask;
            },
        };
    }

    private async Task SaveFileAtRevisionAsAsync(GitItemStatus file, GitRevision revision)
    {
        if (GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        byte[]? bytes = await _session.GetFileBytesAtRevisionAsync(file.Name, revision.ObjectId);
        if (bytes is null)
        {
            await ConfirmDialog.ErrorAsync(this, "Save as", $"{file.Name} does not exist at {revision.ObjectId.ToShortString()}.");
            return;
        }

        var picked = await storage.SaveFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Save file as",
            SuggestedFileName = System.IO.Path.GetFileName(file.Name),
        });
        if (picked?.TryGetLocalPath() is string path)
        {
            await System.IO.File.WriteAllBytesAsync(path, bytes);
        }
    }

    private async Task OpenFileAtRevisionTempAsync(GitItemStatus file, GitRevision revision)
    {
        byte[]? bytes = await _session.GetFileBytesAtRevisionAsync(file.Name, revision.ObjectId);
        if (bytes is null)
        {
            await ConfirmDialog.ErrorAsync(this, "Open revision", $"{file.Name} does not exist at {revision.ObjectId.ToShortString()}.");
            return;
        }

        string tempPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"{revision.ObjectId.ToShortString()}_{System.IO.Path.GetFileName(file.Name)}");
        await System.IO.File.WriteAllBytesAsync(tempPath, bytes);
        GitCommands.OsShellUtil.Open(tempPath);
    }

    internal async Task OpenRemotesAsync()
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
                _browseDiffText = diffText;
                _browseDiffFile = file;
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
            _browseDiffText = null;
            _browseDiffFile = null;
            DiffText.Text = ex.ToString();
        }
    }

    /// <summary>The browse diff pane's current unified diff text (inlines don't retain it) and its file.</summary>
    private string? _browseDiffText;
    private GitItemStatus? _browseDiffFile;

    /// <summary>Apply/revert the selected lines of a committed diff to the working tree (line-level cherry-pick).</summary>
    internal async Task RunCommittedLinePatchAsync(string actionId, int selectionStart, int selectionLength)
    {
        if (_browseDiffText is not string text)
        {
            return;
        }

        GitCommands.Patches.LinePatchVerb verb = actionId == "diff.revertLines"
            ? GitCommands.Patches.LinePatchVerb.Revert
            : GitCommands.Patches.LinePatchVerb.Apply;
        GitCommands.Patches.LinePatchPlan? plan = GitCommands.Patches.LinePatchPlanner.Plan(
            verb, text, selectionStart, selectionLength, _session.FilesEncoding,
            _browseDiffFile?.IsNew is true, _browseDiffFile?.IsRenamed is true);
        if (plan is null)
        {
            return;
        }

        string title = verb is GitCommands.Patches.LinePatchVerb.Revert ? "Revert selected lines" : "Apply selected lines";
        await RunOperationAsync(title, () => _session.ApplyLinePatchAsync(plan));
    }
}
