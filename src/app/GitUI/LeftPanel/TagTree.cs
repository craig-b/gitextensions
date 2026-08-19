using GitCommands.LeftPanel;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitUI.UserControls.RevisionGrid;

namespace GitUI.LeftPanel;

internal sealed class TagTree : BaseRefTree
{
    public TagTree(TreeNode treeNode, IGitUICommandsSource uiCommands, ICheckRefs refsSource)
        : base(treeNode, uiCommands, refsSource, RefsFilter.Tags)
    {
    }

    protected override Nodes FillTree(IReadOnlyList<IGitRef> tags, CancellationToken token)
    {
        Nodes nodes = new(this);

        // The portable builder folds the hierarchy with the same pathToNode semantics
        // CreateRootNode had; tags have no priority setting.
        IReadOnlyList<RefTreeNode> roots = RefTreeBuilder.Build(tags, tag => tag.Name, prioritySetting: "");
        foreach (RefTreeNode root in roots)
        {
            nodes.AddNode(Convert(root));
        }

        return nodes;

        BaseRevisionNode Convert(RefTreeNode node)
        {
            token.ThrowIfCancellationRequested();

            if (node.ObjectId is not ObjectId objectId)
            {
                BasePathNode folder = new(this, node.FullPath);
                foreach (RefTreeNode child in node.Children)
                {
                    folder.Nodes.AddNode(Convert(child));
                }

                return folder;
            }

            return new TagNode(this, objectId, node.FullPath, visible: true);
        }
    }

    protected override void PostFillTreeViewNode(bool firstTime)
    {
        if (firstTime)
        {
            TreeViewNode.Collapse();
        }
    }
}
