using GitCommands;
using GitCommands.LeftPanel;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitUI.CommandsDialogs.WorktreeDialog;
using Microsoft.VisualStudio.Threading;
using UICmd = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI.LeftPanel;

internal sealed class WorktreeTree(TreeNode treeNode, IGitUICommandsSource uiCommands) : Tree(treeNode, uiCommands)
{
    internal void Refresh()
    {
        if (!IsAttached)
        {
            return;
        }

        ReloadNodesDetached((_, cancellationToken) => LoadNodesAsync(cancellationToken), getRefs: null!);
    }

    private async Task<Nodes> LoadNodesAsync(CancellationToken token)
    {
        await TaskScheduler.Default;
        token.ThrowIfCancellationRequested();

        return FillWorktreeTree(token);
    }

    private Nodes FillWorktreeTree(CancellationToken token)
    {
        Nodes nodes = new(this);

        foreach (WorktreeTreeNode worktreeNode in WorktreeTreeBuilder.Build(Module.GetWorktrees(), Module.WorkingDir))
        {
            token.ThrowIfCancellationRequested();
            nodes.AddNode(new WorktreeNode(this, worktreeNode.Worktree, worktreeNode.IsCurrent, worktreeNode.DisplayPath));
        }

        return nodes;
    }

    protected override void PostFillTreeViewNode(bool firstTime)
    {
        if (firstTime && TreeViewNode.Nodes.Count > 1)
        {
            TreeViewNode.Expand();
        }
    }

    public void CreateWorktree(IWin32Window owner)
    {
        string? mainWorktreePath = GetMainWorktreePath();
        if (UICommands.Execute(new UICmd.WorktreeCreate(mainWorktreePath), owner))
        {
            Refresh();
        }
    }

    public void PruneWorktrees(IWin32Window owner)
    {
        if (UICommands.Execute(new UICmd.CommandLineProcess(Command: null, "worktree prune"), owner))
        {
            UICommands.RepoChangedNotifier.Notify();
        }
    }

    public void ManageWorktrees(IWin32Window owner)
    {
        using FormManageWorktree form = new(UICommands);
        form.ShowDialog(owner);
        if (form.ShouldRefreshRevisionGrid)
        {
            UICommands.RepoChangedNotifier.Notify();
        }
    }

    private string GetMainWorktreePath()
    {
        foreach (Node node in Nodes)
        {
            if (node is WorktreeNode wt)
            {
                return wt.Worktree.Path;
            }
        }

        return Module.WorkingDir;
    }
}
