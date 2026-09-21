using GitCommands;

namespace GitCommandsTests;

[TestFixture]
public class PathUtilHostHomeTests
{
    [TestCase("/var/git/gitextensions", "/home/craig", "/var/git/gitextensions")]
    [TestCase("/home/craig/src/foo", "/home/craig", "~/src/foo")]
    [TestCase("/home/craig", "/home/craig", "~")]

    // A trailing separator is display noise; the repository dropdown showed one.
    [TestCase("/home/craig/src/foo/", "/home/craig", "~/src/foo")]
    [TestCase("/var/git/gitextensions/", "/home/craig", "/var/git/gitextensions")]
    [TestCase("/home/craig/", "/home/craig", "~")]

    // A shared prefix is not containment: /home/craig2 is not inside /home/craig.
    [TestCase("/home/craig2/src", "/home/craig", "/home/craig2/src")]
    [TestCase("/home/craigson", "/home/craig", "/home/craigson")]

    // The home as Wine reports it may itself carry a separator.
    [TestCase("/home/craig/src", "/home/craig/", "~/src")]

    // No home to compare against: convert nothing, lose nothing.
    [TestCase("/home/craig/src", null, "/home/craig/src")]
    [TestCase("/home/craig/src", "", "/home/craig/src")]

    // The root must survive being trimmed.
    [TestCase("/", "/home/craig", "/")]
    public void AbbreviateHostHome_shortens_only_a_real_containment(string hostPath, string? home, string expected)
    {
        PathUtil.AbbreviateHostHome(hostPath, home).Should().Be(expected);
    }
}
