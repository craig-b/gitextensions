using GitCommands.LeftPanel;

namespace GitCommandsTests.LeftPanel;

public sealed class LeftPanelSectionOrderTests
{
    [Test]
    public void Empty_or_null_yields_the_default_order()
    {
        LeftPanelSectionOrder.Parse(null).Should().Equal(LeftPanelSectionOrder.DefaultOrder);
        LeftPanelSectionOrder.Parse("").Should().Equal(LeftPanelSectionOrder.DefaultOrder);
    }

    [Test]
    public void Partial_text_promotes_named_sections_and_appends_the_rest()
    {
        LeftPanelSectionOrder.Parse("tags, WORKTREES").Should().Equal(
            LeftPanelSection.Tags,
            LeftPanelSection.Worktrees,
            LeftPanelSection.Branches,
            LeftPanelSection.Remotes,
            LeftPanelSection.Stashes,
            LeftPanelSection.Submodules);
    }

    [Test]
    public void Unknown_names_and_duplicates_are_ignored_so_no_section_can_be_lost()
    {
        IReadOnlyList<LeftPanelSection> parsed = LeftPanelSectionOrder.Parse("Nonsense; Tags,, Tags\nRemotes");

        parsed.Should().StartWith([LeftPanelSection.Tags, LeftPanelSection.Remotes]);
        parsed.Should().BeEquivalentTo(LeftPanelSectionOrder.DefaultOrder);
    }

    [Test]
    public void Round_trips_through_ToText()
    {
        IReadOnlyList<LeftPanelSection> order = LeftPanelSectionOrder.Parse("Stashes, Branches");
        LeftPanelSectionOrder.Parse(LeftPanelSectionOrder.ToText(order)).Should().Equal(order);
    }
}
