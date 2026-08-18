using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using GitCommands;
using GitExtensions.Extensibility;

namespace GitUI.Editor.Diff;

/// <summary>
/// Highlight difftastic diff
/// </summary>
public partial class DifftasticHighlightService : TextHighlightService
{
    protected readonly List<StyledSpan> _textMarkers = [];
    private DiffLinesInfo _diffLinesInfo = new();

    [GeneratedRegex(@"^(\s*(?<matchStart>(?<lineNo>\d+)|(\.+)) )", RegexOptions.ExplicitCapture)]
    private static partial Regex LineNoRegex { get; }

    public DifftasticHighlightService(ref string text, out int rightColumnStart)
    {
        // Hide VRulerPos by default
        rightColumnStart = 0;
        if (!int.TryParse(new EnvironmentAbstraction().GetEnvironmentVariable("DFT_WIDTH"), out int column))
        {
            column = 80;
        }

        StringBuilder sb = new(text.Length);
        StringBuilder lineBuilder = column > 0 ? new(column) : new();
        List<StyledSpan> textMarkers = [];
        int halfColumn = column / 2;
        bool nextIsHeader = true;
        bool debugPrinted = false;
        bool reverseGitColoring = AppSettings.ReverseGitColoring.Value;

        foreach (string rawLine in text.LazySplit('\n'))
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                // Empty rawLine before header
                nextIsHeader = true;
                continue;
            }

            lineBuilder.Clear();
            textMarkers.Clear();
            AnsiEscapeUtilities.ParseEscape(rawLine, lineBuilder, textMarkers, themeColors: reverseGitColoring);

            int leftLineNo = DiffLineInfo.NotApplicableLineNum;
            int rightLineNo = DiffLineInfo.NotApplicableLineNum;

            if (nextIsHeader)
            {
                nextIsHeader = false;
                AddInfo(leftLineNo, rightLineNo, DiffLineType.Header, textMarkers, lineBuilder);
                continue;
            }

            DiffLineType lineType = DiffLineType.Context;

            Match matchLeft = LineNoRegex.Match(lineBuilder.ToString());
            if (!matchLeft.Success)
            {
                // This could also be a side-by-diff with zero context where ... are not printed
                Debug.WriteLineIf(debugPrinted, $"Unexpected Difftastic output, no left line. This could occur for last line and may be OK if another difftool is used. ({_diffLinesInfo.DiffLines.Count}) {lineBuilder}");
                debugPrinted = true;
                AddInfo(leftLineNo, rightLineNo, lineType, textMarkers, lineBuilder);
                continue;
            }

            int leftLen;
            if (matchLeft.Groups["matchStart"].Index >= halfColumn)
            {
                // No left line number (occurs if zero context), ignore (unexpected) markers
                leftLen = 0;
                if (rightColumnStart > 0)
                {
                    // Keep a consistent start for the second column
                    leftLen = halfColumn - rightColumnStart;
                }
            }
            else
            {
                leftLen = matchLeft.Length;
                if (matchLeft.Groups["lineNo"].Success && int.TryParse(matchLeft.Groups["lineNo"].ValueSpan, out int lineNo))
                {
                    leftLineNo = lineNo;
                }

                if (textMarkers.Count > 0 && textMarkers[0].Offset < leftLen)
                {
                    // Use lineno coloring to guess if this is added or removed.
                    Color c = reverseGitColoring ? (textMarkers[0].Background ?? Color.Empty) : (textMarkers[0].Foreground ?? Color.Empty);
                    if (!IsUnchanged(c))
                    {
                        // Use mostly red/green to detect removed/added.
                        if (c.R > c.G)
                        {
                            lineType = DiffLineType.MinusLeft;
                        }
                        else
                        {
                            lineType = DiffLineType.PlusRight;
                            rightLineNo = leftLineNo;
                            leftLineNo = DiffLineInfo.NotApplicableLineNum;
                        }
                    }
                }
            }

            // Trim left lineno from text, marker
            if (leftLen > 0)
            {
                lineBuilder = lineBuilder.Remove(0, leftLen);
                for (int i = 0; i < textMarkers.Count; ++i)
                {
                    textMarkers[i] = RemoveLineNoPart(textMarkers[i], 0, leftLen);
                }
            }

            // Where to try parse for next line number, if both-sides is displayed
            int rightStartOffset = halfColumn - leftLen;
            Match matchRight;
            if (lineBuilder.Length > rightStartOffset
                && LineNoRegex.Match(lineBuilder.ToString()[rightStartOffset..]) is Match match
                && match.Success)
            {
                matchRight = match;
                if (rightColumnStart == 0)
                {
                    // Keep a consistent start for the second column
                    rightColumnStart = rightStartOffset;
                }
            }
            else
            {
                // Lineno assumed in start of line
                rightStartOffset = 0;
                matchRight = LineNoRegex.Match(lineBuilder.ToString());
            }

            if (!matchRight.Success)
            {
                if (lineType != DiffLineType.PlusRight)
                {
                    Debug.WriteLineIf(debugPrinted, $"Unexpected Difftastic no right lineno. This is OK if another difftool is used. ({_diffLinesInfo.DiffLines.Count}) {lineBuilder}");
                    debugPrinted = true;
                }

                AddInfo(leftLineNo, rightLineNo, lineType, textMarkers, lineBuilder);
                continue;
            }

            if (lineType == DiffLineType.PlusRight)
            {
                Debug.WriteLineIf(debugPrinted, $"Unexpected Difftastic has PlusRight and right lineno. This is OK if another difftool is used. ({_diffLinesInfo.DiffLines.Count}) {lineBuilder}");
                debugPrinted = true;
            }

            if (matchRight.Groups["lineNo"].Success && int.TryParse(matchRight.Groups["lineNo"].ValueSpan, out int rightNo))
            {
                rightLineNo = rightNo;
            }

            // Remove right line no from text, markers
            int rightLen = matchRight.Length;
            lineBuilder = lineBuilder.Remove(rightStartOffset, rightLen);
            int columnGap = 0;

            if (rightStartOffset > 0)
            {
                if (rightColumnStart == 0)
                {
                    rightColumnStart = rightStartOffset;
                }

                // Add spaces so both-sides markers are aligned
                columnGap = rightColumnStart - rightStartOffset;
                if (columnGap > 0)
                {
                    lineBuilder = lineBuilder.Insert(rightStartOffset, new string(' ', columnGap));
                }
            }

            bool first = true;
            for (int i = 0; i < textMarkers.Count; ++i)
            {
                StyledSpan tm = textMarkers[i];
                if (tm.EndOffset < rightStartOffset)
                {
                    continue;
                }

                if (first)
                {
                    first = false;

                    // Use lineno coloring to guess if this is added or removed.
                    // If not unchanged this right lineno is assumed to be added (and is likely green).
                    Color c = reverseGitColoring ? (tm.Background ?? Color.Empty) : (tm.Foreground ?? Color.Empty);
                    if (!IsUnchanged(c))
                    {
                        DebugHelpers.Assert(lineType != DiffLineType.PlusRight, $"Left status for rightline {rightLineNo} is {lineType}, incorrect leftline parsing?");

                        // Merge line type with existing left line type
                        lineType = lineType == DiffLineType.Context ? DiffLineType.PlusRight : DiffLineType.MinusPlus;
                    }
                }

                tm = RemoveLineNoPart(tm, rightStartOffset, rightLen);
                if (tm.Offset >= rightStartOffset)
                {
                    tm = tm with { Offset = tm.Offset + columnGap };
                }

                textMarkers[i] = tm;
            }

            AddInfo(leftLineNo, rightLineNo, lineType, textMarkers, lineBuilder);
        }

        text = sb.ToString();

        return;

        // Use lineno coloring to guess if this is added or removed.
        // Assume the theme in Diffstatic sets gray for unchanged.
        static bool IsUnchanged(Color c)
            => c.R == c.G;

        static StyledSpan RemoveLineNoPart(StyledSpan tm, int offset, int length)
        {
            if (tm.Offset + tm.Length <= offset)
            {
                // All is before the gap
                return tm;
            }

            if (tm.Offset >= offset + length)
            {
                // All is after the gap
                return tm with { Offset = tm.Offset - length };
            }

            if (tm.Offset <= offset && offset + length <= tm.Offset + tm.Length)
            {
                // Gap is covered
                return tm with { Length = tm.Length - length };
            }

            if (tm.Offset > offset)
            {
                // Remove the start in the gap
                return tm with { Length = tm.Length - (tm.Offset - offset), Offset = offset };
            }

            // the end part of the gap
            return tm with { Length = tm.Length - (tm.Offset + tm.Length - offset) };
        }

        void AddInfo(int leftLineNo, int rightLineNo, DiffLineType lineType, List<StyledSpan> textMarkers, StringBuilder lineBuilder)
        {
            _diffLinesInfo.Add(
                new()
                {
                    LineNumInDiff = _diffLinesInfo.DiffLines.Count + 1,
                    LeftLineNumber = leftLineNo,
                    RightLineNumber = rightLineNo,
                    LineType = lineType,
                    LineSegment = null,
                    IsMovedLine = false,
                });
            for (int i = 0; i < textMarkers.Count; ++i)
            {
                StyledSpan tm = textMarkers[i];
                if (tm.Length <= 0)
                {
                    textMarkers.RemoveAt(i);
                    --i;
                    continue;
                }

                textMarkers[i] = tm with { Offset = tm.Offset + sb.Length };
            }

            _textMarkers.AddRange(textMarkers);
            sb.Append(lineBuilder);
            sb.Append('\n');
        }
    }

    public override DiffLinesInfo DiffLinesInfo => _diffLinesInfo;

    public override IReadOnlyList<StyledSpan> GetHighlighting() => _textMarkers;

    public override bool IsSearchMatch(DiffLineType? lineType)
        => lineType is DiffLineType.Plus or DiffLineType.Minus or DiffLineType.MinusPlus or DiffLineType.MinusLeft or DiffLineType.PlusRight;
}
