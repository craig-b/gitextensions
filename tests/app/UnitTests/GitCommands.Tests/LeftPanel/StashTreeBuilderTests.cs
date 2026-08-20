using GitCommands.LeftPanel;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;

namespace GitCommandsTests.LeftPanel;

public class StashTreeBuilderTests
{
    [Test]
    public void Keeps_git_order_and_applies_the_naming_rules()
    {
        GitRevision first = new(ObjectId.Parse("aaaa000000000000000000000000000000000000"))
        {
            ReflogSelector = "stash@{0}",
            Subject = "WIP on main",
        };
        GitRevision second = new(ObjectId.Parse("bbbb000000000000000000000000000000000000"))
        {
            ReflogSelector = "stash@{1}",
            Subject = "On feature: half-done",
        };

        IReadOnlyList<StashTreeNode> nodes = StashTreeBuilder.Build([first, second]);

        nodes.Should().HaveCount(2);
        nodes[0].FullPath.Should().Be("stash@{0}");
        nodes[0].DisplayName.Should().Be("stash@{0}: WIP on main");
        nodes[0].ObjectId.Should().Be(first.ObjectId);
        nodes[1].DisplayName.Should().Be("stash@{1}: On feature: half-done");
    }

    [Test]
    public void Refs_prefix_is_stripped_from_the_path()
    {
        GitRevision stash = new(ObjectId.Parse("cccc000000000000000000000000000000000000"))
        {
            ReflogSelector = "refs/stash@{0}",
            Subject = "subject",
        };

        IReadOnlyList<StashTreeNode> nodes = StashTreeBuilder.Build([stash]);

        nodes.Single().FullPath.Should().Be("stash@{0}");
        nodes.Single().DisplayName.Should().Be("@{0}: subject");
    }
}
