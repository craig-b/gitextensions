using GitExtensions.Extensibility;

namespace GitUIPluginInterfacesTests;

/// <summary>
///  Round-trip tests for the persisted font format.
/// </summary>
/// <remarks>
///  These previously operated on <c>System.Drawing.Font</c>. The serialised strings in every
///  TestCase below are unchanged, which is the point: FontParser now produces and consumes
///  <see cref="FontDescriptor"/>, but the on-disk format is identical, so settings written by
///  earlier versions still parse.
/// </remarks>
public class FontParserTests
{
    private FontDescriptor _defaultFont = null!;

    [SetUp]
    public void Setup()
    {
        _defaultFont = new FontDescriptor("Arial", 9);
    }

    [TestCase(false, false, "Arial;9;_IC_;0;0")]
    [TestCase(true, false, "Arial;9;_IC_;1;0")]
    [TestCase(false, true, "Arial;9;_IC_;0;1")]
    [TestCase(true, true, "Arial;9;_IC_;1;1")]
    public void AsString_should_persist_font_with_styles(bool bold, bool italic, string? serialised)
    {
        FontDescriptor font = new("Arial", 9, bold, italic);
        font.AsString().Should().Be(serialised);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("\t")]
    public void Parse_should_return_default_if_null_or_empty(string? serialised)
    {
        FontDescriptor font = serialised.Parse(_defaultFont);

        font.Should().Be(_defaultFont);
    }

    [TestCase("Arial")]
    [TestCase("Arial;")]
    public void Parse_should_return_default_if_less_then_two_parts(string? serialised)
    {
        FontDescriptor font = serialised.Parse(_defaultFont);

        font.Should().Be(_defaultFont);
    }

    [TestCase("Courier;8.25;", "Courier", 8.25f, false, false)]
    [TestCase("Courier;12;_IC_", "Courier", 12f, false, false)]
    [TestCase("Courier;11,3;", "Courier", 11.3f, false, false)]
    [TestCase("Courier;11,3;ru", "Courier", 11.3f, false, false)]
    [TestCase("Courier;12;_IC_;0;0", "Courier", 12f, false, false)]
    [TestCase("Courier;12;_IC_;1;0", "Courier", 12f, true, false)]
    [TestCase("Courier;12;_IC_;0;1", "Courier", 12f, false, true)]
    [TestCase("Courier;12;_IC_;1;1", "Courier", 12f, true, true)]
    public void Parse_should_parse(string? serialised, string name, float size, bool bold, bool italic)
    {
        FontDescriptor font = serialised.Parse(_defaultFont);

        font.Should().NotBe(_defaultFont);

        // FamilyName is the requested name, as OriginalFontName was: a descriptor records what was
        // configured and does not resolve or substitute an unavailable family.
        font.FamilyName.Should().Be(name);
        font.Size.Should().Be(size);
        font.Bold.Should().Be(bold);
        font.Italic.Should().Be(italic);
    }

    [Test]
    public void AsString_and_Parse_should_round_trip()
    {
        FontDescriptor original = new("Cascadia Mono", 10.5f, Bold: true, Italic: true);

        original.AsString().Parse(_defaultFont).Should().Be(original);
    }
}
