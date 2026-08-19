using System.Text.RegularExpressions;
using GitExtensions.Extensibility;
using GitUI.CommandsDialogs;

namespace GitCommands.Commit;

public enum CommitMessageViolationKind
{
    FirstLineTooLong,
    LineTooLong,
    SecondLineNotEmpty,
    RegexNotMatched,
}

/// <param name="OffendingLine">The over-long line for <see cref="CommitMessageViolationKind.LineTooLong"/>; null otherwise.</param>
public sealed record CommitMessageViolation(CommitMessageViolationKind Kind, string? OffendingLine = null);

/// <summary>
///  The commit-dialog validation rules; <see cref="FromSettings"/> reads the live values.
///  A max-length of 0 (and an empty regex) disables that rule, exactly as the settings UI documents.
/// </summary>
public sealed record CommitMessageValidationRules(
    int MaxCharsFirstLine,
    int MaxCharsPerLine,
    bool SecondLineMustBeEmpty,
    string? RegEx)
{
    public static CommitMessageValidationRules FromSettings()
        => new(
            AppSettings.CommitValidationMaxCntCharsFirstLine,
            AppSettings.CommitValidationMaxCntCharsPerLine,
            AppSettings.CommitValidationSecondLineMustBeEmpty,
            AppSettings.CommitValidationRegEx);
}

/// <summary>
///  Computes the commit-message validation violations the commit dialog then prompts about,
///  one prompt per violation in this exact order (extracted from FormCommit's
///  IsCommitMessageValid). Behavior notes preserved from the original:
///  each over-long line yields its own violation; fixup!/squash! messages bypass only the
///  regex rule; an amend! subject (plus its blank separator) is stripped before the regex
///  check; an invalid user regex is swallowed and treated as no violation.
/// </summary>
public static class CommitMessageValidator
{
    public static IReadOnlyList<CommitMessageViolation> Validate(string message, CommitMessageValidationRules rules)
    {
        List<CommitMessageViolation> violations = [];

        if (rules.MaxCharsFirstLine > 0)
        {
            string firstLine = message.Split(Delimiters.NewLines, StringSplitOptions.RemoveEmptyEntries)[0];
            if (firstLine.Length > rules.MaxCharsFirstLine)
            {
                violations.Add(new(CommitMessageViolationKind.FirstLineTooLong));
            }
        }

        if (rules.MaxCharsPerLine > 0)
        {
            string[] lines = message.Split(Delimiters.NewLines, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                if (line.Length > rules.MaxCharsPerLine)
                {
                    violations.Add(new(CommitMessageViolationKind.LineTooLong, line));
                }
            }
        }

        if (rules.SecondLineMustBeEmpty)
        {
            string[] lines = message.Split(Delimiters.NewLines, StringSplitOptions.None);
            if (lines.Length > 2 && lines[1].Length != 0)
            {
                violations.Add(new(CommitMessageViolationKind.SecondLineNotEmpty));
            }
        }

        if (!string.IsNullOrEmpty(rules.RegEx))
        {
            try
            {
                if (!message.StartsWith(CommitKind.Fixup.GetPrefix()) &&
                    !message.StartsWith(CommitKind.Squash.GetPrefix()) &&
                    !Regex.IsMatch(GetTextToValidate(message), rules.RegEx))
                {
                    violations.Add(new(CommitMessageViolationKind.RegexNotMatched));
                }
            }
            catch
            {
            }
        }

        return violations;
    }

    private static string GetTextToValidate(string text)
    {
        if (!text.StartsWith(CommitKind.Amend.GetPrefix()) || !text.ContainsAny(Delimiters.LineFeedAndCarriageReturnSearchValues))
        {
            return text;
        }

        string[] lines = text.Split(Delimiters.NewLines, StringSplitOptions.None);
        if (lines.Length > 2 && lines[1].Length == 0)
        {
            return string.Join(Environment.NewLine, lines.AsSpan(2));
        }

        return text;
    }
}
