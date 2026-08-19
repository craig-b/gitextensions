using GitCommands.Commit;

namespace GitCommandsTests.Commit;

/// <summary>
///  Tests for <see cref="CommitMessageFormatter"/> - the as-you-type formatting that lived in
///  FormCommit.FormatAllText, driven here through a fake document that mirrors the spell-check
///  editor's line semantics (EnsureEmptyLine's insert-before, ReplaceLine splitting on
///  newlines).
/// </summary>
public class CommitMessageFormatterTests
{
    private static readonly CommitMessageFormattingRules _noRules = new(
        MaxCharsFirstLine: 0,
        MaxCharsPerLine: 0,
        SecondLineMustBeEmpty: false,
        AutoWrap: false,
        IndentAfterFirstLine: false);

    private sealed class FakeDocument : ICommitMessageDocument
    {
        public List<string> Lines { get; }
        public List<(int Line, int Offset, int Length, CommitMessageHighlight Highlight)> Highlights { get; } = [];

        public FakeDocument(params string[] lines) => Lines = [.. lines];

        public int LineCount() => Lines.Count;

        public string Line(int line) => Lines[line];

        public int LineLength(int line) => LineCount() <= line ? 0 : Lines[line].Length;

        public void ReplaceLine(int line, string withText)
        {
            string[] parts = withText.Split(Environment.NewLine);
            Lines.RemoveAt(line);
            Lines.InsertRange(line, parts);
        }

        public void EnsureEmptyLine(bool addBullet, int afterLine)
        {
            if (LineLength(afterLine) > 0)
            {
                string pushed = (addBullet ? " - " : string.Empty) + Lines[afterLine];
                Lines[afterLine] = string.Empty;
                Lines.Insert(afterLine + 1, pushed);
            }
        }

        public void SetLineHighlight(int line, int offset, int length, CommitMessageHighlight highlight)
            => Highlights.Add((line, offset, length, highlight));
    }

    private static CommitMessageFormatter Format(FakeDocument document, CommitMessageFormattingRules rules)
    {
        CommitMessageFormatter formatter = new(document);
        formatter.FormatAllText(0, rules);
        return formatter;
    }

    [Test]
    public void Over_limit_subject_is_split_into_normal_and_overlimit()
    {
        FakeDocument document = new("a subject well over the limit");

        Format(document, _noRules with { MaxCharsFirstLine = 10 });

        document.Highlights.Should().Equal(
            (0, 0, 10, CommitMessageHighlight.Normal),
            (0, 10, 19, CommitMessageHighlight.Overlimit));
    }

    [Test]
    public void Second_line_content_is_pushed_to_a_new_line()
    {
        FakeDocument document = new("subject", "body");

        Format(document, _noRules with { SecondLineMustBeEmpty = true });

        document.Lines.Should().Equal("subject", "", "body");
    }

    [Test]
    public void Second_line_content_is_pushed_with_a_bullet_when_indenting()
    {
        FakeDocument document = new("subject", "body");

        Format(document, _noRules with { SecondLineMustBeEmpty = true, IndentAfterFirstLine = true });

        document.Lines.Should().Equal("subject", "", " - body");
    }

    [Test]
    public void Over_long_body_line_is_wrapped_when_auto_wrap_is_on()
    {
        FakeDocument document = new("subject", "these words exceed the limit");

        Format(document, _noRules with { MaxCharsPerLine = 12, AutoWrap = true });

        document.Lines.Should().HaveCountGreaterThan(2);
        document.Lines[0].Should().Be("subject");
        document.Lines.Skip(1).Should().OnlyContain(line => line.Length <= 12);
    }

    [Test]
    public void Unchanged_text_is_not_reformatted_on_the_next_pass()
    {
        FakeDocument document = new("a subject well over the limit", "body");
        CommitMessageFormattingRules rules = _noRules with { MaxCharsFirstLine = 10, MaxCharsPerLine = 20 };

        CommitMessageFormatter formatter = Format(document, rules);
        int highlightsAfterFirstPass = document.Highlights.Count;

        formatter.FormatAllText(0, rules);

        document.Highlights.Should().HaveCount(highlightsAfterFirstPass);
    }

    [Test]
    public void Editing_one_line_reformats_only_that_line()
    {
        FakeDocument document = new("subject", "body one", "body two");
        CommitMessageFormattingRules rules = _noRules with { MaxCharsPerLine = 20 };

        CommitMessageFormatter formatter = Format(document, rules);
        document.Highlights.Clear();

        document.Lines[2] = "body two edited";
        formatter.FormatAllText(0, rules);

        document.Highlights.Should().OnlyContain(highlight => highlight.Line == 2);
    }
}
