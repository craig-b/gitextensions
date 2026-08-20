using GitCommands.LeftPanel;

namespace GitCommandsTests.LeftPanel;

public sealed class SubmoduleTreeBuilderTests
{
    private static SubmoduleTreeNode Folder(string name, params SubmoduleTreeNode[] children)
    {
        SubmoduleTreeNode folder = new() { Name = name, IsFolder = true };
        folder.Children.AddRange(children);
        return folder;
    }

    private static SubmoduleTreeNode Leaf(string name) => new() { Name = name };

    [Test]
    public void Compaction_merges_single_child_folder_chains()
    {
        List<SubmoduleTreeNode> nodes = [Folder("extension", Folder("src", Folder("test", Folder("assets"))))];

        SubmoduleTreeBuilder.CompactSingleChildFolderChains(nodes);

        nodes.Single().Name.Should().Be("extension/src/test/assets");
        nodes.Single().Children.Should().BeEmpty();
    }

    [Test]
    public void Compaction_stops_at_a_non_folder_child()
    {
        List<SubmoduleTreeNode> nodes = [Folder("a", Folder("b", Leaf("submodule")))];

        SubmoduleTreeBuilder.CompactSingleChildFolderChains(nodes);

        nodes.Single().Name.Should().Be("a/b");
        nodes.Single().Children.Single().Name.Should().Be("submodule");
    }

    [Test]
    public void Compaction_keeps_folders_with_multiple_children()
    {
        List<SubmoduleTreeNode> nodes = [Folder("Externals", Folder("a"), Folder("b"))];

        SubmoduleTreeBuilder.CompactSingleChildFolderChains(nodes);

        nodes.Single().Name.Should().Be("Externals");
        nodes.Single().Children.Should().HaveCount(2);
    }

    [Test]
    public void Compaction_applies_at_every_depth()
    {
        List<SubmoduleTreeNode> nodes = [Folder("top", Leaf("module"), Folder("x", Folder("y", Leaf("deep"))))];

        SubmoduleTreeBuilder.CompactSingleChildFolderChains(nodes);

        nodes.Single().Children.Should().HaveCount(2);
        nodes.Single().Children[1].Name.Should().Be("x/y");
        nodes.Single().Children[1].Children.Single().Name.Should().Be("deep");
    }
}
