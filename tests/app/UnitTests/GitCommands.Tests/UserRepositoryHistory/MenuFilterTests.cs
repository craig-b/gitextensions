using GitCommands.UserRepositoryHistory;

namespace GitCommandsTests.UserRepositoryHistory;
public class MenuFilterTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void IsVisible_should_show_every_item_for_a_blank_filter(string? filterText)
    {
        MenuFilter.IsVisible("GitExtensions", filterText).Should().BeTrue();
        MenuFilter.IsVisible(null, filterText).Should().BeTrue();
    }

    [TestCase("GitExtensions", "Extensions", true)]
    [TestCase("GitExtensions", "gitext", true)]
    [TestCase("GitExtensions", "TENSION", true)]
    [TestCase("GitExtensions", "svn", false)]
    public void IsVisible_should_match_case_insensitive_substrings(string itemText, string filterText, bool expected)
    {
        MenuFilter.IsVisible(itemText, filterText).Should().Be(expected);
    }

    [Test]
    public void IsVisible_should_hide_items_without_text_for_a_non_blank_filter()
    {
        MenuFilter.IsVisible(null, "git").Should().BeFalse();
    }
}
