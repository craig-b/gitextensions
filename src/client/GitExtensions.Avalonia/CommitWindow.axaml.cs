using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using GitCommands;
using GitCommands.Commit;
using GitExtensions.Avalonia.Rendering;
using GitExtensions.Extensibility.Git;

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

    public CommitWindow(SliceSession session)
    {
        InitializeComponent();

        _session = session;
        _messageFormatter = new CommitMessageFormatter(new TextBoxCommitMessageDocument(MessageBox));
        MessageBox.TextChanged += OnMessageTextChanged;

        Loaded += async (_, _) =>
        {
            ShowBranchInfo();
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
                DiffText.Inlines!.Clear();
                DiffText.Inlines.AddRange(InlineRendering.ToInlines(text, spans));
                DiffGutter.Text = LineNumberGutter.Build(text, lineNumbers);
            });
        }
        catch (System.Exception ex)
        {
            DiffText.Text = ex.ToString();
        }
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
