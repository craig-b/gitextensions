namespace GitUI.Editor.Diff;

/// <summary>
/// A portable, renderer-agnostic description of a styled range of text, produced by
/// <see cref="ITextHighlightService.GetHighlighting"/>. A GitUI-side adapter applies these
/// spans to the editor's native marker strategy.
/// </summary>
public readonly record struct StyledSpan(int Offset, int Length, Color? Foreground, Color? Background, FontStyle Style = FontStyle.Regular)
{
    public int EndOffset => Offset + Length;
}
