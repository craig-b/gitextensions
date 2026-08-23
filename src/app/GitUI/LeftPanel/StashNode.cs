using System.Diagnostics;
using GitCommands;
using GitCommands.LeftPanel;
using GitExtensions.Extensibility.Git;
using GitUI.Properties;
using UICmd = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI.LeftPanel;

[DebuggerDisplay("(Tag) FullPath = {FullPath}, Hash = {ObjectId}, Visible: {Visible}")]
internal sealed class StashNode : BaseRevisionNode
{
    public StashNode(Tree tree, StashTreeNode stash, bool visible)
        : base(tree, stash.FullPath, visible)
    {
        ObjectId = stash.ObjectId;
        DisplayName = stash.DisplayName;
        ReflogSelector = stash.ReflogSelector;
    }

    public string DisplayName { get; }
    public string ReflogSelector { get; }

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
        OpenStash(TreeViewNode.TreeView!);
    }

    internal bool OpenStash(IWin32Window owner)
    {
        return UICommands.Execute(new UICmd.Stash(true, ReflogSelector), owner);
    }

    public void ApplyStash(IWin32Window owner)
    {
        UICommands.Execute(new UICmd.StashApply(ReflogSelector), owner);
    }

    public void PopStash(IWin32Window owner)
    {
        UICommands.Execute(new UICmd.StashPop(ReflogSelector), owner);
    }

    public void DropStash(IWin32Window owner)
    {
        using (new WaitCursorScope())
        {
            TaskDialogButton result;
            if (AppSettings.DontConfirmStashDrop)
            {
                result = TaskDialogButton.Yes;
            }
            else
            {
                TaskDialogPage page = new()
                {
                    Text = TranslatedStrings.AreYouSure,
                    Caption = TranslatedStrings.StashDropConfirmTitle,
                    Heading = TranslatedStrings.CannotBeUndone,
                    Buttons = { TaskDialogButton.Yes, TaskDialogButton.No },
                    Icon = TaskDialogIcon.Information,
                    Verification = new TaskDialogVerificationCheckBox
                    {
                        Text = TranslatedStrings.DontShowAgain
                    },
                    SizeToContent = true
                };

                result = TaskDialog.ShowDialog(owner, page);

                if (page.Verification.Checked)
                {
                    AppSettings.DontConfirmStashDrop = true;
                }
            }

            if (result == TaskDialogButton.Yes)
            {
                UICommands.Execute(new UICmd.StashDrop(ReflogSelector), owner);
            }
        }
    }

    public override void ApplyStyle()
    {
        base.ApplyStyle();

        TreeViewNode.ImageKey = TreeViewNode.SelectedImageKey =
            Visible
                ? nameof(Images.Stash)
                : nameof(Images.EyeClosed);
    }

    protected override string DisplayText()
    {
        return DisplayName;
    }
}
