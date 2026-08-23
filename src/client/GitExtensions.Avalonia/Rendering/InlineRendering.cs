using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using GitCommands.RichText;
using GitUI.Editor.Diff;

namespace GitExtensions.Avalonia.Rendering;

/// <summary>
///  Renders the portable models into Avalonia inlines: the M6 <see cref="RichContent"/> segments
///  (commit info) and the M4 <see cref="StyledSpan"/> lists (diff highlighting). These are the
///  Avalonia counterparts of the WinForms adapters (SetRichContent and ApplyTextHighlighting) -
///  same models, different view, which is the whole point of the slice.
/// </summary>
internal static class InlineRendering
{
    private static readonly IBrush _linkBrush = new SolidColorBrush(Color.FromRgb(0x2b, 0x6c, 0xd4));

    public static IEnumerable<Inline> ToInlines(RichContent content, System.Action<string>? onLinkClick = null)
    {
        foreach (RichTextSegment segment in content.Segments)
        {
            if (segment.LinkTarget is string linkTarget && onLinkClick is not null)
            {
                // Runs receive no pointer events; a TextBlock in an InlineUIContainer does.
                TextBlock linkBlock = new()
                {
                    Text = segment.Text,
                    Foreground = _linkBrush,
                    TextDecorations = TextDecorations.Underline,
                    Cursor = new Cursor(StandardCursorType.Hand),
                };
                linkBlock.PointerReleased += (_, _) => onLinkClick(linkTarget);

                yield return new InlineUIContainer(linkBlock) { BaselineAlignment = BaselineAlignment.Baseline };
                continue;
            }

            Run run = new(segment.Text);

            if (segment.LinkTarget is not null)
            {
                run.Foreground = _linkBrush;
                run.TextDecorations = TextDecorations.Underline;
            }
            else if (segment.Style.HasFlag(RichTextStyle.Underline))
            {
                run.TextDecorations = TextDecorations.Underline;
            }

            yield return run;
        }
    }

    /// <summary>
    ///  Flattens possibly-overlapping styled spans (a line-wide background plus inline word
    ///  markers over the same range, exactly as the WinForms marker strategy layers them - later
    ///  spans win per property) into non-overlapping runs.
    /// </summary>
    public static IEnumerable<Inline> ToInlines(string text, IReadOnlyList<StyledSpan> spans)
    {
        if (text.Length == 0)
        {
            yield break;
        }

        List<int> boundaries = [0, text.Length];
        foreach (StyledSpan span in spans)
        {
            boundaries.Add(Math.Clamp(span.Offset, 0, text.Length));
            boundaries.Add(Math.Clamp(span.Offset + span.Length, 0, text.Length));
        }

        boundaries = [.. boundaries.Distinct().OrderBy(offset => offset)];

        for (int i = 0; i < boundaries.Count - 1; i++)
        {
            int start = boundaries[i];
            int end = boundaries[i + 1];

            System.Drawing.Color? foreground = null;
            System.Drawing.Color? background = null;

            foreach (StyledSpan span in spans)
            {
                if (span.Offset <= start && span.Offset + span.Length >= end)
                {
                    if (span.Foreground is { IsEmpty: false } spanForeground)
                    {
                        foreground = spanForeground;
                    }

                    if (span.Background is { IsEmpty: false } spanBackground)
                    {
                        background = spanBackground;
                    }
                }
            }

            Run run = new(text[start..end]);
            if (foreground is { } fore)
            {
                run.Foreground = ToBrush(fore);
            }

            if (background is { } back)
            {
                run.Background = ToBrush(back);
            }

            yield return run;
        }
    }

    private static IBrush ToBrush(System.Drawing.Color color)
        => new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
}
