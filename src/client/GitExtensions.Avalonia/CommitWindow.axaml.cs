using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using GitCommands;
using GitCommands.Commit;
using GitExtensions.Avalonia.Rendering;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;

namespace GitExtensions.Avalonia;

/// <summary>
///  The client's commit screen - the first client screen bound to the portable models the WinForms
///  dialog now shares: the work-tree status partition, StatusTreeSorter trees, the
///  CommitMessageFormatter document seam, CommitMessageValidator, CommitDialogGates, and
///  BranchPushTarget. All view code here is Avalonia glue.
/// </summary>
public partial class CommitWindow : Window
{
    private readonly SliceSession _session;
    private readonly CommitMessageFormatter _messageFormatter;
    private bool _formattingMessage;
    private CancellationTokenSource? _diffCts;

    public bool Committed { get; private set; }

    public CommitWindow()
    {
        // XAML previewer only.
        InitializeComponent();
        _session = null!;
        _messageFormatter = null!;
    }

    public CommitWindow(SliceSession session, string? initialMessage = null)
    {
        InitializeComponent();

        _session = session;
        _messageFormatter = new CommitMessageFormatter(new TextBoxCommitMessageDocument(MessageBox));
        MessageBox.TextChanged += OnMessageTextChanged;
        ConventionalPrefixCombo.ItemsSource = GitCommands.Commit.ConventionalCommitMessage.HeaderCommitTypes;
        UnstagedTree.ContextRequested += (_, e) => OnFileListContextRequested(UnstagedTree, staged: false, e);
        StagedTree.ContextRequested += (_, e) => OnFileListContextRequested(StagedTree, staged: true, e);
        DiffPaneMenu.Attach(DiffText, () => _diffPaneText, isCommitWindow: true, addSelectionToCommitMessage: AppendToCommitMessage);

        Loaded += async (_, _) =>
        {
            ShowBranchInfo();
            if (initialMessage is not null)
            {
                MessageBox.Text = initialMessage;
            }

            await ReloadStatusAsync();
        };
    }

    private void OnMessageTextChanged(object? sender, TextChangedEventArgs e)
    {
        // The formatter's own edits re-raise TextChanged; the guard mirrors the WinForms
        // IsUndoInProgress bypass.
        if (_formattingMessage)
        {
            return;
        }

        _formattingMessage = true;
        try
        {
            _messageFormatter.FormatAllText(0, CommitMessageFormattingRules.FromSettings());
        }
        finally
        {
            _formattingMessage = false;
        }
    }

    /// <summary>Applies (or replaces) the Conventional Commits type prefix on the message's first line.</summary>
    private void OnConventionalPrefixSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (ConventionalPrefixCombo.SelectedItem is not string keyword)
        {
            return;
        }

        string text = MessageBox.Text ?? "";
        int newline = text.IndexOf('\n');
        string firstLine = (newline < 0 ? text : text[..newline]).TrimEnd('\r');

        (string newFirstLine, int caret) = GitCommands.Commit.ConventionalCommitMessage.PrefixOrReplaceKeyword(
            keyword, text, firstLine, MessageBox.CaretIndex, insertScopeParentheses: false);

        MessageBox.Text = newline < 0 ? newFirstLine : newFirstLine + text[newline..];
        MessageBox.CaretIndex = Math.Min(caret, MessageBox.Text?.Length ?? 0);
        ConventionalPrefixCombo.SelectedIndex = -1;
        MessageBox.Focus();
    }

    private void ShowBranchInfo()
    {
        BranchPushTarget pushTarget = _session.PushTarget;
        string pushTo = pushTarget.Kind switch
        {
            PushTargetKind.Tracked => pushTarget.Target!,
            PushTargetKind.DefaultRemoteUntracked => $"{pushTarget.Target} (untracked)",
            _ => "(remote not configured)",
        };

        BranchInfo.Text = $"{_session.SelectedBranch} → {pushTo}";
    }

    private async Task ReloadStatusAsync()
    {
        (IReadOnlyList<GitItemStatus> unstaged, IReadOnlyList<GitItemStatus> staged) =
            await Task.Run(() => _session.GetWorkTreeStatus(CancellationToken.None));

        UnstagedTree.ItemsSource = StatusNode.BuildTree(unstaged).Children;
        StagedTree.ItemsSource = StatusNode.BuildTree(staged).Children;
        StatusSummary.Text = $"{unstaged.Count} unstaged, {staged.Count} staged";
    }

    private static IReadOnlyList<GitItemStatus> SelectedStatuses(TreeView tree)
        => [.. tree.SelectedItems!.OfType<StatusNode>().SelectMany(node => node.DescendantStatuses()).Distinct()];

    /// <summary>
    ///  Both status lists' menus project from the registry's file-status surface — the commit
    ///  window's first context menus. Conflicted selections prepend the resolve group.
    /// </summary>
    private void OnFileListContextRequested(TreeView tree, bool staged, ContextRequestedEventArgs e)
    {
        IReadOnlyList<GitItemStatus> selected = SelectedStatuses(tree);
        if (selected.Count == 0)
        {
            return;
        }

        e.Handled = true;
        string workingDir = _session.WorkingDir;
        GitCommands.Actions.FileMenuContext context = new(
            SelectedCount: selected.Count,
            AnyWorkTree: !staged,
            AnyIndex: staged,
            AnyTracked: selected.Any(status => status.IsTracked),
            AnySubmodule: selected.Any(status => status.IsSubmodule),
            AnyConflicted: selected.Any(status => status.IsUnmerged),
            IsArtificialRevision: true,
            SelectionOnDisk: selected.All(status => System.IO.File.Exists(AbsolutePath(status))));

        Dictionary<string, Func<Task>> handlers = new()
        {
            ["file.stage"] = () => StageAsync(selected),
            ["file.unstage"] = () => UnstageAsync(selected),
            ["file.history"] = () => ShowHistory(showBlame: false),
            ["file.blame"] = () => ShowHistory(showBlame: true),
            ["file.copyPath"] = () => CopyToClipboardAsync(string.Join("\n", selected.Select(AbsolutePath))),
            ["file.copyRelativePath"] = () => CopyToClipboardAsync(string.Join("\n", selected.Select(status => status.Name))),
            ["file.open"] = () =>
            {
                OsShellUtil.Open(AbsolutePath(selected[0]));
                return Task.CompletedTask;
            },
            ["file.showInFolder"] = () =>
            {
                OsShellUtil.Open(System.IO.Path.GetDirectoryName(AbsolutePath(selected[0]))!);
                return Task.CompletedTask;
            },
            ["file.resetChanges"] = async () =>
            {
                int newFiles = selected.Count(status => status.IsNew);
                string question = newFiles > 0
                    ? $"Reset changes to {selected.Count} file(s)?\n{newFiles} new file(s) will be DELETED."
                    : $"Reset changes to {selected.Count} file(s)?";
                if (!await ConfirmDialog.ConfirmAsync(this, "Reset changes", question))
                {
                    return;
                }

                (bool success, string output) = await _session.ResetFileChangesAsync(selected, toHead: staged, resetAndDelete: newFiles > 0);
                if (!success)
                {
                    await ConfirmDialog.ErrorAsync(this, "Reset changes", output);
                }

                await ReloadStatusAsync();
            },
            ["file.openDifftool"] = () => _session.OpenFileDifftoolAsync(
                selected[0].Name,
                selected[0].OldName,
                staged ? "HEAD" : GitRevision.IndexGuid,
                staged ? GitRevision.IndexGuid : GitRevision.WorkTreeGuid),
            ["file.rename"] = async () =>
            {
                string oldName = selected[0].Name;
                string? newName = await ConfirmDialog.InputAsync(this, "Rename / move", $"New name for {oldName}:", oldName);
                if (string.IsNullOrWhiteSpace(newName) || newName == oldName)
                {
                    return;
                }

                (bool success, string output) = await _session.RenameFileAsync(isFolder: false, oldName, newName.Trim());
                if (!success)
                {
                    await ConfirmDialog.ErrorAsync(this, "Rename", output);
                }

                await ReloadStatusAsync();
            },
            ["file.delete"] = async () =>
            {
                if (!await ConfirmDialog.ConfirmAsync(this, "Delete", $"Delete {selected.Count} file(s) from disk?\nThis cannot be undone."))
                {
                    return;
                }

                if (staged)
                {
                    await Task.Run(() => _session.UnstageFiles(selected));
                }

                foreach (GitItemStatus status in selected.Where(status => !status.IsSubmodule))
                {
                    string path = AbsolutePath(status);
                    try
                    {
                        if (System.IO.File.Exists(path))
                        {
                            System.IO.File.Delete(path);
                        }
                        else if (System.IO.Directory.Exists(path))
                        {
                            System.IO.Directory.Delete(path, recursive: true);
                        }
                    }
                    catch (System.IO.IOException)
                    {
                        // In-use files stay; the reload shows what survived.
                    }
                }

                await ReloadStatusAsync();
            },
            ["file.gitignore"] = () => AddToIgnoreAsync(localExclude: false),
            ["file.gitignoreLocal"] = () => AddToIgnoreAsync(localExclude: true),
            ["file.skipWorktree"] = async () =>
            {
                await _session.SetSkipWorktreeAsync(selected, skipWorktree: true);
                await ReloadStatusAsync();
            },
            ["file.assumeUnchanged"] = async () =>
            {
                await _session.SetAssumeUnchangedAsync(selected, assumeUnchanged: true);
                await ReloadStatusAsync();
            },
            ["file.stopTracking"] = async () =>
            {
                (bool success, string output) = await _session.StopTrackingFileAsync(selected[0].Name);
                if (!success)
                {
                    await ConfirmDialog.ErrorAsync(this, "Stop tracking", output);
                }

                await ReloadStatusAsync();
            },
            ["submodule.open"] = () =>
            {
                new MainWindow(AbsolutePath(FirstSubmodule())).Show();
                return Task.CompletedTask;
            },
            ["submodule.update"] = async () =>
            {
                await _session.UpdateSubmoduleAsync(FirstSubmodule().Name);
                await ReloadStatusAsync();
            },
            ["submodule.reset"] = async () =>
            {
                GitItemStatus submodule = FirstSubmodule();
                if (await ConfirmDialog.ConfirmAsync(this, "Reset submodule", $"Reset ALL changes in {submodule.Name}? Untracked files are kept."))
                {
                    await new SliceSession(AbsolutePath(submodule)).ResetAllChangesAsync(clean: false);
                    await ReloadStatusAsync();
                }
            },
            ["submodule.stash"] = async () =>
            {
                await new SliceSession(AbsolutePath(FirstSubmodule())).StashSaveAsync();
                await ReloadStatusAsync();
            },
            ["submodule.commit"] = async () =>
            {
                CommitWindow submoduleCommit = new(new SliceSession(AbsolutePath(FirstSubmodule())));
                await submoduleCommit.ShowDialog(this);
                await ReloadStatusAsync();
            },
            ["conflict.ours"] = () => ResolveConflictsAsync(selected, GitCommands.Conflicts.ConflictSide.Local),
            ["conflict.theirs"] = () => ResolveConflictsAsync(selected, GitCommands.Conflicts.ConflictSide.Remote),
            ["conflict.openWindow"] = async () =>
            {
                await new ConflictsWindow(_session).ShowDialog(this);
                await ReloadStatusAsync();
            },
            ["conflict.markResolved"] = async () =>
            {
                foreach (GitItemStatus status in selected.Where(status => status.IsUnmerged))
                {
                    await _session.StageConflictedFileAsync(status.Name);
                }

                await ReloadStatusAsync();
            },
            ["conflict.delete"] = async () =>
            {
                List<GitItemStatus> conflicted = [.. selected.Where(status => status.IsUnmerged)];
                if (await ConfirmDialog.ConfirmAsync(this, "Delete", $"Delete {conflicted.Count} conflicted file(s) from the working tree?"))
                {
                    foreach (GitItemStatus status in conflicted)
                    {
                        await _session.RemoveConflictedFileAsync(status.Name);
                    }

                    await ReloadStatusAsync();
                }
            },
        };

        ContextMenu menu = MainWindow.BuildMenu(
            GitCommands.Actions.FileMenuRegistry.FileMenuFor(context),
            action => handlers.ContainsKey(action.Id),
            action => GitCommands.Actions.FileMenuRegistry.IsApplicable(action, context),
            action => handlers[action.Id](),
            GitCommands.Actions.FileMenuRegistry.FileSubmenuGroups);

        if (menu.Items.Count > 0)
        {
            menu.Open(tree);
        }

        string AbsolutePath(GitItemStatus status) => System.IO.Path.Combine(workingDir, status.Name);

        GitItemStatus FirstSubmodule() => selected.First(status => status.IsSubmodule);

        Task ShowHistory(bool showBlame)
        {
            new FileHistoryWindow(_session, selected[0].Name, showBlame).Show(this);
            return Task.CompletedTask;
        }

        async Task AddToIgnoreAsync(bool localExclude)
        {
            (bool success, string output) = await _session.AddToGitIgnoreAsync([.. selected.Select(status => status.Name)], localExclude);
            if (!success)
            {
                await ConfirmDialog.ErrorAsync(this, "Ignore", output);
            }

            await ReloadStatusAsync();
        }
    }

    private async Task ResolveConflictsAsync(IReadOnlyList<GitItemStatus> selected, GitCommands.Conflicts.ConflictSide side)
    {
        // ResolveConflictSideAsync already stages the resolution (checkout-index + add).
        foreach (GitItemStatus status in selected.Where(status => status.IsUnmerged))
        {
            await _session.ResolveConflictSideAsync(status.Name, side);
        }

        await ReloadStatusAsync();
    }

    private async Task CopyToClipboardAsync(string text)
    {
        if (GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private static IReadOnlyList<GitItemStatus> AllStatuses(TreeView tree)
        => [.. (tree.ItemsSource?.OfType<StatusNode>() ?? []).SelectMany(node => node.DescendantStatuses())];

    private void OnUnstagedSelectionChanged(object? sender, SelectionChangedEventArgs e)
        => ShowFileDiff(UnstagedTree, staged: false);

    private void OnStagedSelectionChanged(object? sender, SelectionChangedEventArgs e)
        => ShowFileDiff(StagedTree, staged: true);

    private void ShowFileDiff(TreeView tree, bool staged)
    {
        GitItemStatus? file = tree.SelectedItems!.OfType<StatusNode>().FirstOrDefault()?.Status;
        if (file is null)
        {
            return;
        }

        _diffCts?.Cancel();
        _diffCts = new CancellationTokenSource();
        CancellationToken cancellationToken = _diffCts.Token;

        _ = ShowFileDiffAsync(file, staged, cancellationToken);
    }

    private async Task ShowFileDiffAsync(GitItemStatus file, bool staged, CancellationToken cancellationToken)
    {
        try
        {
            var (text, spans, lineNumbers) = await Task.Run(() => _session.GetWorkTreeFileDiff(file, staged), cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _diffPaneText = text;
                DiffText.Inlines!.Clear();
                DiffText.Inlines.AddRange(InlineRendering.ToInlines(text, spans));
                DiffGutter.Text = LineNumberGutter.Build(text, lineNumbers);
            });
        }
        catch (System.Exception ex)
        {
            _diffPaneText = null;
            DiffText.Text = ex.ToString();
        }
    }

    /// <summary>The diff pane's current unified diff text (inlines don't retain it).</summary>
    private string? _diffPaneText;

    /// <summary>"Add selection to commit message": the stripped selection lands on its own line at the end.</summary>
    private void AppendToCommitMessage(string text)
    {
        string current = MessageBox.Text ?? "";
        MessageBox.Text = current.Length == 0 || current.EndsWith('\n')
            ? current + text
            : current + "\n" + text;
    }

    private async void OnStageClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        => await StageAsync(SelectedStatuses(UnstagedTree));

    private async void OnStageAllClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        => await StageAsync(AllStatuses(UnstagedTree));

    private async void OnUnstageClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        => await UnstageAsync(SelectedStatuses(StagedTree));

    private async void OnUnstageAllClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        => await UnstageAsync(AllStatuses(StagedTree));

    private async Task StageAsync(IReadOnlyList<GitItemStatus> files)
    {
        if (files.Count == 0)
        {
            return;
        }

        (bool success, string output) = await Task.Run(() => _session.StageFiles(files));
        if (!success)
        {
            await ConfirmDialog.ErrorAsync(this, "Stage failed", output);
        }

        await ReloadStatusAsync();
    }

    private async Task UnstageAsync(IReadOnlyList<GitItemStatus> files)
    {
        if (files.Count == 0)
        {
            return;
        }

        await Task.Run(() => _session.UnstageFiles(files));
        await ReloadStatusAsync();
    }

    private async void OnCommitClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        => await ExecuteCommitAsync();

    private async Task ExecuteCommitAsync()
    {
        bool amend = AmendCheckBox.IsChecked == true;
        bool stagedIsEmpty = AllStatuses(StagedTree).Count == 0;
        bool allowEmpty = false;

        // Stage phase: the same gate walk as the WinForms dialog.
        foreach (CommitDialogGate gate in CommitDialogGates.EvaluateStagePhase(
            amend,
            dontConfirmAmend: AppSettings.DontConfirmAmend,
            stagedIsEmpty,
            isMergeCommit: _session.IsMergeCommitPending))
        {
            switch (gate)
            {
                case CommitDialogGate.ConfirmAmend:
                    if (!await ConfirmDialog.ConfirmAsync(this, "Amend commit", "You are about to rewrite the last commit. Continue?"))
                    {
                        return;
                    }

                    break;

                case CommitDialogGate.ConfirmEmptyMergeCommit:
                    if (!await ConfirmDialog.ConfirmAsync(this, "No staged changes", "There are no staged changes. Commit an empty changeset to conclude the merge?"))
                    {
                        return;
                    }

                    allowEmpty = true;
                    break;

                case CommitDialogGate.ResolveNoStagedChanges:
                    int choice = await ConfirmDialog.ShowAsync(this, "No staged changes", "There are no files staged for this commit.",
                        "Stage all changes and commit", "Make an empty commit", "Cancel");
                    if (choice is 2 or -1)
                    {
                        return;
                    }

                    if (choice == 0)
                    {
                        await StageAsync(AllStatuses(UnstagedTree));
                        if (AllStatuses(StagedTree).Count == 0)
                        {
                            return;
                        }
                    }
                    else
                    {
                        allowEmpty = true;
                    }

                    break;
            }
        }

        // Commit phase.
        string message = MessageBox.Text ?? "";
        foreach (CommitDialogGate gate in CommitDialogGates.EvaluateCommitPhase(
            inConflictedMerge: _session.InConflictedMerge,
            useFormCommitMessage: true,
            messageIsEmptyOrTemplate: string.IsNullOrEmpty(message),
            dontConfirmCommitIfNoBranch: AppSettings.DontConfirmCommitIfNoBranch,
            isDetachedHead: _session.IsDetachedHead,
            inRebase: _session.InRebase))
        {
            switch (gate)
            {
                case CommitDialogGate.BlockConflictedMerge:
                    await ConfirmDialog.ErrorAsync(this, "Merge conflicts", "There are unresolved merge conflicts; solve them before committing.");
                    return;

                case CommitDialogGate.BlockEmptyMessage:
                    await ConfirmDialog.ErrorAsync(this, "Commit message", "Please enter a commit message.");
                    return;

                case CommitDialogGate.ValidateMessage:
                    foreach (CommitMessageViolation violation in CommitMessageValidator.Validate(message, CommitMessageValidationRules.FromSettings()))
                    {
                        string warning = violation.Kind switch
                        {
                            CommitMessageViolationKind.FirstLineTooLong => "First line of commit message contains too many characters.\nDo you want to continue?",
                            CommitMessageViolationKind.LineTooLong => $"The following line of commit message contains too many characters:\n\n{violation.OffendingLine}\n\nDo you want to continue?",
                            CommitMessageViolationKind.SecondLineNotEmpty => "Second line of commit message is not empty.\nDo you want to continue?",
                            _ => "Commit message does not match RegEx.\nDo you want to continue?",
                        };

                        if (!await ConfirmDialog.ConfirmAsync(this, "Commit validation", warning))
                        {
                            return;
                        }
                    }

                    break;

                case CommitDialogGate.ConfirmDetachedHead:
                    if (!await ConfirmDialog.ConfirmAsync(this, "Not on a branch", "HEAD is detached: the commit will not be on any branch. Continue?"))
                    {
                        return;
                    }

                    break;
            }
        }

        CommitButton.IsEnabled = false;
        try
        {
            (bool success, string output) = await _session.CommitAsync(
                message,
                amend,
                resetAuthor: amend && ResetAuthorCheckBox.IsChecked == true,
                allowEmpty);

            ResultOutput.Text = output.Trim();

            if (success)
            {
                Committed = true;
                MessageBox.Text = "";
                AmendCheckBox.IsChecked = false;
                await ReloadStatusAsync();
            }
            else
            {
                await ConfirmDialog.ErrorAsync(this, "Commit failed", output);
            }
        }
        finally
        {
            CommitButton.IsEnabled = true;
        }
    }

    /// <summary>
    ///  Verification harness (GE_SPIKE_COMMITTEST): show the loaded screen, snapshot it, stage
    ///  everything, commit with a fixed message, snapshot the result, close.
    /// </summary>
    internal async Task RunHarnessAsync(string snapshotDirectory)
    {
        await ReloadStatusAsync();

        StatusNode? firstLeaf = (UnstagedTree.ItemsSource?.OfType<StatusNode>() ?? [])
            .SelectMany(Leaves)
            .FirstOrDefault();
        if (firstLeaf is not null)
        {
            UnstagedTree.SelectedItems!.Add(firstLeaf);
        }

        await Task.Delay(1200);
        Snapshot(System.IO.Path.Combine(snapshotDirectory, "commit_before.png"));

        await StageAsync(AllStatuses(UnstagedTree));

        // Exercise the formatter's over-limit highlighting with a temporarily tight rule.
        int savedLimit = AppSettings.CommitValidationMaxCntCharsFirstLine;
        AppSettings.CommitValidationMaxCntCharsFirstLine = 30;
        MessageBox.Text = "a subject line that runs well past the thirty character limit";
        await Task.Delay(400);
        Snapshot(System.IO.Path.Combine(snapshotDirectory, "commit_highlight.png"));
        AppSettings.CommitValidationMaxCntCharsFirstLine = savedLimit;

        MessageBox.Text = "feat: change committed from the Avalonia client";
        await Task.Delay(200);
        await ExecuteCommitAsync();
        await Task.Delay(200);
        Snapshot(System.IO.Path.Combine(snapshotDirectory, "commit_after.png"));

        Close();

        static IEnumerable<StatusNode> Leaves(StatusNode node)
            => node.Status is not null ? [node] : node.Children.SelectMany(Leaves);

        void Snapshot(string path)
        {
            global::Avalonia.PixelSize size = new((int)Bounds.Width, (int)Bounds.Height);
            using global::Avalonia.Media.Imaging.RenderTargetBitmap bitmap = new(size);
            bitmap.Render(this);
            bitmap.Save(path);
        }
    }
}
