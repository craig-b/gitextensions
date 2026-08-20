using System.Globalization;
using System.Text;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;

namespace GitCommands.Blame;

/// <summary>The blame gutter's display toggles (command toggles live in AppSettings).</summary>
public sealed record BlameDisplayOptions(
    bool ShowAuthor,
    bool ShowAuthorDate,
    bool ShowAuthorTime,
    bool DisplayAuthorFirst,
    bool ShowOriginalFilePath)
{
    public string DateTimeFormat(CultureInfo culture)
        => ShowAuthorTime
            ? culture.DateTimeFormat.ShortDatePattern + " " + culture.DateTimeFormat.ShortTimePattern
            : culture.DateTimeFormat.ShortDatePattern;
}

/// <summary>
///  The gutter text, body text, and per-line new-commit markers for a blame result. Views
///  render these verbatim; the trailing-space padding on captions deliberately reaches the
///  line end so mouse-over highlighting (done via text background) covers the full width.
/// </summary>
public sealed record BlameContents(string Gutter, string Body, IReadOnlyList<bool> IsNewCommitLine, int LineLength)
{
    public static BlameContents Empty { get; } = new("", "", [], 0);
}

public static class BlameGutterModel
{
    /// <summary>The caption width: at least 80, else 25 + the longest author + longest differing filename.</summary>
    public static int EstimateLineLength(GitBlame blame, string? posixFileName)
    {
        int filePathLengthEstimate = blame.Lines.Where(l => posixFileName != l.Commit.FileName)
                                                .Select(l => l.Commit.FileName.Length)
                                                .DefaultIfEmpty(0)
                                                .Max();
        int lineLengthEstimate = 25 + blame.Lines.Max(l => l.Commit.Author?.Length ?? 0) + filePathLengthEstimate;
        return Math.Max(80, lineLengthEstimate);
    }

    /// <summary>
    ///  One line's caption: author/date ordering per the options, " - " separators only
    ///  between shown parts, the original file path only when it differs, right-padded to
    ///  <paramref name="lineLength"/>.
    /// </summary>
    public static string BuildCaption(GitBlameLine line, StringBuilder builder, int lineLength, string dateTimeFormat, string? posixFileName, BlameDisplayOptions options)
    {
        if (options.ShowAuthor && options.DisplayAuthorFirst)
        {
            builder.Append(line.Commit.Author);
            if (options.ShowAuthorDate)
            {
                builder.Append(" - ");
            }
        }

        if (options.ShowAuthorDate)
        {
            builder.Append(line.Commit.AuthorTime.ToString(dateTimeFormat));
        }

        if (options.ShowAuthor && !options.DisplayAuthorFirst)
        {
            if (options.ShowAuthorDate)
            {
                builder.Append(" - ");
            }

            builder.Append(line.Commit.Author);
        }

        if (options.ShowOriginalFilePath && posixFileName != line.Commit.FileName)
        {
            builder.Append(" - ");
            builder.Append(line.Commit.FileName);
        }

        builder.Append(' ', Math.Max(0, lineLength - builder.Length)).AppendLine();

        return builder.ToString();
    }

    /// <summary>
    ///  The full gutter/body pair: consecutive lines of the same commit get a blank gutter
    ///  line, captions are cached per commit.
    /// </summary>
    public static BlameContents Build(GitBlame blame, string? fileName, BlameDisplayOptions options, CultureInfo culture)
    {
        if (blame.Lines.Count == 0)
        {
            return BlameContents.Empty;
        }

        string dateTimeFormat = options.DateTimeFormat(culture);
        string? posixFileName = fileName?.ToPosixPath();

        int lineLength = EstimateLineLength(blame, posixFileName);
        StringBuilder lineBuilder = new(lineLength + 2);
        StringBuilder gutter = new(capacity: lineBuilder.Capacity * blame.Lines.Count);
        StringBuilder body = new(capacity: 4096);
        string emptyLine = new(' ', lineLength);
        Dictionary<ObjectId, string> captionCache = [];
        bool[] isNewCommitLine = new bool[blame.Lines.Count];

        GitBlameCommit? lastCommit = null;
        for (int index = 0; index < blame.Lines.Count; index++)
        {
            GitBlameLine line = blame.Lines[index];
            if (line.Commit == lastCommit)
            {
                gutter.AppendLine(emptyLine);
            }
            else
            {
                isNewCommitLine[index] = true;
                if (!captionCache.TryGetValue(line.Commit.ObjectId, out string? caption))
                {
                    caption = BuildCaption(line, lineBuilder, lineLength, dateTimeFormat, posixFileName, options);
                    captionCache.Add(line.Commit.ObjectId, caption);
                    lineBuilder.Clear();
                }

                gutter.Append(caption);
            }

            body.AppendLine(line.Text);

            lastCommit = line.Commit;
        }

        return new BlameContents(gutter.ToString(), body.ToString(), isNewCommitLine, lineLength);
    }
}

/// <summary>The 7-bucket age gradient behind the gutter (index only; colors are the view's).</summary>
public static class BlameAgeBuckets
{
    public const int BucketCount = 7;

    /// <summary>Ages older than this boundary all land in the oldest bucket's scale.</summary>
    public static DateTime ArtificialOldBoundary(DateTime now) => now.AddYears(-3);

    public static IReadOnlyList<int> Compute(IReadOnlyList<GitBlameLine> blameLines, DateTime now)
    {
        long mostRecentDate = now.Ticks;
        DateTime artificialOldBoundary = ArtificialOldBoundary(now);

        long lessRecentDate = Math.Min(artificialOldBoundary.Ticks,
                                      blameLines.Select(l => l.Commit.AuthorTime)
                                                .Where(d => d != DateTime.MinValue)
                                                .DefaultIfEmpty(artificialOldBoundary)
                                                .Min()
                                                .Ticks);
        long intervalSize = (mostRecentDate - lessRecentDate + 1) / BucketCount;

        int[] buckets = new int[blameLines.Count];
        for (int index = 0; index < blameLines.Count; index++)
        {
            long relativeTicks = Math.Max(0, blameLines[index].Commit.AuthorTime.Ticks - lessRecentDate);
            buckets[index] = Math.Min((int)(relativeTicks / intervalSize), BucketCount - 1);
        }

        return buckets;
    }
}

/// <summary>Contiguous zero-based [Start, End] line ranges belonging to one commit.</summary>
public static class BlameLineRuns
{
    public static IReadOnlyList<(int Start, int End)> ForCommit(IReadOnlyList<GitBlameLine> lines, GitBlameCommit commit)
    {
        List<(int Start, int End)> runs = [];
        int startLine = -1;
        int prevLine = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            if (ReferenceEquals(lines[i].Commit, commit))
            {
                if (prevLine != i - 1 && startLine != -1)
                {
                    runs.Add((startLine, prevLine));
                    startLine = -1;
                }

                prevLine = i;
                if (startLine == -1)
                {
                    startLine = i;
                }
            }
        }

        if (startLine != -1)
        {
            runs.Add((startLine, prevLine));
        }

        return runs;
    }
}

/// <summary>The line the viewer scrolls to after a (re)load.</summary>
public static class BlameTargetLine
{
    public static int Resolve(int? clickedOriginLineNumber, int? initialLine, bool sameFile, int currentFileLine)
        => clickedOriginLineNumber ?? initialLine ?? (sameFile ? currentFileLine : 1);

    public static int Clamp(int line, int lineCount) => Math.Min(line, lineCount);
}

/// <summary>
///  Which parent "blame previous revision" targets - replaces the menu-caption-as-state
///  coupling in BlameControl. The actual (un-rewritten) parent wins when it is in the grid;
///  otherwise the visible revision's parent, when that one is.
/// </summary>
public enum BlamePreviousTarget
{
    Disabled,
    ActualParent,
    VisibleParent,
}

public static class BlamePreviousRevisionModel
{
    public static BlamePreviousTarget Resolve(
        GitRevision? visibleRevision,
        GitRevision? actualRevision,
        Func<ObjectId?, bool> isInGrid)
    {
        if (visibleRevision is null)
        {
            return BlamePreviousTarget.Disabled;
        }

        if (HasParentInGrid(actualRevision))
        {
            return BlamePreviousTarget.ActualParent;
        }

        return HasParentInGrid(visibleRevision) ? BlamePreviousTarget.VisibleParent : BlamePreviousTarget.Disabled;

        bool HasParentInGrid(GitRevision? revision)
            => revision?.HasParent is true && isInGrid(revision.FirstParentId);
    }
}
