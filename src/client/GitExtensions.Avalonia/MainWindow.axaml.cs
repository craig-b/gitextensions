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
    private readonly CancellationTokenSource _logCts = new();

    public MainWindow(string repositoryPath)
    {
        InitializeComponent();

        _session = new SliceSession(repositoryPath);
        Title = $"Git Extensions - {_session.WorkingDir}";

        LogControl.RevisionSelected += (_, revision) => _ = ShowRevisionAsync(revision);

        Loaded += (_, _) => StartLogStream();
        Closed += (_, _) => _logCts.Cancel();
    }

    private void StartLogStream()
    {
        if (!_session.IsValidRepository)
        {
            CommitBody.Text = $"Not a git repository: {_session.WorkingDir}";
            return;
        }

        Stopwatch loadStopwatch = Stopwatch.StartNew();

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
                    _logCts.Token);
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

    /// <summary>
    ///  One left/right line-number pair per diff-view line, from the real (M4-portable)
    ///  DiffLineNumAnalyzer - the same data the WinForms FileViewer margin paints.
    /// </summary>
    private static string BuildLineNumberGutter(string diffText, DiffLinesInfo lineNumbers)
    {
        int lineCount = diffText.Length == 0 ? 0 : diffText.Count(c => c == '\n') + (diffText.EndsWith('\n') ? 0 : 1);
        System.Text.StringBuilder gutter = new();
        for (int line = 1; line <= lineCount; line++)
        {
            if (lineNumbers.DiffLines.TryGetValue(line, out DiffLineInfo? info))
            {
                string left = info.LeftLineNumber == DiffLineInfo.NotApplicableLineNum ? "" : info.LeftLineNumber.ToString();
                string right = info.RightLineNumber == DiffLineInfo.NotApplicableLineNum ? "" : info.RightLineNumber.ToString();
                gutter.Append($"{left,5} {right,5}");
            }

            gutter.Append('\n');
        }

        return gutter.ToString();
    }

    private async Task ShowRevisionAsync(GitRevision revision)
    {
        _selectionCts?.Cancel();
        _selectionCts = new CancellationTokenSource();
        CancellationToken cancellationToken = _selectionCts.Token;

        try
        {
            var (header, body) = await Task.Run(() => _session.GetCommitInfo(revision), cancellationToken);
            var (diffText, spans, lineNumbers) = await Task.Run(() => _session.GetDiff(revision), cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                CommitHeader.Inlines!.Clear();
                CommitHeader.Inlines.AddRange(InlineRendering.ToInlines(header));

                CommitBody.Inlines!.Clear();
                CommitBody.Inlines.AddRange(InlineRendering.ToInlines(body));

                DiffText.Inlines!.Clear();
                DiffText.Inlines.AddRange(InlineRendering.ToInlines(diffText, spans));

                DiffGutter.Text = BuildLineNumberGutter(diffText, lineNumbers);
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
}
