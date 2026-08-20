using GitUI.Properties;

namespace GitUI.LeftPanel;

// Top-level nodes used to group SubmoduleNodes
internal sealed class SubmoduleFolderNode(Tree tree, string name) : Node(tree)
{
    protected override string DisplayText()
    {
        return name;
    }

    public override void ApplyStyle()
    {
        base.ApplyStyle();
        TreeViewNode.ImageKey = TreeViewNode.SelectedImageKey = nameof(Images.FolderClosed);
    }

    protected override FontStyle GetFontStyle()
    {
        return base.GetFontStyle() | FontStyle.Italic;
    }
}
