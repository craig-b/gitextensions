using GitExtensions.Extensibility;

namespace GitUI.Editor.Diff;

public class TextHighlightService : ITextHighlightService
{
    /// <summary>
    /// Base class for highlighting, not adding any highlighting.
    /// </summary>
    public static TextHighlightService Instance { get; } = new();

    protected TextHighlightService()
    {
    }

    /// <summary>
    /// The parsed diff-line metadata for the diff line number gutter, if this service produces any.
    /// </summary>
    public virtual DiffLinesInfo? DiffLinesInfo => null;

    /// <summary>
    /// Whether the diff line number gutter should show a left (original) column.
    /// Only relevant for services that set <see cref="DiffLinesInfo"/>.
    /// </summary>
    public virtual bool ShowLeftColumn => true;

    public virtual IReadOnlyList<StyledSpan> GetHighlighting() => [];

    public virtual bool IsSearchMatch(DiffLineType? lineType)
    {
        DebugHelpers.Fail($"Unexpected highlight service {GetType()}, not a diff/grep type.");
        return false;
    }
}
