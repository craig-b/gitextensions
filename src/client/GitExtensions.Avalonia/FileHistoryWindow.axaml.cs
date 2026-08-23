using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using GitCommands;
using GitCommands.Blame;
using GitCommands.FileHistory;
using GitExtensions.Avalonia.Rendering;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;

namespace GitExtensions.Avalonia;

/// <summary>
///  The file-history window, kept in the WinForms shape per craig's direction: the file's
///  own filtered log on top (with the portable --follow workaround), and the Commit info /
///  Diff / View / Blame tabs below - all decisions from GitCommands.FileHistory and
///  GitCommands.Blame.
/// </summary>
public partial class FileHistoryWindow : Window
{
    private readonly SliceSession _session;
    private readonly string _fileName;
    private readonly CancellationTokenSource _logCts = new();
    private IReadOnlyDictionary<ObjectId, string> _fileByCommit = new Dictionary<ObjectId, string>();
    private GitRevision? _selectedRevision;
    private CancellationTokenSource? _selectionCts;

    public FileHistoryWindow(SliceSession session, string fileName, bool showBlame = false)
    {
        InitializeComponent();

        _session = session;
        _fileName = FileHistoryStartup.NormalizeFileName(fileName);
        Title = $"File History - {_fileName} - {_session.WorkingDir}";

        Tabs.SelectedItem = FileHistoryStartup.InitialTab(blameTabExists: true, showBlame) is FileHistoryTab.Blame ? BlameTab : DiffTab;

        LogControl.RevisionSelected += (_, revision) =>
        {
            _selectedRevision = revision;
            _ = ShowSelectionAsync(revision);
        };

        DiffPaneMenu.Attach(
            DiffText,
            () => _diffTabText,
            getPatchTarget: () => GitCommands.Actions.DiffLineTarget.Committed,
            runPatchVerb: RunCommittedLinePatchAsync);
        DiffViewBar viewBar = new(_session);
        viewBar.OptionsChanged += (_, _) =>
        {
            if (_selectedRevision is GitRevision revision)
            {
                _ = ShowSelectionAsync(revision);
            }
        };
        viewBar.AttachFind(DiffText, DiffScroll, () => _diffTabText);
        DiffViewBarHost.Content = viewBar;
        KeyDown += (_, keyArgs) =>
        {
            if (keyArgs.Key == global::Avalonia.Input.Key.F
                && keyArgs.KeyModifiers == global::Avalonia.Input.KeyModifiers.Control
                && Tabs.SelectedItem == DiffTab)
            {
                keyArgs.Handled = true;
                viewBar.FocusFind();
            }
        };
        BlameGutter.ContextRequested += OnBlameContextRequested;
        BlameBody.ContextRequested += OnBlameContextRequested;

        Loaded += (_, _) => StartStream();
        Closed += (_, _) => _logCts.Cancel();
    }

    /// <summary>The blame gutter menu: verbs on the commit of the clicked line (selected by the press).</summary>
    private void OnBlameContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_blame is null || _selectedBlameLine is not GitBlameLine line)
        {
            return;
        }

        e.Handled = true;
        Dictionary<string, Func<Task>> handlers = new()
        {
            ["blame.blameThis"] = () => BlameAtAsync(line.Commit.ObjectId, line.Commit.FileName, line.OriginLineNumber),
            ["blame.blamePrevious"] = BlamePreviousAsync,
            ["blame.showChanges"] = () =>
            {
                if (LogControl.TryJumpTo(line.Commit.ObjectId))
                {
                    Tabs.SelectedItem = DiffTab;
                }

                return Task.CompletedTask;
            },
            ["blame.copyHash"] = () => CopyToClipboardAsync(line.Commit.ObjectId.ToString()),
        };

        bool hasParent = BlamePreviousButton.IsEnabled;
        ContextMenu menu = MainWindow.BuildMenu(
            GitCommands.Actions.DiffMenuRegistry.BlameGutterActions,
            action => handlers.ContainsKey(action.Id),
            action => action.Id != "blame.blamePrevious" || hasParent,
            action => handlers[action.Id]());

        if (menu.Items.Count > 0)
        {
            menu.Open(sender as Control);
        }
    }

    /// <summary>Blames the file at a specific commit, through the grid jump (the pending-target pattern).</summary>
    private async Task BlameAtAsync(ObjectId commitId, string fileName, int line)
    {
        _pendingBlame = (commitId, fileName, line);
        if (!LogControl.TryJumpTo(commitId))
        {
            _pendingBlame = null;
            await RenderBlameAsync(commitId, fileName, line, CancellationToken.None);
        }
    }

    private async Task CopyToClipboardAsync(string text)
    {
        if (GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    /// <summary>Apply/revert the selected lines of the viewed commit's diff to the working tree.</summary>
    private async Task RunCommittedLinePatchAsync(string actionId, int selectionStart, int selectionLength)
    {
        if (_diffTabText is not string text)
        {
            return;
        }

        GitCommands.Patches.LinePatchVerb verb = actionId == "diff.revertLines"
            ? GitCommands.Patches.LinePatchVerb.Revert
            : GitCommands.Patches.LinePatchVerb.Apply;
        GitCommands.Patches.LinePatchPlan? plan = GitCommands.Patches.LinePatchPlanner.Plan(
            verb, text, selectionStart, selectionLength, _session.FilesEncoding);
        if (plan is null)
        {
            return;
        }

        (bool success, string output) = await _session.ApplyLinePatchAsync(plan);
        if (!success)
        {
            await ConfirmDialog.ErrorAsync(this, "Line patch failed", output);
        }
    }

    private void StartStream()
    {
        CancellationToken cancellationToken = _logCts.Token;
        _ = Task.Run(() =>
        {
            (string pathFilter, _fileByCommit) = _session.BuildFileHistoryFilter(_fileName);

            // Must precede Add: relativity (lane coloring) propagates from the checked-out node.
            LogControl.Graph.HeadId = _session.CurrentCheckout;

            _session.StreamFileLog(
                pathFilter,
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
                    LogControl.Graph.LoadingCompleted();
                    _ = LogControl.EnsureCachedToAsync(LogControl.Graph.Count - 1);
                    Dispatcher.UIThread.Post(() =>
                    {
                        LogControl.NotifyRowsChanged();
                        Title = $"File History - {_fileName} - {_session.WorkingDir} ({LogControl.Count:n0} commits)";
                        LogControl.SelectRow(0);
                    });
                },
                onError: exception => Dispatcher.UIThread.Post(() => CommitBody.Text = exception.Message),
                cancellationToken);
        }, cancellationToken);
    }

    /// <summary>The historical per-revision name: artificial rows map to the checkout, then the follow cache.</summary>
    private string ResolveFileName(GitRevision revision)
    {
        ObjectId lookupId = revision.IsArtificial ? _session.CurrentCheckout : revision.ObjectId;
        return _fileByCommit.TryGetValue(lookupId, out string? name) ? name : _fileName;
    }

    private void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_selectedRevision is GitRevision revision)
        {
            _ = ShowSelectionAsync(revision);
        }
    }

    private async Task ShowSelectionAsync(GitRevision revision)
    {
        _selectionCts?.Cancel();
        _selectionCts = new CancellationTokenSource();
        CancellationToken cancellationToken = _selectionCts.Token;

        string fileName = ResolveFileName(revision);
        Title = $"File History - {FileHistoryStartup.BuildTitle(_fileName, fileName)} - {_session.WorkingDir}";

        try
        {
            bool fileExists = await Task.Run(() => _session.FileExistsAtRevision(fileName, revision), cancellationToken);
            FileHistoryTabDecision tabs = FileHistoryTabDecision.Resolve(
                revision.IsArtificial, isFolder: fileName.EndsWith('/'), fileExists, blameSupported: true);

            CommitInfoTab.IsEnabled = tabs.ShowCommitInfo;
            DiffTab.IsEnabled = tabs.ShowDiff;
            ViewTab.IsEnabled = tabs.ShowView;
            BlameTab.IsEnabled = tabs.ShowBlame;

            if (Tabs.SelectedItem is TabItem { IsEnabled: false })
            {
                Tabs.SelectedItem = tabs.PreferredTab is FileHistoryTab.CommitInfo ? CommitInfoTab : DiffTab;
                return; // the tab change re-enters
            }

            if (Tabs.SelectedItem == CommitInfoTab)
            {
                var (header, body) = await Task.Run(() => _session.GetCommitInfo(revision), cancellationToken);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    CommitHeader.Inlines!.Clear();
                    CommitHeader.Inlines.AddRange(InlineRendering.ToInlines(header));
                    CommitBody.Inlines!.Clear();
                    CommitBody.Inlines.AddRange(InlineRendering.ToInlines(body));
                });
            }
            else if (Tabs.SelectedItem == DiffTab)
            {
                GitItemStatus file = new(fileName) { IsTracked = true };
                ObjectId? firstId = revision.HasParent ? revision.FirstParentId : null;
                var (diffText, spans, lineNumbers) = await Task.Run(() => _session.GetRevisionFileDiff(firstId, revision.ObjectId, file), cancellationToken);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _diffTabText = diffText;
                    DiffText.Inlines!.Clear();
                    DiffText.Inlines.AddRange(InlineRendering.ToInlines(diffText, spans));
                    DiffGutter.Text = LineNumberGutter.Build(diffText, lineNumbers);
                });
            }
            else if (Tabs.SelectedItem == ViewTab)
            {
                string? text = await Task.Run(() => _session.GetFileTextAtRevision(fileName, revision.IsArtificial ? _session.CurrentCheckout : revision.ObjectId), cancellationToken);
                await Dispatcher.UIThread.InvokeAsync(() => ViewText.Text = text ?? "");
            }
            else if (Tabs.SelectedItem == BlameTab)
            {
                // A blame-previous drill-down carries its own file name and target line
                // through the grid jump (the WinForms _clickedBlameLine pattern).
                if (_pendingBlame is { } pending && pending.CommitId == revision.ObjectId)
                {
                    _pendingBlame = null;
                    await RenderBlameAsync(revision.ObjectId, pending.FileName, pending.Line, cancellationToken);
                }
                else
                {
                    await RenderBlameAsync(revision.ObjectId, fileName, targetLine: null, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CommitBody.Text = ex.Message;
        }
    }

    private GitBlame? _blame;
    private string? _blameFileName;
    private GitBlameLine? _selectedBlameLine;
    private (ObjectId CommitId, string FileName, int Line)? _pendingBlame;

    /// <summary>The diff tab's current unified diff text (inlines don't retain it).</summary>
    private string? _diffTabText;

    // ColorBrewer Greens (the same ramp BlameControl uses), light-theme leaning.
    private static readonly global::Avalonia.Media.IBrush[] AgeBucketBrushes =
        [.. new[] { 0xF7FCF5u, 0xC7E9C0u, 0xA1D99Bu, 0x74C476u, 0x41AB5Du, 0x238B45u, 0x00441Bu }
            .Select(rgb => (global::Avalonia.Media.IBrush)new global::Avalonia.Media.SolidColorBrush(
                global::Avalonia.Media.Color.FromArgb(0x60, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)))];

    /// <summary>Renders a blame (grid selection or blame-previous drill-down) with the age-gradient gutter.</summary>
    private async Task RenderBlameAsync(ObjectId objectId, string fileName, int? targetLine, CancellationToken cancellationToken)
    {
        (GitBlame blame, BlameContents contents, IReadOnlyList<int> buckets) = await Task.Run(
            () =>
            {
                GitBlame blame = _session.GetBlame(fileName, objectId, cancellationToken);
                BlameContents contents = BlameGutterModel.Build(blame, fileName, CurrentBlameDisplayOptions(), CultureInfo.CurrentCulture);
                return (blame, contents, BlameAgeBuckets.Compute(blame.Lines, DateTime.Now));
            },
            cancellationToken);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _blame = blame;
            _blameFileName = fileName;
            _selectedBlameLine = null;
            BlameInfo.Text = "";
            BlamePreviousButton.IsEnabled = false;

            BlameGutter.Inlines!.Clear();
            bool showLineNumbers = AppSettings.BlameShowLineNumbers;
            string[] gutterLines = contents.Gutter.Split('\n');
            for (int i = 0; i < blame.Lines.Count && i < gutterLines.Length; i++)
            {
                string lineNumberPrefix = showLineNumbers ? $"{i + 1,5} " : "";
                BlameGutter.Inlines.Add(new global::Avalonia.Controls.Documents.Run(lineNumberPrefix + gutterLines[i].TrimEnd('\r') + "\n")
                {
                    Background = AgeBucketBrushes[buckets[i]],
                });
            }

            BlameBody.Text = contents.Body;

            if (targetLine is int line)
            {
                // Positioning must wait for the text layout; one background dispatch suffices.
                Dispatcher.UIThread.Post(() =>
                {
                    int clamped = BlameTargetLine.Clamp(line, blame.Lines.Count);
                    double lineHeight = BlameBody.TextLayout.Height / Math.Max(1, blame.Lines.Count);
                    BlameScroll.Offset = new global::Avalonia.Vector(0, Math.Max(0, (clamped - 3) * lineHeight));
                    SelectBlameLine(clamped - 1);
                }, DispatcherPriority.Background);
            }
        });
    }

    /// <summary>Click in either pane selects the line's commit (the WinForms SelectedLineChanged).</summary>
    private void OnBlamePanePressed(object? sender, global::Avalonia.Input.PointerPressedEventArgs e)
    {
        if (_blame is null || sender is not SelectableTextBlock pane || string.IsNullOrEmpty(pane.Text))
        {
            return;
        }

        double lineHeight = pane.TextLayout.Height / Math.Max(1, _blame.Lines.Count);
        int line = (int)(e.GetPosition(pane).Y / lineHeight);
        SelectBlameLine(line);
    }

    private void SelectBlameLine(int lineIndex)
    {
        if (_blame is null || lineIndex < 0 || lineIndex >= _blame.Lines.Count)
        {
            return;
        }

        GitBlameLine line = _blame.Lines[lineIndex];
        _selectedBlameLine = line;
        BlameInfo.Text = $"{line.Commit.ObjectId.ToShortString()} - {line.Commit.Author} - {line.Commit.AuthorTime:d} - {line.Commit.Summary}";

        GitRevision revision = _session.GetRevision(line.Commit.ObjectId);
        BlamePreviousButton.IsEnabled = revision.HasParent;
    }

    /// <summary>The blame-previous drill-down: map the line through the commit's diff, blame the parent.</summary>
    private async void OnBlamePreviousClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        => await BlamePreviousAsync();

    internal async Task BlamePreviousAsync()
    {
        if (_blame is null || _selectedBlameLine is not GitBlameLine line)
        {
            return;
        }

        try
        {
            GitRevision revision = await Task.Run(() => _session.GetRevision(line.Commit.ObjectId));
            if (!revision.HasParent)
            {
                return;
            }

            string fileName = line.Commit.FileName;
            int originalLine = await Task.Run(() => _session.GetOriginalLineInPreviousCommit(revision, fileName, line.OriginLineNumber));

            // A successful grid jump re-enters the blame render with the pending target;
            // outside the filtered history, render directly.
            _pendingBlame = (revision.FirstParentId, fileName, originalLine);
            if (!LogControl.TryJumpTo(revision.FirstParentId))
            {
                _pendingBlame = null;
                await RenderBlameAsync(revision.FirstParentId, fileName, originalLine, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            BlameInfo.Text = ex.Message;
        }
    }

    /// <summary>Verification harness (GE_SPIKE_FILEHISTORYTEST=&lt;file&gt;): stream, select, walk the tabs, report.</summary>
    internal async Task RunHarnessAsync()
    {
        await Task.Delay(4000);
        Console.Error.WriteLine($"[fh] {_fileName}: {LogControl.Count} commits");
        LogControl.SelectRow(0);
        await Task.Delay(500);

        foreach (TabItem tab in new[] { DiffTab, ViewTab, BlameTab, CommitInfoTab })
        {
            Tabs.SelectedItem = tab;
            await Task.Delay(1200);
        }

        Console.Error.WriteLine($"[fh] diff: {DiffText.Inlines?.Count ?? 0} inlines | view: {ViewText.Text?.Length ?? 0} chars | blame: {BlameBody.Text?.Split('\n').Length ?? 0} lines, gutter {BlameGutter.Inlines?.Count ?? 0} runs");

        Tabs.SelectedItem = BlameTab;
        await Task.Delay(800);
        SelectBlameLine(10);
        Console.Error.WriteLine($"[fh] blame line 10: {BlameInfo.Text} | previous enabled: {BlamePreviousButton.IsEnabled}");
        if (BlamePreviousButton.IsEnabled)
        {
            await BlamePreviousAsync();
            await Task.Delay(2500);
            SelectBlameLine(10);
            Console.Error.WriteLine($"[fh] after blame-previous: {BlameInfo.Text} | lines: {_blame?.Lines.Count}");
        }

        Environment.Exit(0);
    }

    private static BlameDisplayOptions CurrentBlameDisplayOptions() => new(
        ShowAuthor: AppSettings.BlameShowAuthor,
        ShowAuthorDate: AppSettings.BlameShowAuthorDate,
        ShowAuthorTime: AppSettings.BlameShowAuthorTime,
        DisplayAuthorFirst: AppSettings.BlameDisplayAuthorFirst,
        ShowOriginalFilePath: AppSettings.BlameShowOriginalFilePath);
}
