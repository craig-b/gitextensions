using GitCommands.Commit;

namespace GitCommandsTests.Commit;

/// <summary>
///  Tests for <see cref="ConventionalCommitMessage.PrefixOrReplaceKeyword"/>. The cases were
///  converted from UI.IntegrationTests' FormCommitTests, which drove them through the whole
///  form; the model needs no form.
/// </summary>
public class ConventionalCommitMessageTests
{
    [TestCase("", 0, "feat: ", 6)]
    [TestCase("text", 3, "feat: text", 9)]
    public void Keyword_is_prefixed_when_none(string initialText, int initialPosition, string expectedText, int expectedPosition)
    {
        (string message, int selectionStart) = PrefixOrReplaceKeyword("feat", initialText, initialPosition, insertScopeParentheses: false);
        message.Should().Be(expectedText);
        selectionStart.Should().Be(expectedPosition);
    }

    [TestCase("", 0, "feat(): ", 5)]
    [TestCase("text", 3, "feat(): text", 5)]
    public void Keyword_is_prefixed_when_none_with_scope(string initialText, int initialPosition, string expectedText, int expectedPosition)
    {
        (string message, int selectionStart) = PrefixOrReplaceKeyword("feat", initialText, initialPosition, insertScopeParentheses: true);
        message.Should().Be(expectedText);
        selectionStart.Should().Be(expectedPosition);
    }

    [TestCase("fix: ", 0, "feat: ", 6)]
    [TestCase("fix: text", 3, "feat: text", 6)]
    public void Keyword_is_prefixed_when_already_typed(string initialText, int initialPosition, string expectedText, int expectedPosition)
    {
        (string message, int selectionStart) = PrefixOrReplaceKeyword("feat", initialText, initialPosition, insertScopeParentheses: false);
        message.Should().Be(expectedText);
        selectionStart.Should().Be(expectedPosition);
    }

    [TestCase("fix: ", 0, "feat(): ", 5)]
    [TestCase("fix: text", 3, "feat(): text", 5)]
    [TestCase("fix(scope): ", 0, "feat(scope): ", 13)]
    [TestCase("fix(scope): text", 14, "feat(scope): text", 15)]
    public void Keyword_is_prefixed_when_already_typed_with_scope(string initialText, int initialPosition, string expectedText, int expectedPosition)
    {
        (string message, int selectionStart) = PrefixOrReplaceKeyword("feat", initialText, initialPosition, insertScopeParentheses: true);
        message.Should().Be(expectedText);
        selectionStart.Should().Be(expectedPosition);
    }

    [Test]
    public void Whitespace_only_message_is_treated_as_empty()
    {
        (string message, int selectionStart) = PrefixOrReplaceKeyword("feat", "  ", currentPosition: 0, insertScopeParentheses: false);
        message.Should().Be("feat: ");
        selectionStart.Should().Be(6);
    }

    // Mirrors how FormCommit supplies the view state: the first line of the document, empty
    // when the whole message is whitespace.
    private static (string Message, int SelectionStart) PrefixOrReplaceKeyword(string keyword, string messageText, int currentPosition, bool insertScopeParentheses)
        => ConventionalCommitMessage.PrefixOrReplaceKeyword(
            keyword,
            messageText,
            firstLine: string.IsNullOrWhiteSpace(messageText) ? string.Empty : messageText.Split('\n')[0],
            currentPosition,
            insertScopeParentheses);
}
