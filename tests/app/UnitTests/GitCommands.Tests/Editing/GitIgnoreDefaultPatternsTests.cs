using GitCommands.Editing;

namespace GitCommandsTests.Editing;

/// <summary>
///  Tests for <see cref="GitIgnoreDefaultPatterns"/>.
/// </summary>
public class GitIgnoreDefaultPatternsTests
{
    [Test]
    public void Patterns_should_be_non_empty_and_contain_ThumbsDb()
    {
        GitIgnoreDefaultPatterns.Patterns.Should().NotBeEmpty();
        GitIgnoreDefaultPatterns.Patterns.Should().Contain("Thumbs.db");
    }

    [Test]
    public void GetPatternsToAdd_should_return_patterns_not_present_in_current_content()
    {
        string[] patterns = ["one", "two", "three"];
        string currentContent = "two";

        string[] result = GitIgnoreDefaultPatterns.GetPatternsToAdd(currentContent, patterns);

        result.Should().BeEquivalentTo(["one", "three"]);
    }

    [Test]
    public void GetPatternsToAdd_should_filter_out_patterns_present_as_exact_lines()
    {
        string[] patterns = ["one", "two", "three"];
        string currentContent = string.Join(Environment.NewLine, "one", "two", "three");

        string[] result = GitIgnoreDefaultPatterns.GetPatternsToAdd(currentContent, patterns);

        result.Should().BeEmpty();
    }

    [Test]
    public void GetPatternsToAdd_with_empty_content_should_return_all_patterns()
    {
        string[] patterns = ["one", "two", "three"];

        string[] result = GitIgnoreDefaultPatterns.GetPatternsToAdd(string.Empty, patterns);

        result.Should().BeEquivalentTo(patterns);
    }

    [Test]
    public void GetPatternsToAdd_should_not_match_partial_line_content()
    {
        // "one" is present only as part of a longer line ("someone"), not as an exact line -
        // GetPatternsToAdd splits on lines, not substrings, so it should still be reported as
        // missing.
        string[] patterns = ["one"];
        string currentContent = "someone";

        string[] result = GitIgnoreDefaultPatterns.GetPatternsToAdd(currentContent, patterns);

        result.Should().BeEquivalentTo(["one"]);
    }

    [Test]
    public void Append_should_produce_exact_expected_format()
    {
        string currentContent = "existing content";
        string[] patternsToAdd = ["one", "two"];

        string result = GitIgnoreDefaultPatterns.Append(currentContent, patternsToAdd);

        result.Should().Be($"existing content{Environment.NewLine}one{Environment.NewLine}two{Environment.NewLine}");
    }

    [Test]
    public void Append_with_no_patterns_to_add_should_still_append_newline()
    {
        string currentContent = "existing content";
        string[] patternsToAdd = [];

        string result = GitIgnoreDefaultPatterns.Append(currentContent, patternsToAdd);

        result.Should().Be($"existing content{Environment.NewLine}{Environment.NewLine}");
    }
}
