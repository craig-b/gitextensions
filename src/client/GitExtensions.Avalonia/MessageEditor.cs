using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using GitCommands.Commit;

namespace GitExtensions.Avalonia;

/// <summary>
///  The commit screen's message editor: a monospace TextBox over a highlight layer that paints
///  the formatter's ranges (over-limit text). Monospace + NoWrap keeps the geometry honest -
///  a range at (line, offset, length) is a rectangle at (offset * charWidth, line * lineHeight),
///  shifted by the TextBox's scroll offset. Grows a real editing control (and spell check)
///  later; the ICommitMessageDocument seam stays the same.
/// </summary>
public sealed class MessageEditor : Grid
{
    private readonly HighlightLayer _layer;
    private readonly TextBox _textBox;
    private ScrollViewer? _scrollViewer;

    public MessageEditor()
    {
        _layer = new HighlightLayer();
        _textBox = new TextBox
        {
            AcceptsReturn = true,
            FontFamily = new FontFamily("monospace"),
            FontSize = 13,
            TextWrapping = TextWrapping.NoWrap,
            Background = Brushes.Transparent,
        };

        Children.Add(_layer);
        Children.Add(_textBox);

        _textBox.TextChanged += (sender, e) => TextChanged?.Invoke(this, e);
        _textBox.TemplateApplied += (_, _) =>
        {
            _scrollViewer = _textBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (_scrollViewer is not null)
            {
                _scrollViewer.ScrollChanged += (_, _) => SyncLayer();
            }

            SyncLayer();
        };
    }

    public event EventHandler<TextChangedEventArgs>? TextChanged;

    public string? Text
    {
        get => _textBox.Text;
        set => _textBox.Text = value;
    }

    public string? Watermark
    {
        get => _textBox.Watermark;
        set => _textBox.Watermark = value;
    }

    public int CaretIndex
    {
        get => _textBox.CaretIndex;
        set => _textBox.CaretIndex = value;
    }

    public void SetLineHighlight(int line, int offset, int length, CommitMessageHighlight highlight)
    {
        _layer.SetLineHighlight(line, offset, length, highlight);
        SyncLayer();
    }

    public void TrimHighlightsTo(int lineCount)
    {
        _layer.TrimTo(lineCount);
        SyncLayer();
    }

    private void SyncLayer()
    {
        _layer.Metrics = MeasureMetrics();
        _layer.ScrollOffset = _scrollViewer?.Offset ?? default;
        _layer.Origin = new Point(_textBox.Padding.Left + _textBox.BorderThickness.Left, _textBox.Padding.Top + _textBox.BorderThickness.Top);
        _layer.InvalidateVisual();
    }

    private (double CharWidth, double LineHeight) MeasureMetrics()
    {
        FormattedText probe = new("M", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(_textBox.FontFamily), _textBox.FontSize, Brushes.Black);
        return (probe.WidthIncludingTrailingWhitespace, probe.Height);
    }

    private sealed class HighlightLayer : Control
    {
        private static readonly IBrush _overlimitBrush = new SolidColorBrush(AvColorFromArgb(0x55, 0xE5, 0x39, 0x35));

        private readonly Dictionary<int, List<(int Offset, int Length)>> _overlimitByLine = [];

        public (double CharWidth, double LineHeight) Metrics { get; set; }

        public Vector ScrollOffset { get; set; }

        public Point Origin { get; set; }

        private static Color AvColorFromArgb(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);

        public void SetLineHighlight(int line, int offset, int length, CommitMessageHighlight highlight)
        {
            if (length <= 0)
            {
                return;
            }

            List<(int Offset, int Length)> runs = _overlimitByLine.TryGetValue(line, out List<(int, int)>? existing) ? existing : [];

            // Any assignment recolors the range: clip stored over-limit runs against it first.
            int start = offset;
            int end = offset + length;
            List<(int Offset, int Length)> clipped = [];
            foreach ((int runStart, int runLength) in runs)
            {
                int runEnd = runStart + runLength;
                if (runStart < start && runEnd > start)
                {
                    clipped.Add((runStart, start - runStart));
                }

                if (runStart < end && runEnd > end)
                {
                    clipped.Add((end, runEnd - end));
                }

                if (runEnd <= start || runStart >= end)
                {
                    clipped.Add((runStart, runLength));
                }
            }

            if (highlight == CommitMessageHighlight.Overlimit)
            {
                clipped.Add((offset, length));
            }

            if (clipped.Count == 0)
            {
                _overlimitByLine.Remove(line);
            }
            else
            {
                _overlimitByLine[line] = clipped;
            }
        }

        public void TrimTo(int lineCount)
        {
            foreach (int line in _overlimitByLine.Keys.Where(line => line >= lineCount).ToList())
            {
                _overlimitByLine.Remove(line);
            }
        }

        public override void Render(DrawingContext context)
        {
            (double charWidth, double lineHeight) = Metrics;
            if (charWidth <= 0)
            {
                return;
            }

            foreach ((int line, List<(int Offset, int Length)> runs) in _overlimitByLine)
            {
                double y = Origin.Y + (line * lineHeight) - ScrollOffset.Y;
                if (y + lineHeight < 0 || y > Bounds.Height)
                {
                    continue;
                }

                foreach ((int offset, int length) in runs)
                {
                    context.FillRectangle(
                        _overlimitBrush,
                        new Rect(Origin.X + (offset * charWidth) - ScrollOffset.X, y, length * charWidth, lineHeight));
                }
            }
        }
    }
}
