using ResourceManager.CommitDataRenders;

namespace ResourceManagerTests.CommitDataRenders;
public class TabbedHeaderLabelFormatterTests
{
    private TabbedHeaderLabelFormatter _formatter = null!;

    [SetUp]
    public void Setup()
    {
        _formatter = new TabbedHeaderLabelFormatter();
    }

    [TestCase(null, 10, ":		")]
    [TestCase("", 10, ":		")]
    [TestCase("", 16, ":			")]
    [TestCase(" ", 10, " :		")]
    [TestCase("a", 10, "a:		")]
    [TestCase("a", 8, "a:	")]
    [TestCase("abc", 1, "abc:")]
    // M6: labels are raw text now (the model encodes at serialization); tab count is computed
    // from the visible label - the old expectations tabbed the ENCODED label.
    [TestCase("John Doe <John.Doe@test.com>", 38, "John Doe <John.Doe@test.com>:		")]
    [TestCase("John Doe <John.Doe@test.com>", 40, "John Doe <John.Doe@test.com>:		")]
    public void FormatLabel_should_render_correctly(string? given, int desiredLength, string expected)
    {
        _formatter.FormatLabel(given!, desiredLength).Should().Be(expected);
    }
}
