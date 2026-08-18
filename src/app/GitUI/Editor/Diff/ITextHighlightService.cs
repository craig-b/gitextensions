namespace GitUI.Editor.Diff;

public interface ITextHighlightService
{
    /// <summary>
    /// The parsed diff-line metadata for the diff line number gutter, if this service produces any.
    /// </summary>
    DiffLinesInfo? DiffLinesInfo { get; }

    /// <summary>
    /// Whether the diff line number gutter should show a left (original) column.
    /// Only relevant for services that set <see cref="DiffLinesInfo"/>.
    /// </summary>
    bool ShowLeftColumn { get; }

    /// <summary>
    /// Get the styled spans computed for the current text.
    /// This is primarily used to highlight changed files for diffs.
    /// </summary>
    /// <returns>The styled spans to apply to the document.</returns>
    IReadOnlyList<StyledSpan> GetHighlighting();

    /// <summary>
    /// Check if the line type is a search match for next/previous navigation, e.g. +/- for regular patches.
    /// </summary>
    /// <param name="lineType">The line type at the index in the viewer text, if known.</param>
    /// <returns><see langword="true"/> if the line is a searchmatch; otherwise <see langword="false"/>.</returns>
    bool IsSearchMatch(DiffLineType? lineType);
}
