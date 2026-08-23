using GitCommands.RichText;

namespace GitCommandsTests.RichText;

public class RichContentTests
{
    [Test]
    public void Empty_content_serializes_to_empty_strings()
    {
        RichContent content = new();
        content.IsEmpty.Should().BeTrue();
        content.ToXhtml().Should().BeEmpty();
        content.ToPlainText().Should().BeEmpty();
    }

    [Test]
    public void AddText_encodes_markup_characters_in_xhtml_only()
    {
        RichContent content = new RichContent().AddText("a <b> & 'c' \"d\"");
        content.ToXhtml().Should().Be("a &lt;b&gt; &amp; &#39;c&#39; &quot;d&quot;");
        content.ToPlainText().Should().Be("a <b> & 'c' \"d\"");
    }

    [Test]
    public void AddText_with_null_or_empty_is_a_noop()
    {
        RichContent content = new RichContent().AddText(null).AddText("");
        content.IsEmpty.Should().BeTrue();
    }

    [Test]
    public void AddLink_reproduces_LinkFactory_markup_exactly()
    {
        // The reference string is what LinkFactory.CreateLink has always produced:
        // "<a href=" + HtmlEncode(uri).Quote("'") + ">" + HtmlEncode(caption) + "</a>"
        RichContent content = new RichContent().AddLink("John <Doe>", "mailto:john@doe.com");
        content.ToXhtml().Should().Be("<a href='mailto:john@doe.com'>John &lt;Doe&gt;</a>");
        content.ToPlainText().Should().Be("John <Doe>");
    }

    [Test]
    public void AddLink_encodes_the_target_uri()
    {
        RichContent content = new RichContent().AddLink("branch", "gitext://gotobranch/feature/a&b");
        content.ToXhtml().Should().Be("<a href='gitext://gotobranch/feature/a&amp;b'>branch</a>");
    }

    [Test]
    public void AddUnderlined_wraps_encoded_text()
    {
        RichContent content = new RichContent().AddUnderlined("tag <1>");
        content.ToXhtml().Should().Be("<u>tag &lt;1&gt;</u>");
        content.ToPlainText().Should().Be("tag <1>");
    }

    [Test]
    public void AddLine_appends_environment_newline()
    {
        RichContent content = new RichContent().AddLine("first").AddLine().AddText("second");
        content.ToPlainText().Should().Be($"first{Environment.NewLine}{Environment.NewLine}second");
        content.ToXhtml().Should().Be($"first{Environment.NewLine}{Environment.NewLine}second");
    }

    [Test]
    public void SegmentsEqual_compares_by_value()
    {
        RichContent left = new RichContent().AddText("a").AddLink("b", "gitext://gototag/b");
        RichContent right = new RichContent().AddText("a").AddLink("b", "gitext://gototag/b");
        left.SegmentsEqual(right).Should().BeTrue();
        right.AddText("x");
        left.SegmentsEqual(right).Should().BeFalse();
        left.SegmentsEqual(null).Should().BeFalse();
    }

    [Test]
    public void Append_concatenates_segments_in_order()
    {
        RichContent first = new RichContent().AddText("a");
        RichContent second = new RichContent().AddLink("b", "gitext://gotocommit/deadbeef").AddText("c");
        first.Append(second).Append(null);
        first.ToPlainText().Should().Be("abc");
        first.Segments.Should().HaveCount(3);
    }

    [Test]
    public void From_creates_single_text_content()
    {
        RichContent.From("x").ToXhtml().Should().Be("x");
        RichContent.From(null).IsEmpty.Should().BeTrue();
    }
}
