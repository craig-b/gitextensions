using GitCommands.Commit;

namespace GitCommandsTests.Commit;

/// <summary>
///  Tests for <see cref="CommitMessageTemplateExpander.Expand"/>. The cases were converted from
///  GitUI.Tests' FormCommitTests, which needed a whole FormCommit only to reach
///  Module.GetSelectedBranch().
/// </summary>
public class CommitMessageTemplateExpanderTests
{
    [TestCase("Commit message")]
    [TestCase("Commit message begin ? () {} [] end")]
    [TestCase("Commit message begin {}[] end")]
    [TestCase("Commit message ?? (()) [[]] end")]
    [TestCase("Commit message {{ } end")]
    [TestCase("Commit message { }} end")]
    [TestCase("{ } end")]
    public void Expand_should_not_change_message_without_complete_placeholder(string message)
    {
        CommitMessageTemplateExpander.Expand(message, () => "master").Should().Be(message);
    }

    [TestCase("Commit message {{}}", "Commit message ")]
    [TestCase("Commit message {{}} end", "Commit message  end")]
    [TestCase("Commit message{{}}end", "Commit messageend")]
    [TestCase("Commit message {{.*}}", "Commit message ")]
    public void Expand_should_replace_with_empty_for_empty_branch_name(string message, string expectedValue)
    {
        CommitMessageTemplateExpander.Expand(message, () => string.Empty).Should().Be(expectedValue);
    }

    [TestCase("master", "Message:{{(.*)}} end", "Message:master end")]
    [TestCase("master", "Message:{{.*}} end", "Message: end")] // no group
    [TestCase("master", @"Name: {{([A-Z]+-\d+)-(.*)}}", "Name: ")] // no matching
    [TestCase("feature/ABC-4587-commitMessageRegex", @"{{([A-Z]+-\d+)}}: My message, issue:{{[A-Z]+-(\d+)}}", "ABC-4587: My message, issue:4587")] // multiple regex
    [TestCase("feature/ABC-4587-commitMessageRegex", @"Name: {{([A-Z]+-\d+)-(.*)}}[2], issue: {{([A-Z]+-\d+)-(.*)}}[1]", "Name: commitMessageRegex, issue: ABC-4587")] // regex indexing
    [TestCase("feature/ABC-4587-commitMessageRegex", @"{{([A-Z]+-\d+)-(.*)}}: My message", "ABC-4587: My message")] // default index is 1
    [TestCase("feature/ABC-4587-commitMessageRegex", @"Commit from:{{^feature/(.*)$}}[2] branch", "Commit from: branch")] // overindexing
    [TestCase("feature/ABC-4587-commitMessageRegex", @"Commit from:{{^feature/(.*)$}} branch", "Commit from:ABC-4587-commitMessageRegex branch")]
    public void Expand_should_replace_based_on_branch_name(string branch, string message, string expectedValue)
    {
        CommitMessageTemplateExpander.Expand(message, () => branch).Should().Be(expectedValue);
    }

    [Test]
    public void Expand_should_return_partially_expanded_message_when_a_pattern_is_invalid()
    {
        // First placeholder expands; the second's invalid pattern throws and is swallowed.
        CommitMessageTemplateExpander.Expand("a:{{(.*)}} b:{{(}}", () => "master").Should().Be("a:master b:{{(}}");
    }
}
