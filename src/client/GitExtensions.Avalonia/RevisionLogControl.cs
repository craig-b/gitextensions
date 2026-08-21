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
    private readonly SortedSet<int> _selectedIndexes = [];
    private readonly List<(Rect Bounds, IGitRef Ref)> _chipHitRects = [];

    private readonly Typeface _typeface = new("monospace");
    private readonly Typeface _textTypeface = Typeface.Default;

    // Theme-variant palette; FormattedText bakes its brush, so the text cache clears on change.
    private sealed record Palette(IBrush Background, IBrush Selection, IBrush Text, IBrush DimText);

    private static readonly Palette _lightPalette = new(
        Brushes.White,
        new SolidColorBrush(AvColor.FromRgb(0xd6, 0xe6, 0xf7)),
        Brushes.Black,
        Brushes.DimGray);

    private static readonly Palette _darkPalette = new(
        new SolidColorBrush(AvColor.FromRgb(0x1e, 0x1e, 0x1e)),
        new SolidColorBrush(AvColor.FromRgb(0x26, 0x4f, 0x78)),
        new SolidColorBrush(AvColor.FromRgb(0xf0, 0xf0, 0xf0)),
        new SolidColorBrush(AvColor.FromRgb(0x9d, 0x9d, 0x9d)));

    private Palette CurrentPalette => ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Dark ? _darkPalette : _lightPalette;


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
    private bool _resetting;
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
            if (_resetting)
            {
                // A render's kick must not restart the pump between "pump parked" and
                // "graph cleared" in ResetAsync - the reset re-kicks nothing; the first
                // frame after it kicks again.
                return _cacheTask;
            }

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
            _resetting = true;
            _cacheTarget = -1;
            pump = _cacheTask;
        }

        try
        {
            await pump;

            _graph.Clear();
            _selectedIndex = -1;
            _offset = default;
            _textCache.Clear();
        }
        finally
        {
            lock (_cacheLock)
            {
                _resetting = false;
                _cacheTarget = -1;
            }
        }

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

    public RevisionLogControl()
    {
        Focusable = true;
        ActualThemeVariantChanged += (_, _) =>
        {
            _textCache.Clear();
            InvalidateVisual();
        };
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        int row = (int)((_offset.Y + e.GetPosition(this).Y) / RowHeight);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ToggleRowSelection(row);
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _selectedIndex >= 0)
        {
            SelectRange(_selectedIndex, row);
        }
        else
        {
            SelectRow(row);
        }

        base.OnPointerPressed(e);
    }

    /// <summary>Ctrl+click: toggle a row in the multi-selection; the clicked row becomes primary.</summary>
    private void ToggleRowSelection(int row)
    {
        if (row < 0 || row >= _graph.Count)
        {
            return;
        }

        if (!_selectedIndexes.Remove(row))
        {
            _selectedIndexes.Add(row);
        }

        _selectedIndex = row;
        InvalidateVisual();
        if (_graph.GetNodeForRow(row)?.GitRevision is { } revision)
        {
            RevisionSelected?.Invoke(this, revision);
        }
    }

    /// <summary>Shift+click: select the anchor..row range; the clicked row becomes primary.</summary>
    private void SelectRange(int anchor, int row)
    {
        if (row < 0 || row >= _graph.Count)
        {
            return;
        }

        _selectedIndexes.Clear();
        for (int i = Math.Min(anchor, row); i <= Math.Max(anchor, row); i++)
        {
            _selectedIndexes.Add(i);
        }

        _selectedIndex = row;
        InvalidateVisual();
        if (_graph.GetNodeForRow(row)?.GitRevision is { } revision)
        {
            RevisionSelected?.Invoke(this, revision);
        }
    }

    /// <summary>The ref chip under a viewport point, for chip context menus.</summary>
    public IGitRef? HitTestRefChip(Point point)
    {
        foreach ((Rect bounds, IGitRef gitRef) in _chipHitRects)
        {
            if (bounds.Contains(point))
            {
                return gitRef;
            }
        }

        return null;
    }

    /// <summary>The selected revisions in row order (primary selection included).</summary>
    public IReadOnlyList<GitRevision> SelectedRevisions
        => [.. _selectedIndexes
            .Where(row => row < _graph.Count)
            .Select(row => _graph.GetNodeForRow(row)?.GitRevision)
            .OfType<GitRevision>()];

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int rowsPerPage = Math.Max(1, (int)(_viewport.Height / RowHeight) - 1);
        int current = _selectedIndex < 0 ? FirstVisibleRow : _selectedIndex;

        int? target = e.Key switch
        {
            Key.Up => current - 1,
            Key.Down => current + 1,
            Key.PageUp => current - rowsPerPage,
            Key.PageDown => current + rowsPerPage,
            Key.Home => 0,
            Key.End => _graph.Count - 1,
            _ => null,
        };

        if (target is not int row || _graph.Count == 0)
        {
            base.OnKeyDown(e);
            return;
        }

        row = Math.Clamp(row, 0, _graph.Count - 1);
        SelectRow(row);
        EnsureRowVisible(row, rowsPerPage);
        e.Handled = true;
    }

    /// <summary>Selects and scrolls to the given revision if it is in the graph.</summary>
    public bool TryJumpTo(ObjectId objectId)
    {
        if (!_graph.TryGetRowIndex(objectId, out int row))
        {
            return false;
        }

        SelectRow(row);
        EnsureRowVisible(row, Math.Max(1, (int)(_viewport.Height / RowHeight) - 1));
        return true;
    }

    private void EnsureRowVisible(int row, int rowsPerPage)
    {
        int firstVisible = FirstVisibleRow;
        if (row < firstVisible)
        {
            ScrollToRow(row);
        }
        else if (row > firstVisible + rowsPerPage)
        {
            ScrollToRow(row - rowsPerPage);
        }
    }

    public void SelectRow(int row)
    {
        if (row < 0 || row >= _graph.Count)
        {
            return;
        }

        _selectedIndex = row;
        _selectedIndexes.Clear();
        _selectedIndexes.Add(row);
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

        Palette palette = CurrentPalette;
        context.FillRectangle(palette.Background, new Rect(Bounds.Size));
        _chipHitRects.Clear();

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

        // The WinForms grid contract: NEVER read rows at or beyond GetCachedCount() while the
        // build is in flight - the trailing straighten look-ahead window is mutated IN PLACE by
        // the pump (lane dictionaries included), so reading those rows from the render thread is
        // a data race, not just a stale frame. Rows beyond the boundary stay blank this frame;
        // the pump invalidates as the boundary advances.
        int renderableCount = _graph.GetCachedCount();
        int lastRenderableRow = Math.Min(lastRow, renderableCount - 1);

        // Graph width follows the widest visible row.
        int maxLanes = 1;
        for (int row = firstRow; row <= lastRenderableRow; row++)
        {
            maxLanes = Math.Max(maxLanes, _graph.GetSegmentsForRow(row)?.GetLaneCount() ?? 1);
        }

        maxLanes = Math.Min(maxLanes, MaxGraphLanes);
        double graphWidth = GraphPadding + (maxLanes * LaneWidth);

        for (int row = firstRow; row <= lastRow; row++)
        {
            double yTop = (row * RowHeight) - _offset.Y;
            double yMid = yTop + (RowHeight / 2);

            if (row == _selectedIndex || _selectedIndexes.Contains(row))
            {
                context.FillRectangle(palette.Selection, new Rect(0, yTop, Bounds.Width, RowHeight));
            }

            if (row > lastRenderableRow)
            {
                continue;
            }

            IRevisionGraphRow? graphRow = _graph.GetSegmentsForRow(row);
            if (graphRow is null)
            {
                continue;
            }

            IRevisionGraphRow? previousRow = row > 0 ? _graph.GetSegmentsForRow(row - 1) : null;
            IRevisionGraphRow? nextRow = row < lastRenderableRow ? _graph.GetSegmentsForRow(row + 1) : null;

            DrawGraphRow(context, graphRow, previousRow, nextRow, yTop, yMid);
            DrawTextColumns(context, graphRow, graphWidth, yMid, palette);
        }

        stopwatch.Stop();
        LastRenderMillis = stopwatch.Elapsed.TotalMilliseconds;

        TaskCompletionSource? renderTcs = _renderTcs;
        _renderTcs = null;
        renderTcs?.TrySetResult();

        if (Environment.GetEnvironmentVariable("GE_SPIKE_DEBUG") == "1")
        {
            Console.Error.WriteLine($"[render] count={count} renderable={renderableCount} first={firstRow} last={lastRow} viewport={_viewport} bounds={Bounds} row0null={firstRow > lastRenderableRow || _graph.GetSegmentsForRow(firstRow) is null} ms={LastRenderMillis:0.0}");
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

    private void DrawTextColumns(DrawingContext context, IRevisionGraphRow row, double graphWidth, double yMid, Palette palette)
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
        double subjectRight = textX + subjectWidth;

        // Ref chips (branch/tag labels) before the subject, taking at most half the subject room.
        double chipLimit = textX + (subjectWidth / 2);
        foreach (IGitRef gitRef in revision.Refs)
        {
            if (textX >= chipLimit)
            {
                break;
            }

            double chipAdvance = DrawRefChip(context, gitRef, textX, yMid, chipLimit - textX);
            _chipHitRects.Add((new Rect(textX, yMid - 9, chipAdvance, 18), gitRef));
            textX += chipAdvance;
        }

        subjectWidth = Math.Max(20, subjectRight - textX);
        DrawCachedText(context, 0, revision.Subject, _textTypeface, 13, palette.Text, new Point(textX, yMid), subjectWidth);
        DrawCachedText(context, 1, revision.Author ?? "", _textTypeface, 12, palette.DimText, new Point(textX + subjectWidth + 8, yMid), authorWidth);
        DrawCachedText(context, 2, revision.AuthorDate.ToString("yyyy-MM-dd HH:mm"), _typeface, 12, palette.DimText, new Point(textX + subjectWidth + 8 + authorWidth + 8, yMid), dateWidth);
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

    private sealed record ChipStyle(IBrush Background, IBrush Text);

    private static readonly ChipStyle _localBranchChipLight = new(new SolidColorBrush(AvColor.FromRgb(0xc8, 0xe6, 0xc9)), new SolidColorBrush(AvColor.FromRgb(0x1b, 0x5e, 0x20)));
    private static readonly ChipStyle _remoteBranchChipLight = new(new SolidColorBrush(AvColor.FromRgb(0xff, 0xe0, 0xb2)), new SolidColorBrush(AvColor.FromRgb(0x8a, 0x50, 0x00)));
    private static readonly ChipStyle _tagChipLight = new(new SolidColorBrush(AvColor.FromRgb(0xe1, 0xbe, 0xe7)), new SolidColorBrush(AvColor.FromRgb(0x6a, 0x1b, 0x9a)));
    private static readonly ChipStyle _localBranchChipDark = new(new SolidColorBrush(AvColor.FromRgb(0x1f, 0x45, 0x22)), new SolidColorBrush(AvColor.FromRgb(0xa5, 0xd6, 0xa7)));
    private static readonly ChipStyle _remoteBranchChipDark = new(new SolidColorBrush(AvColor.FromRgb(0x4e, 0x34, 0x0e)), new SolidColorBrush(AvColor.FromRgb(0xff, 0xcc, 0x80)));
    private static readonly ChipStyle _tagChipDark = new(new SolidColorBrush(AvColor.FromRgb(0x42, 0x1f, 0x4a)), new SolidColorBrush(AvColor.FromRgb(0xce, 0x93, 0xd8)));

    /// <returns>The width consumed, including trailing spacing.</returns>
    private double DrawRefChip(DrawingContext context, IGitRef gitRef, double x, double yMid, double maxWidth)
    {
        bool dark = ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Dark;
        ChipStyle style = gitRef.IsTag
            ? dark ? _tagChipDark : _tagChipLight
            : gitRef.IsRemote
                ? dark ? _remoteBranchChipDark : _remoteBranchChipLight
                : dark ? _localBranchChipDark : _localBranchChipLight;

        const double padding = 5;
        const double chipHeight = 16;

        (string, int, int) key = (gitRef.Name, dark ? 4 : 3, (int)maxWidth);
        if (!_textCache.TryGetValue(key, out FormattedText? formatted))
        {
            if (_textCache.Count > 6000)
            {
                _textCache.Clear();
            }

            formatted = new FormattedText(gitRef.Name, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, _textTypeface, 11, style.Text)
            {
                MaxTextWidth = Math.Max(10, maxWidth - (2 * padding)),
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            _textCache.Add(key, formatted);
        }

        double chipWidth = formatted.Width + (2 * padding);
        context.DrawRectangle(style.Background, null, new RoundedRect(new Rect(x, yMid - (chipHeight / 2), chipWidth, chipHeight), 3));
        context.DrawText(formatted, new Point(x + padding, yMid - (formatted.Height / 2)));

        return chipWidth + 4;
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
