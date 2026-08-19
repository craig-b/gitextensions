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
    private readonly SliceSession _session;
    private CancellationTokenSource? _selectionCts;
    private CancellationTokenSource _logCts = new();

    public MainWindow(string repositoryPath)
    {
        InitializeComponent();

        _session = new SliceSession(repositoryPath);
        Title = $"Git Extensions - {_session.WorkingDir}";

        LogControl.RevisionSelected += (_, revision) => _ = ShowRevisionAsync(revision);

        Loaded += (_, _) => StartLogStream();
        Closed += (_, _) => _logCts.Cancel();

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

    /// <summary>
    ///  Restarts the log stream after history changed (a commit): stop the reader, let any
    ///  in-flight batch drain, park the lane pump, clear, stream again.
    /// </summary>
    private async Task ReloadLogAsync()
    {
        _logCts.Cancel();
        _logCts = new CancellationTokenSource();
        await Task.Delay(200);
        await LogControl.ResetAsync();
        StartLogStream();
    }

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

                            if (Environment.GetEnvironmentVariable("GE_SPIKE_BENCH") == "1")
                            {
                                _ = RunScrollBenchmarkAsync();
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
