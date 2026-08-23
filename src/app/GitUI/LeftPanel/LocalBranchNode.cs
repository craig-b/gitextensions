using System.Diagnostics;
using GitExtensions.Extensibility.Git;
using GitUI.LeftPanel.Interfaces;
using GitUI.Properties;
using UICmd = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI.LeftPanel;

[DebuggerDisplay("(Local) FullPath = {FullPath}, Hash = {ObjectId}, Visible: {Visible}")]
internal sealed class LocalBranchNode : BaseBranchLeafNode, IGitRefActions, ICanRename, ICanDelete
{
    public LocalBranchNode(Tree tree, in ObjectId objectId, string fullPath, bool isCurrent, bool visible)
        : base(tree, objectId, fullPath, visible, nameof(Images.BranchLocal), nameof(Images.BranchLocalMerged))
    {
        IsCurrent = isCurrent;
    }

    /// <summary>Indicates whether this is the currently checked-out branch.</summary>
    public bool IsCurrent { get; }

    protected override FontStyle GetFontStyle()
        => base.GetFontStyle() | (IsCurrent ? FontStyle.Bold : FontStyle.Regular);

    public override bool Equals(object? obj)
        => base.Equals(obj) && obj is LocalBranchNode;

    public override int GetHashCode()
        => base.GetHashCode();

    internal override void OnDoubleClick()
    {
        Checkout();
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

    internal override void OnRename()
    {
        Rename();
    }

    internal override void OnDelete()
    {
        Delete();
    }

    public bool Checkout()
    {
        return MessageBoxes.ConfirmBranchCheckout(ParentWindow(), FullPath) && UICommands.Execute(new UICmd.CheckoutBranch(Branch: FullPath, Remote: false), ParentWindow());
    }

    public bool CreateBranch()
    {
        return UICommands.Execute(new UICmd.CreateBranchFrom(Branch: FullPath), ParentWindow());
    }

    public bool Merge()
    {
        return UICommands.Execute(new UICmd.MergeBranch(Branch: FullPath), ParentWindow());
    }

    public bool Delete()
    {
        return UICommands.Execute(new UICmd.DeleteBranches([FullPath]), ParentWindow());
    }

    public bool Rename()
    {
        return UICommands.Execute(new UICmd.RenameBranch(Branch: FullPath), ParentWindow());
    }
}
