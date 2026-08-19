using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using GitCommands.Commit;

namespace GitExtensions.Avalonia;

/// <summary>
///  <see cref="ICommitMessageDocument"/> over a plain Avalonia TextBox, mirroring the WinForms
///  spell-check editor's line semantics: EnsureEmptyLine inserts before the line's content and
///  pushes it down (optionally with a bullet). A plain TextBox cannot color text ranges, so
///  <see cref="SetLineHighlight"/> is a no-op until the client gains a highlighting editor -
///  the formatter's text edits (empty second line, auto-wrap) all apply.
/// </summary>
internal sealed class TextBoxCommitMessageDocument : ICommitMessageDocument
{
    private readonly TextBox _textBox;

    public TextBoxCommitMessageDocument(TextBox textBox)
    {
        _textBox = textBox;
    }

    private string[] Lines => (_textBox.Text ?? "").Replace("\r\n", "\n").Split('\n');

    private void SetLines(string[] lines) => _textBox.Text = string.Join('\n', lines);

    public int LineCount() => Lines.Length;

    public string Line(int line) => Lines[line];

    public int LineLength(int line)
    {
        string[] lines = Lines;
        return lines.Length <= line ? 0 : lines[line].Length;
    }

    public void ReplaceLine(int line, string withText)
    {
        List<string> lines = [.. Lines];
        lines.RemoveAt(line);
        lines.InsertRange(line, withText.Replace("\r\n", "\n").Split('\n'));
        SetLines([.. lines]);
    }

    public void EnsureEmptyLine(bool addBullet, int afterLine)
    {
        if (LineLength(afterLine) == 0)
        {
            return;
        }

        List<string> lines = [.. Lines];
        string pushed = (addBullet ? " - " : string.Empty) + lines[afterLine];
        lines[afterLine] = string.Empty;
        lines.Insert(afterLine + 1, pushed);
        SetLines([.. lines]);

        // Keep typing at the end of the pushed-down content, as the WinForms editor does.
        _textBox.CaretIndex = lines.Take(afterLine + 1).Sum(l => l.Length + 1) + pushed.Length;
    }

    public void SetLineHighlight(int line, int offset, int length, CommitMessageHighlight highlight)
    {
        // Plain TextBox: no range coloring available.
    }
}
