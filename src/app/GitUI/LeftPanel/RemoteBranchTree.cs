using GitCommands;
using GitCommands.Git;
using GitCommands.LeftPanel;
using GitCommands.Remotes;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitUI.UserControls.RevisionGrid;
using Microsoft;
using UICmd = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI.LeftPanel;

internal sealed class RemoteBranchTree : BaseRefTree
{
    private readonly IAheadBehindDataProvider? _aheadBehindDataProvider;

    public RemoteBranchTree(TreeNode treeNode, IGitUICommandsSource uiCommands, IAheadBehindDataProvider? aheadBehindDataProvider, ICheckRefs refsSource)
        : base(treeNode, uiCommands, refsSource, RefsFilter.Remotes)
    {
        _aheadBehindDataProvider = aheadBehindDataProvider;
    }

    protected override Nodes FillTree(IReadOnlyList<IGitRef> branches, CancellationToken token)
    {
        Nodes nodes = new(this);

        // More than one local can point to a single remote branch, pick one of them.
        IDictionary<string, AheadBehindData>? aheadBehindData = _aheadBehindDataProvider?.GetData()?.DistinctBy(r => r.Value.RemoteRef).ToDictionary(r => r.Value.RemoteRef, r => r.Value);

        IReadOnlyList<Remote> remotes = ThreadHelper.JoinableTaskFactory.Run(Module.GetRemotesAsync);
        ConfigFileRemoteSettingsManager remotesManager = new(() => Module);

        IReadOnlyList<RefTreeNode> roots = RemoteTreeBuilder.Build(
            branches,
            remotes,
            remotesManager.GetDisabledRemotes(),
            AppSettings.PrioritizedBranchNames,
            AppSettings.PrioritizedRemoteNames,
            aheadBehindData);

        foreach (RefTreeNode root in roots)
        {
            nodes.AddNode(Convert(root));
        }

        return nodes;

        Node Convert(RefTreeNode node)
        {
            token.ThrowIfCancellationRequested();

            Node converted;
            switch (node.Kind)
            {
                case RefTreeNodeKind.RemoteRepo:
                    converted = new RemoteRepoNode(this, node.FullPath, remotesManager, node.Remote!.Value, node.Enabled);
                    break;

                case RefTreeNodeKind.InactiveGroup:
                    converted = new RemoteRepoFolderNode(this, TranslatedStrings.Inactive);
                    break;

                case RefTreeNodeKind.RemoteBranch:
                    RemoteBranchNode remoteBranchNode = new(this, node.ObjectId!.Value, node.FullPath, visible: true);
                    if (node.AheadBehindDisplay is not null)
                    {
                        remoteBranchNode.UpdateAheadBehind(node.AheadBehindDisplay, node.RelatedBranch!);
                    }

                    converted = remoteBranchNode;
                    break;

                default:
                    converted = new BasePathNode(this, node.FullPath);
                    break;
            }

            foreach (RefTreeNode child in node.Children)
            {
                converted.Nodes.AddNode(Convert(child));
            }

            return converted;
        }
    }

    protected override void PostFillTreeViewNode(bool firstTime)
    {
        if (firstTime)
        {
            TreeViewNode.Expand();
        }
    }

    internal void PopupManageRemotesForm(string? remoteName)
    {
        UICommands.Execute(new UICmd.Remotes(remoteName), TreeViewNode.TreeView);
    }

    internal bool FetchAll()
    {
        ((GitUICommands)UICommands).StartPullDialogAndPullImmediately(
            out bool pullCompleted,
            TreeViewNode.TreeView,
            pullAction: GitPullAction.FetchAll);
        return pullCompleted;
    }

    internal bool FetchPruneAll()
    {
        ((GitUICommands)UICommands).StartPullDialogAndPullImmediately(
            out bool pullCompleted,
            TreeViewNode.TreeView,
            pullAction: GitPullAction.FetchPruneAll);
        return pullCompleted;
    }
}
