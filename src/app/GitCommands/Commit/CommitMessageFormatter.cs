using GitExtensions.Extensibility;
using GitUI.CommandsDialogs.CommitDialog;

namespace GitCommands.Commit;

/// <summary>
///  The live formatting rules the commit dialog applies while typing; <see cref="FromSettings"/>
///  reads the live values (they can change while the dialog is open).
/// </summary>
public sealed record CommitMessageFormattingRules(
    int MaxCharsFirstLine,
    int MaxCharsPerLine,
    bool SecondLineMustBeEmpty,
    bool AutoWrap,
    bool IndentAfterFirstLine)
{
    public static CommitMessageFormattingRules FromSettings()
        => new(
            AppSettings.CommitValidationMaxCntCharsFirstLine,
            AppSettings.CommitValidationMaxCntCharsPerLine,
            AppSettings.CommitValidationSecondLineMustBeEmpty,
            AppSettings.CommitValidationAutoWrap,
            AppSettings.CommitValidationIndentAfterFirstLine);
}

/// <summary>
///  The commit dialog's as-you-type message formatting (extracted from FormCommit.FormatAllText):
///  enforce the empty second line, auto-wrap over-long body lines through
///  <see cref="WordWrapper"/>, and highlight over-limit text - incrementally, re-formatting only
///  lines that changed since the last pass (the <c>_formattedLines</c> cache), recursing when a
///  formatting edit itself changes the document. The algorithm is verbatim; it drives an
///  <see cref="ICommitMessageDocument"/> instead of the WinForms editor.
/// </summary>
public sealed class CommitMessageFormatter
{
    private readonly ICommitMessageDocument _document;
    private readonly List<string> _formattedLines = [];

    public CommitMessageFormatter(ICommitMessageDocument document)
    {
        _document = document;
    }

    public void FormatAllText(int startLine, CommitMessageFormattingRules rules)
    {
        int limit1 = rules.MaxCharsFirstLine;
        int limitX = rules.MaxCharsPerLine;
        bool empty2 = rules.SecondLineMustBeEmpty;
        bool commitValidationAutoWrap = rules.AutoWrap;
        bool commitValidationIndentAfterFirstLine = rules.IndentAfterFirstLine;

        int lineCount = _document.LineCount();

        TrimFormattedLines();

        for (int line = startLine; line < lineCount; line++)
        {
            if (DidFormattedLineChange(line))
            {
                bool lineChanged = FormatLine(line);
                SetFormattedLine(line);
                if (lineChanged)
                {
                    FormatAllText(line, rules);
                }
            }
        }

        return;

        void TrimFormattedLines()
        {
            if (_formattedLines.Count > lineCount)
            {
                _formattedLines.RemoveRange(lineCount, _formattedLines.Count - lineCount);
            }
        }

        bool DidFormattedLineChange(int lineNumber)
        {
            return _formattedLines.Count <= lineNumber ||
                   !_formattedLines[lineNumber].Equals(_document.Line(lineNumber), StringComparison.OrdinalIgnoreCase);
        }

        bool FormatLine(int line)
        {
            bool changed = false;

            if (limit1 > 0 && line == 0)
            {
                ColorTextAsNecessary(limit1, fullRefresh: false);
            }

            if (empty2 && line == 1)
            {
                // Ensure next line. Optionally add a bullet.
                _document.EnsureEmptyLine(commitValidationIndentAfterFirstLine, 1);
                _document.SetLineHighlight(2, 0, _document.LineLength(2), CommitMessageHighlight.Reset);
                if (FormatLine(2))
                {
                    changed = true;
                }
            }

            if (limitX > 0 && line >= (empty2 ? 2 : 1))
            {
                if (commitValidationAutoWrap && WrapIfNecessary())
                {
                    changed = true;
                }

                ColorTextAsNecessary(limitX, changed);
            }

            return changed;

            void ColorTextAsNecessary(int lineLimit, bool fullRefresh)
            {
                int lineLength = _document.LineLength(line);
                int offset = 0;
                bool textAppended = false;
                if (!fullRefresh && _formattedLines.Count > line)
                {
                    offset = _formattedLines[line].CommonPrefix(_document.Line(line)).Length;
                    textAppended = offset > 0 && offset == _formattedLines[line].Length;
                }

                int len = Math.Min(lineLimit, lineLength) - offset;

                if (!textAppended && len > 0)
                {
                    _document.SetLineHighlight(line, offset, len, CommitMessageHighlight.Normal);
                }

                if (lineLength > lineLimit)
                {
                    if (offset <= lineLimit || !textAppended)
                    {
                        offset = Math.Max(offset, lineLimit);
                        len = lineLength - offset;
                        if (len > 0)
                        {
                            _document.SetLineHighlight(line, offset, len, CommitMessageHighlight.Overlimit);
                        }
                    }
                }
            }

            bool WrapIfNecessary()
            {
                if (_document.LineLength(line) > limitX)
                {
                    string oldText = _document.Line(line);
                    string newText = WordWrapper.WrapSingleLine(oldText, limitX);
                    if (!string.Equals(oldText, newText))
                    {
                        _document.ReplaceLine(line, newText);
                        return true;
                    }
                }

                return false;
            }
        }

        void SetFormattedLine(int lineNumber)
        {
            // line not formatted yet
            if (_formattedLines.Count <= lineNumber)
            {
                DebugHelpers.Assert(_formattedLines.Count == lineNumber, $"{_formattedLines.Count}:{lineNumber}");
                _formattedLines.Add(_document.Line(lineNumber));
            }
            else
            {
                _formattedLines[lineNumber] = _document.Line(lineNumber);
            }
        }
    }
}
