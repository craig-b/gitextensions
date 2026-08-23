namespace GitUI.Editor.Diff;

/// <summary>
///  The diff pane's copy transforms (context-menu redesign, surface 4) - the pure halves of
///  WinForms' Copy / Copy new version / Copy old version, lifted out of FileViewer(Internal).
///  All operate on the rendered diff text and the selection's character offsets; the header
///  region (before the first hunk) always copies verbatim, and there is no whole-document
///  fallback - callers pass a real selection (reviewed decision).
/// </summary>
public static class DiffCopyModel
{
    /// <summary>
    ///  "Copy": the selection with the diff line prefixes stripped, when the selection starts
    ///  inside the hunks; a selection touching the file header keeps prefixes verbatim.
    ///  <paramref name="fullPrefixes"/> comes from the pane's DiffHighlightService
    ///  (single-char for plain diffs, longer for combined diffs).
    /// </summary>
    public static string? CopySelection(string fileText, int selectionStart, string selectedText, string[] fullPrefixes)
    {
        if (string.IsNullOrEmpty(selectedText))
        {
            return null;
        }

        int firstHunk = fileText.IndexOf("\n@@", StringComparison.Ordinal);
        if (firstHunk > selectionStart)
        {
            return selectedText;
        }

        string code = PadToLineStart(fileText, selectionStart, selectedText);
        return string.Join("\n", code.Split('\n').Select(RemovePrefix));

        string RemovePrefix(string line)
        {
            foreach (string prefix in fullPrefixes)
            {
                if (line.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return line[prefix.Length..];
                }
            }

            return line;
        }
    }

    /// <summary>"Copy new version": the selection as the file reads AFTER the change (drops '-' lines).</summary>
    public static string? CopyNewVersion(string fileText, int selectionStart, string selectedText)
        => CopyVersion(fileText, selectionStart, selectedText, dropPrefix: '-');

    /// <summary>"Copy old version": the selection as the file read BEFORE the change (drops '+' lines).</summary>
    public static string? CopyOldVersion(string fileText, int selectionStart, string selectedText)
        => CopyVersion(fileText, selectionStart, selectedText, dropPrefix: '+');

    private static string? CopyVersion(string fileText, int selectionStart, string selectedText, char dropPrefix)
    {
        if (string.IsNullOrEmpty(selectedText))
        {
            return null;
        }

        string text = PadToLineStart(fileText, selectionStart, selectedText);

        // Keep empty lines, lines not starting with the dropped marker, and the "---"/"+++"
        // file-header lines (first three chars identical) - the WinForms rule verbatim.
        IEnumerable<string> lines = text.Split('\n')
            .Where(line => line.Length == 0
                || line[0] != dropPrefix
                || (line.Length > 2 && line[1] == line[0] && line[2] == line[0]));

        int firstHunk = fileText.IndexOf("\n@@", StringComparison.Ordinal);
        if (firstHunk <= selectionStart)
        {
            lines = lines.Select(line => line.Length > 0 && " -+".Contains(line[0]) ? line[1..] : line);
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    ///  A selection starting mid-line gets an artificial leading space so the first line
    ///  survives prefix stripping intact (the stripped char is the pad, not content).
    /// </summary>
    private static string PadToLineStart(string fileText, int selectionStart, string selectedText)
        => selectionStart > 0 && selectionStart <= fileText.Length && fileText[selectionStart - 1] != '\n'
            ? " " + selectedText
            : selectedText;
}
