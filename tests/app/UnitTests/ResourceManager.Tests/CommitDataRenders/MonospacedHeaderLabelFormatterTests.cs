using ResourceManager.CommitDataRenders;

namespace ResourceManagerTests.CommitDataRenders;
public class MonospacedHeaderLabelFormatterTests
{
    private MonospacedHeaderLabelFormatter _formatter = null!;

    [SetUp]
    public void Setup()
    {
        _formatter = new MonospacedHeaderLabelFormatter();
    }

    [TestCase(null, 10, ":         ")]
    [TestCase("", 10, ":         ")]
    [TestCase(" ", 10, " :        ")]
    [TestCase("a", 10, "a:        ")]
    [TestCase("abc", 1, "abc:")]
    // M6: labels are raw text now (the model encodes at serialization) and padding counts
    // visible characters - the old expectation padded the ENCODED label.
    [TestCase("John Doe <John.Doe@test.com>", 40, "John Doe <John.Doe@test.com>:           ")]
    public void FormatLabel_should_render_correctly(string? given, int desiredLength, string expected)
    {
        _formatter.FormatLabel(given!, desiredLength).Should().Be(expected);
    }
}
