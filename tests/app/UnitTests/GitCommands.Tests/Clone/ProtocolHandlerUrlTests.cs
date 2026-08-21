using GitCommands.Clone;

namespace GitCommandsTests.Clone;

public sealed class ProtocolHandlerUrlTests
{
    [TestCase("git://host/repo")]
    [TestCase("http://host/repo")]
    [TestCase("https://host/repo.git")]
    public void Plain_git_and_http_urls_pass_through_unchanged(string argument)
    {
        ProtocolHandlerUrl.TryParseCloneUrl(argument).Should().Be(argument);
    }

    [TestCase("github-windows://openRepo/https://github.com/owner/repo")]
    [TestCase("github-mac://openRepo/https://github.com/owner/repo")]
    public void Github_desktop_schemes_are_stripped_to_their_payload_url(string argument)
    {
        ProtocolHandlerUrl.TryParseCloneUrl(argument).Should().Be("https://github.com/owner/repo");
    }

    [TestCase("")]
    [TestCase("blah")]
    [TestCase("ssh://host/repo")]
    [TestCase("file:///repo")]
    [TestCase("github-linux://openRepo/https://github.com/owner/repo")]
    public void Anything_else_is_not_a_protocol_handler_clone_url(string argument)
    {
        ProtocolHandlerUrl.TryParseCloneUrl(argument).Should().BeNull();
    }
}
