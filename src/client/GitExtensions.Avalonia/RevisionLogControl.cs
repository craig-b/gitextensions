using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using GitUI.UserControls.RevisionGrid.Graph;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;
using AvColor = Avalonia.Media.Color;
using SdColor = System.Drawing.Color;

namespace GitExtensions.Avalonia;

/// <summary>
///  The spike's virtualized revision-log control (plan §19.2): renders the REAL portable
///  RevisionGraph model - the same IRevisionGraphRow lanes/segments the WinForms grid paints -
///  with Avalonia geometry. One control owns scrolling (ILogicalScrollable), and only the
///  visible band of rows is ever laid out or drawn, so row count does not affect frame cost.
/// </summary>
public sealed class RevisionLogControl : Control, ILogicalScrollable
{
    private const double RowHeight = 24;
    private const double LaneWidth = 13;
    private const double NodeRadius = 4;
    private const double GraphPadding = 8;
    private const int MaxGraphLanes = 24;

    private readonly RevisionGraph _graph = new();
    private Vector _offset;
    private Size _extent;
    private Size _viewport;
    private bool _canScroll;
    private int _selectedIndex = -1;

    private static readonly IBrush _selectionBrush = new SolidColorBrush(AvColor.FromRgb(0xd6, 0xe6, 0xf7));
    private readonly Typeface _typeface = new("monospace");
    private readonly Typeface _textTypeface = Typeface.Default;

    public event EventHandler<GitRevision>? RevisionSelected;

    private TaskCompletionSource? _renderTcs;

    /// <summary>
    ///  Completes after the next real Render pass - the bench must sample frame cost only
    ///  after an actual paint (a Render-priority dispatcher hop completes before the
    ///  compositor paints, sampling stale values).
    /// </summary>
    public Task NextRenderAsync()
    {
        _renderTcs ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return _renderTcs.Task;
    }

    public RevisionGraph Graph => _graph;

    public double LastRenderMillis { get; private set; }

    public int Count => _graph.Count;

    public void NotifyRowsChanged()
    {
        UpdateScrollInfo();
        InvalidateVisual();
    }

    // RevisionGraph.BuildOrderedRowCache mutates a List and is not re-entrant: the WinForms grid
    // funnels every CacheTo through ONE serialized background updater and never builds from the
    // paint path. Same rule here - Render only reads cached rows and requests more; this pump is
    // the single CacheTo caller.
    private readonly object _cacheLock = new();
    private int _cacheTarget = -1;
    private Task _cacheTask = Task.CompletedTask;
    private readonly CancellationTokenSource _shutdownCts = new();

    /// <summary>
    ///  Cheap request from the render path: raise the pump's target and start it if idle.
    ///  A kick that races a completing pump self-heals - the next frame kicks again.
    /// </summary>
    public void RequestCacheTo(int lastRow) => KickPump(lastRow);

    /// <summary>
    ///  Completes only when the lane layout genuinely reaches <paramref name="lastRow"/>
    ///  (clamped to the current count) - re-kicks the pump across any completion race.
    /// </summary>
    public async Task EnsureCachedToAsync(int lastRow)
    {
        while (!_shutdownCts.IsCancellationRequested)
        {
            Task pump = KickPump(lastRow);
            await pump.ConfigureAwait(false);
            if (_graph.GetCachedCount() > Math.Min(lastRow, _graph.Count - 1))
            {
                return;
            }

            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    private Task KickPump(int lastRow)
    {
        lock (_cacheLock)
        {
            _cacheTarget = Math.Max(_cacheTarget, lastRow);
            if (_cacheTask.IsCompleted)
            {
                _cacheTask = Task.Run(CachePump);
            }

            return _cacheTask;
        }
    }

    private void CachePump()
    {
        CancellationToken cancellationToken = _shutdownCts.Token;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int target;
                lock (_cacheLock)
                {
                    target = Math.Min(_cacheTarget, _graph.Count - 1);
                }

                int cached = _graph.GetCachedCount();
                if (cached > target)
                {
                    return;
                }

                _graph.CacheTo(cached, target, cancellationToken);

                Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Background);

                if (_graph.GetCachedCount() <= cached)
                {
                    // No progress: the look-ahead clamp is waiting for more revisions to stream
                    // in. The next batch invalidates, and Render re-requests.
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _shutdownCts.Cancel();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    ///  Empties the graph for a reload: parks the cache pump (the single writer) and waits for
    ///  it before clearing, so no lane build races the reset.
    /// </summary>
    public async Task ResetAsync()
    {
        Task pump;
        lock (_cacheLock)
        {
            _cacheTarget = -1;
            pump = _cacheTask;
        }

        await pump;

        _graph.Clear();
        _selectedIndex = -1;
        _offset = default;
        _textCache.Clear();
        NotifyRowsChanged();
    }

    public void ScrollToRow(int row)
    {
        double y = Math.Clamp(row * RowHeight, 0, Math.Max(0, _extent.Height - _viewport.Height));
        ((ILogicalScrollable)this).Offset = new Vector(0, y);
    }

    public int FirstVisibleRow => (int)(_offset.Y / RowHeight);

    private void UpdateScrollInfo()
    {
        _extent = new Size(_viewport.Width, _graph.Count * RowHeight);
        _scrollInvalidated?.Invoke(this, EventArgs.Empty);
    }

    protected override Size MeasureOverride(Size availableSize) => availableSize;

    protected override Size ArrangeOverride(Size finalSize)
    {
        _viewport = finalSize;
        UpdateScrollInfo();
        return base.ArrangeOverride(finalSize);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        SelectRow((int)((_offset.Y + e.GetPosition(this).Y) / RowHeight));
        base.OnPointerPressed(e);
    }

    public void SelectRow(int row)
    {
        if (row < 0 || row >= _graph.Count)
        {
            return;
        }

        _selectedIndex = row;
        InvalidateVisual();
        GitRevision? revision = _graph.GetNodeForRow(row)?.GitRevision;
        if (revision is not null)
        {
            RevisionSelected?.Invoke(this, revision);
        }
    }

    public override void Render(DrawingContext context)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        context.FillRectangle(Brushes.White, new Rect(Bounds.Size));

        int count = _graph.Count;
        if (count == 0)
        {
            return;
        }

        int firstRow = Math.Max(0, (int)(_offset.Y / RowHeight));
        int lastRow = Math.Min(count - 1, (int)((_offset.Y + _viewport.Height) / RowHeight) + 1);

        // Ask the pump for the visible band plus a page of look-ahead; this frame paints
        // whatever is already cached and the pump invalidates when more arrives.
        RequestCacheTo(Math.Min(count - 1, lastRow + 100));

        // Graph width follows the widest visible row.
        int maxLanes = 1;
        for (int row = firstRow; row <= lastRow; row++)
        {
            maxLanes = Math.Max(maxLanes, _graph.GetSegmentsForRow(row)?.GetLaneCount() ?? 1);
        }

        maxLanes = Math.Min(maxLanes, MaxGraphLanes);
        double graphWidth = GraphPadding + (maxLanes * LaneWidth);

        for (int row = firstRow; row <= lastRow; row++)
        {
            double yTop = (row * RowHeight) - _offset.Y;
            double yMid = yTop + (RowHeight / 2);

            if (row == _selectedIndex)
            {
                context.FillRectangle(_selectionBrush, new Rect(0, yTop, Bounds.Width, RowHeight));
            }

            IRevisionGraphRow? graphRow = _graph.GetSegmentsForRow(row);
            if (graphRow is null)
            {
                continue;
            }

            IRevisionGraphRow? previousRow = row > 0 ? _graph.GetSegmentsForRow(row - 1) : null;
            IRevisionGraphRow? nextRow = row < count - 1 ? _graph.GetSegmentsForRow(row + 1) : null;

            DrawGraphRow(context, graphRow, previousRow, nextRow, yTop, yMid);
            DrawTextColumns(context, graphRow, graphWidth, yMid);
        }

        stopwatch.Stop();
        LastRenderMillis = stopwatch.Elapsed.TotalMilliseconds;

        TaskCompletionSource? renderTcs = _renderTcs;
        _renderTcs = null;
        renderTcs?.TrySetResult();

        if (Environment.GetEnvironmentVariable("GE_SPIKE_DEBUG") == "1")
        {
            Console.Error.WriteLine($"[render] count={count} first={firstRow} last={lastRow} viewport={_viewport} bounds={Bounds} row0null={_graph.GetSegmentsForRow(firstRow) is null} ms={LastRenderMillis:0.0}");
        }
    }

    private void DrawGraphRow(DrawingContext context, IRevisionGraphRow row, IRevisionGraphRow? previousRow, IRevisionGraphRow? nextRow, double yTop, double yMid)
    {
        double yBottom = yTop + RowHeight;

        foreach (RevisionGraphSegment segment in row.Segments)
        {
            Pen pen = GetPenForSegment(segment);
            int centerLane = row.GetLaneForSegment(segment).Index;
            if (centerLane < 0 || centerLane > MaxGraphLanes)
            {
                continue;
            }

            double xCenter = LaneX(centerLane);

            // Upper half: connect from this segment's lane in the previous row (towards the child).
            if (previousRow is not null && segment.Child != row.Revision)
            {
                int startLane = previousRow.GetLaneForSegment(segment).Index;
                if (startLane >= 0)
                {
                    context.DrawLine(pen, new Point(LaneX(startLane), yTop - (RowHeight / 2)), new Point(xCenter, yMid));
                }
            }
            else if (segment.Child == row.Revision || previousRow is null)
            {
                // Segment starts at this revision's node.
                double xNode = LaneX(row.GetCurrentRevisionLane());
                if (segment.Child == row.Revision)
                {
                    context.DrawLine(pen, new Point(xNode, yMid), new Point(xCenter, yMid));
                }
            }

            // Lower half: connect towards this segment's lane in the next row (towards the parent).
            if (segment.Parent == row.Revision)
            {
                // Ends at this revision's node.
                double xNode = LaneX(row.GetCurrentRevisionLane());
                context.DrawLine(pen, new Point(xCenter, yMid), new Point(xNode, yMid));
            }
            else if (nextRow is not null)
            {
                int endLane = nextRow.GetLaneForSegment(segment).Index;
                if (endLane >= 0)
                {
                    context.DrawLine(pen, new Point(xCenter, yMid), new Point(LaneX(endLane), yBottom + (RowHeight / 2)));
                }
            }
        }

        // The revision node itself.
        int nodeLane = row.GetCurrentRevisionLane();
        if (nodeLane >= 0 && nodeLane <= MaxGraphLanes)
        {
            RevisionGraphRevision revision = row.Revision;
            IBrush nodeBrush = ToBrush(GetColorForRevision(row, revision));
            Point center = new(LaneX(nodeLane), yMid);

            bool hasRefs = revision.GitRevision?.Refs.Count > 0;
            if (hasRefs)
            {
                context.FillRectangle(nodeBrush, new Rect(center.X - NodeRadius, center.Y - NodeRadius, NodeRadius * 2, NodeRadius * 2));
            }
            else
            {
                context.DrawEllipse(nodeBrush, null, center, NodeRadius, NodeRadius);
            }

            if (revision.Objectid == _graph.HeadId)
            {
                context.DrawEllipse(null, new Pen(nodeBrush, 2), center, NodeRadius + 2.5, NodeRadius + 2.5);
            }
        }
    }

    private void DrawTextColumns(DrawingContext context, IRevisionGraphRow row, double graphWidth, double yMid)
    {
        GitRevision? revision = row.Revision.GitRevision;
        if (revision is null)
        {
            return;
        }

        double textX = graphWidth + GraphPadding;
        double authorWidth = 170;
        double dateWidth = 120;
        double subjectWidth = Math.Max(50, Bounds.Width - textX - authorWidth - dateWidth - 24);

        DrawCachedText(context, 0, revision.Subject, _textTypeface, 13, Brushes.Black, new Point(textX, yMid), subjectWidth);
        DrawCachedText(context, 1, revision.Author ?? "", _textTypeface, 12, Brushes.DimGray, new Point(textX + subjectWidth + 8, yMid), authorWidth);
        DrawCachedText(context, 2, revision.AuthorDate.ToString("yyyy-MM-dd HH:mm"), _typeface, 12, Brushes.DimGray, new Point(textX + subjectWidth + 8 + authorWidth + 8, yMid), dateWidth);
    }

    // FormattedText construction (text shaping) dominates frame cost if done per frame; cache it
    // keyed by CONTENT, not commit - authors and dates repeat across rows, so those columns
    // become permanent hits and only unseen subjects shape per frame. Bounded: cleared wholesale
    // when it outgrows ~6k entries (a few hundred KB), i.e. after thousands of unseen subjects.
    private readonly Dictionary<(string Text, int Column, int Width), FormattedText> _textCache = [];

    private void DrawCachedText(DrawingContext context, int column, string text, Typeface typeface, double size, IBrush brush, Point position, double maxWidth)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        (string, int, int) key = (text, column, (int)maxWidth);
        if (!_textCache.TryGetValue(key, out FormattedText? formatted))
        {
            if (_textCache.Count > 6000)
            {
                _textCache.Clear();
            }

            formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, brush)
            {
                MaxTextWidth = maxWidth,
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            _textCache.Add(key, formatted);
        }

        context.DrawText(formatted, position - new Vector(0, formatted.Height / 2));
    }

    private static double LaneX(int lane) => GraphPadding + ((lane + 0.5) * LaneWidth);

    private static Pen GetPenForSegment(RevisionGraphSegment segment)
    {
        SdColor color = segment.Child.IsRelative
            ? segment.LaneInfo is LaneInfo laneInfo ? RevisionGraphLaneColor.GetColorForIndex(laneInfo.Color) : RevisionGraphLaneColor.NonRelativeColor
            : RevisionGraphLaneColor.NonRelativeColor;
        return PenCache.Get(color);
    }

    private static SdColor GetColorForRevision(IRevisionGraphRow row, RevisionGraphRevision revision)
    {
        if (!revision.IsRelative)
        {
            return RevisionGraphLaneColor.NonRelativeColor;
        }

        RevisionGraphSegment? segment = row.Segments.FirstOrDefault(s => s.Child == revision || s.Parent == revision);
        return segment?.LaneInfo is LaneInfo laneInfo
            ? RevisionGraphLaneColor.GetColorForIndex(laneInfo.Color)
            : RevisionGraphLaneColor.PresetGraphColors[0];
    }

    private static IBrush ToBrush(SdColor color) => BrushCache.Get(color);

    private static class PenCache
    {
        private static readonly Dictionary<SdColor, Pen> _pens = [];

        public static Pen Get(SdColor color)
        {
            if (!_pens.TryGetValue(color, out Pen? pen))
            {
                pen = new Pen(BrushCache.Get(color), 2);
                _pens.Add(color, pen);
            }

            return pen;
        }
    }

    private static class BrushCache
    {
        private static readonly Dictionary<SdColor, IBrush> _brushes = [];

        public static IBrush Get(SdColor color)
        {
            if (!_brushes.TryGetValue(color, out IBrush? brush))
            {
                brush = new SolidColorBrush(AvColor.FromArgb(color.A, color.R, color.G, color.B));
                _brushes.Add(color, brush);
            }

            return brush;
        }
    }

    #region ILogicalScrollable

    private EventHandler? _scrollInvalidated;

    bool ILogicalScrollable.CanHorizontallyScroll { get => false; set { } }

    bool ILogicalScrollable.CanVerticallyScroll { get => _canScroll; set => _canScroll = value; }

    bool ILogicalScrollable.IsLogicalScrollEnabled => true;

    Size ILogicalScrollable.ScrollSize => new(0, RowHeight * 3);

    Size ILogicalScrollable.PageScrollSize => new(0, Math.Max(RowHeight, _viewport.Height - RowHeight));

    Size IScrollable.Extent => _extent;

    Vector IScrollable.Offset
    {
        get => _offset;
        set
        {
            _offset = new Vector(0, Math.Clamp(value.Y, 0, Math.Max(0, _extent.Height - _viewport.Height)));
            InvalidateVisual();
        }
    }

    Size IScrollable.Viewport => _viewport;

    event EventHandler? ILogicalScrollable.ScrollInvalidated
    {
        add => _scrollInvalidated += value;
        remove => _scrollInvalidated -= value;
    }

    bool ILogicalScrollable.BringIntoView(Control target, Rect targetRect) => false;

    Control? ILogicalScrollable.GetControlInDirection(NavigationDirection direction, Control? from) => null;

    void ILogicalScrollable.RaiseScrollInvalidated(EventArgs e) => _scrollInvalidated?.Invoke(this, e);

    #endregion
}
