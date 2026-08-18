namespace GitUI.Editor.Diff;

/// <summary>
/// A portable, renderer-agnostic description of a styled range of text, produced by
/// <see cref="ITextHighlightService.GetHighlighting"/>. A GitUI-side adapter applies these
/// spans to the editor's native marker strategy.
/// </summary>
public readonly record struct StyledSpan(int Offset, int Length, Color? Foreground, Color? Background)
{
    /// <summary>
    /// The INCLUSIVE end offset (the offset of the span's last character), matching the
    /// ICSharpCode TextMarker.EndOffset this type replaced - all consuming arithmetic
    /// (marker merging, per-line marker windows) was written against that convention.
    /// </summary>
    public int EndOffset => Offset + Length - 1;
}
