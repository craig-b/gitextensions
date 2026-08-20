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

        Loaded += (_, _) => StartStream();
        Closed += (_, _) => _logCts.Cancel();
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
                BlameContents contents = await Task.Run(
                    () =>
                    {
                        GitBlame blame = _session.GetBlame(fileName, revision.ObjectId, cancellationToken);
                        return BlameGutterModel.Build(blame, fileName, CurrentBlameDisplayOptions(), CultureInfo.CurrentCulture);
                    },
                    cancellationToken);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    BlameGutter.Text = contents.Gutter;
                    BlameBody.Text = contents.Body;
                });
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

        Console.Error.WriteLine($"[fh] diff: {DiffText.Inlines?.Count ?? 0} inlines | view: {ViewText.Text?.Length ?? 0} chars | blame: {BlameBody.Text?.Split('\n').Length ?? 0} lines, gutter {BlameGutter.Text?.Split('\n').Length ?? 0} lines");
        string? firstGutter = BlameGutter.Text?.Split('\n').FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
        Console.Error.WriteLine($"[fh] first blame caption: {firstGutter?.TrimEnd()}");
        Environment.Exit(0);
    }

    private static BlameDisplayOptions CurrentBlameDisplayOptions() => new(
        ShowAuthor: AppSettings.BlameShowAuthor,
        ShowAuthorDate: AppSettings.BlameShowAuthorDate,
        ShowAuthorTime: AppSettings.BlameShowAuthorTime,
        DisplayAuthorFirst: AppSettings.BlameDisplayAuthorFirst,
        ShowOriginalFilePath: AppSettings.BlameShowOriginalFilePath);
}
