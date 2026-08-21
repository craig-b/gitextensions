using GitCommands.Clone;

namespace GitCommandsTests.Clone;

public sealed class CloneUrlExtractorTests
{
    [TestCase(null, false, "")]
    [TestCase("", false, "")]
    [TestCase(" ", false, "")]
    [TestCase("blah", false, "")]
    [TestCase("git clone https://github.com/gitextensions/gitextensions && cd gitextensions", true, "https://github.com/gitextensions/gitextensions")]
    [TestCase("git clone ssh://username@gerrit-server:/PROJECT", true, "ssh://username@gerrit-server:/PROJECT")]
    [TestCase("git clone https://github.com/gitextensions/gitextensions && git clone https://github.com/gitextensions/git.hub", true, "https://github.com/gitextensions/gitextensions")]
    public void TryExtractUrl_extracts_the_first_git_url(string? contents, bool expectedResult, string expectedUrl)
    {
        CloneUrlExtractor.TryExtractUrl(contents, out string url).Should().Be(expectedResult);
        url.Should().Be(expectedUrl);
    }
}
