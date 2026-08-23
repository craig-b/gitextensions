using System.Linq;
using GitUI.Editor.Diff;

namespace GitExtensions.Avalonia.Rendering;

/// <summary>
///  One left/right line-number pair per diff-view line, from the real (M4-portable)
///  DiffLineNumAnalyzer - the same data the WinForms FileViewer margin paints.
/// </summary>
internal static class LineNumberGutter
{
    public static string Build(string diffText, DiffLinesInfo lineNumbers)
    {
        int lineCount = diffText.Length == 0 ? 0 : diffText.Count(c => c == '\n') + (diffText.EndsWith('\n') ? 0 : 1);
        System.Text.StringBuilder gutter = new();
        for (int line = 1; line <= lineCount; line++)
        {
            if (lineNumbers.DiffLines.TryGetValue(line, out DiffLineInfo? info))
            {
                string left = info.LeftLineNumber == DiffLineInfo.NotApplicableLineNum ? "" : info.LeftLineNumber.ToString();
                string right = info.RightLineNumber == DiffLineInfo.NotApplicableLineNum ? "" : info.RightLineNumber.ToString();
                gutter.Append($"{left,5} {right,5}");
            }

            gutter.Append('\n');
        }

        return gutter.ToString();
    }
}
