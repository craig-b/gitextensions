using GitCommands.Commit;

namespace GitCommandsTests.Commit;

/// <summary>
///  Tests for <see cref="CommitMessageValidator.Validate"/> - logic that lived inline in
///  FormCommit's IsCommitMessageValid (each violation was an immediate message box) and never
///  had coverage before the §20 extraction.
/// </summary>
public class CommitMessageValidatorTests
{
    private static readonly CommitMessageValidationRules _allDisabled = new(
        MaxCharsFirstLine: 0,
        MaxCharsPerLine: 0,
        SecondLineMustBeEmpty: false,
        RegEx: null);

    [Test]
    public void No_rules_no_violations()
    {
        CommitMessageValidator.Validate("anything at all", _allDisabled).Should().BeEmpty();
    }

    [Test]
    public void First_line_too_long()
    {
        CommitMessageValidationRules rules = _allDisabled with { MaxCharsFirstLine = 10 };

        CommitMessageValidator.Validate("a short one", rules).Should().Equal(
            new CommitMessageViolation(CommitMessageViolationKind.FirstLineTooLong));
        CommitMessageValidator.Validate("short", rules).Should().BeEmpty();
    }

    [Test]
    public void First_line_rule_skips_leading_empty_lines()
    {
        // The original used RemoveEmptyEntries, so the "first line" is the first non-empty one.
        CommitMessageValidationRules rules = _allDisabled with { MaxCharsFirstLine = 10 };

        CommitMessageValidator.Validate("\n\nthis line is too long", rules).Should().Equal(
            new CommitMessageViolation(CommitMessageViolationKind.FirstLineTooLong));
    }

    [Test]
    public void Each_over_long_line_is_its_own_violation_in_order()
    {
        CommitMessageValidationRules rules = _allDisabled with { MaxCharsPerLine = 5 };

        CommitMessageValidator.Validate("first long line\nok\nsecond long line", rules).Should().Equal(
            new CommitMessageViolation(CommitMessageViolationKind.LineTooLong, "first long line"),
            new CommitMessageViolation(CommitMessageViolationKind.LineTooLong, "second long line"));
    }

    [Test]
    public void Second_line_must_be_empty()
    {
        CommitMessageValidationRules rules = _allDisabled with { SecondLineMustBeEmpty = true };

        CommitMessageValidator.Validate("subject\nbody right away\nmore", rules).Should().Equal(
            new CommitMessageViolation(CommitMessageViolationKind.SecondLineNotEmpty));
        CommitMessageValidator.Validate("subject\n\nbody", rules).Should().BeEmpty();

        // Preserved oddity: the rule only fires for messages of more than two lines.
        CommitMessageValidator.Validate("subject\nbody right away", rules).Should().BeEmpty();
    }

    [Test]
    public void Regex_not_matched()
    {
        CommitMessageValidationRules rules = _allDisabled with { RegEx = "^JIRA-\\d+" };

        CommitMessageValidator.Validate("no ticket here", rules).Should().Equal(
            new CommitMessageViolation(CommitMessageViolationKind.RegexNotMatched));
        CommitMessageValidator.Validate("JIRA-42 fix the thing", rules).Should().BeEmpty();
    }

    [TestCase("fixup! no ticket here")]
    [TestCase("squash! no ticket here")]
    public void Fixup_and_squash_bypass_only_the_regex_rule(string message)
    {
        CommitMessageValidationRules regexOnly = _allDisabled with { RegEx = "^JIRA-\\d+" };
        CommitMessageValidator.Validate(message, regexOnly).Should().BeEmpty();

        CommitMessageValidationRules withLineRule = regexOnly with { MaxCharsPerLine = 5 };
        CommitMessageValidator.Validate(message, withLineRule).Should().Equal(
            new CommitMessageViolation(CommitMessageViolationKind.LineTooLong, message));
    }

    [Test]
    public void Amend_subject_and_separator_are_stripped_before_the_regex_check()
    {
        CommitMessageValidationRules rules = _allDisabled with { RegEx = "^JIRA-\\d+" };

        CommitMessageValidator.Validate("amend! original subject\n\nJIRA-42 reworded", rules).Should().BeEmpty();
        CommitMessageValidator.Validate("amend! original subject\n\nno ticket", rules).Should().Equal(
            new CommitMessageViolation(CommitMessageViolationKind.RegexNotMatched));

        // Without the blank separator line, the message is validated as-is.
        CommitMessageValidator.Validate("amend! subject\nJIRA-42 body\nmore", rules).Should().Equal(
            new CommitMessageViolation(CommitMessageViolationKind.RegexNotMatched));
    }

    [Test]
    public void Invalid_user_regex_is_swallowed()
    {
        CommitMessageValidationRules rules = _allDisabled with { RegEx = "(" };

        CommitMessageValidator.Validate("anything", rules).Should().BeEmpty();
    }

    [Test]
    public void Violations_are_reported_in_prompt_order()
    {
        CommitMessageValidationRules rules = new(
            MaxCharsFirstLine: 5,
            MaxCharsPerLine: 8,
            SecondLineMustBeEmpty: true,
            RegEx: "^JIRA-\\d+");

        CommitMessageValidator.Validate("a long subject\nnon-empty second\nbody", rules).Should().Equal(
            new CommitMessageViolation(CommitMessageViolationKind.FirstLineTooLong),
            new CommitMessageViolation(CommitMessageViolationKind.LineTooLong, "a long subject"),
            new CommitMessageViolation(CommitMessageViolationKind.LineTooLong, "non-empty second"),
            new CommitMessageViolation(CommitMessageViolationKind.SecondLineNotEmpty),
            new CommitMessageViolation(CommitMessageViolationKind.RegexNotMatched));
    }
}
