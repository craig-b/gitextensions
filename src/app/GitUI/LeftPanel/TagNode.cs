using System.Diagnostics;
using GitExtensions.Extensibility.Git;
using GitUI.LeftPanel.Interfaces;
using GitUI.Properties;
using UICmd = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI.LeftPanel;

[DebuggerDisplay("(Tag) FullPath = {FullPath}, Hash = {ObjectId}, Visible: {Visible}")]
internal sealed class TagNode : BaseRevisionNode, IGitRefActions, ICanDelete
{
    public TagNode(Tree tree, in ObjectId objectId, string fullPath, bool visible)
        : base(tree, fullPath, visible)
    {
        ObjectId = objectId;
    }

    internal override void OnSelected()
    {
        if (Tree.IgnoreSelectionChangedEvent)
        {
            return;
        }

        base.OnSelected();
        SelectRevision();
    }

    internal override void OnDoubleClick()
    {
        CreateBranch();
    }

    internal override void OnDelete()
    {
        Delete();
    }

    public bool CreateBranch()
    {
        return UICommands.Execute(new UICmd.CreateBranch(ObjectId), TreeViewNode.TreeView);
    }

    public bool Delete()
    {
        return UICommands.Execute(new UICmd.DeleteTag(FullPath), TreeViewNode.TreeView);
    }

    public bool Merge()
    {
        return UICommands.Execute(new UICmd.MergeBranch(FullPath), TreeViewNode.TreeView);
    }

    public override void ApplyStyle()
    {
        base.ApplyStyle();

        TreeViewNode.ImageKey = TreeViewNode.SelectedImageKey =
            Visible
                ? nameof(Images.TagHorizontal)
                : nameof(Images.EyeClosed);
    }

    public bool Checkout()
    {
        return UICommands.Execute(new UICmd.CheckoutRevision(FullPath), TreeViewNode.TreeView);
    }
}
