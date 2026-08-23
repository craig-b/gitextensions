using GitCommands.Git;
using GitExtensions.Extensibility.Git;

namespace GitCommands.LeftPanel;

/// <summary>
///  Builds the remotes hierarchy the left panel shows: each configured
///  remote is a root (in prioritized order - alphabetical, then the remote priority regexes),
///  its branches fold into path folders beneath it, remotes without branches still appear,
///  branches of unconfigured remotes are dropped, and disabled remotes gather under a single
///  trailing group node whose caption the views localize.
/// </summary>
public static class RemoteTreeBuilder
{
    public static IReadOnlyList<RefTreeNode> Build(
        IReadOnlyList<IGitRef> branches,
        IReadOnlyList<Remote> remotes,
        IReadOnlyList<Remote> disabledRemotes,
        string branchPrioritySetting,
        string remotePrioritySetting,
        IDictionary<string, AheadBehindData>? aheadBehindByRemoteRef = null)
    {
        Dictionary<string, Remote> remoteByName = remotes.ToDictionary(remote => remote.Name);
        Dictionary<string, RefTreeNode> pathToNode = [];
        List<RefTreeNode> enabledRemoteRoots = [];

        foreach (IGitRef branch in RefPriorityOrder.OrderByPriority([.. branches], gitRef => gitRef.LocalName, branchPrioritySetting))
        {
            if (branch.ObjectId.IsZero)
            {
                throw new InvalidOperationException($"Branch '{branch.Name}' has no ObjectId.");
            }

            string remoteName = branch.Name.SubstringUntil('/');
            if (!remoteByName.TryGetValue(remoteName, out Remote remote))
            {
                // a remote-tracking branch whose remote is not configured is not shown
                continue;
            }

            string[] parts = branch.Name.Split('/');
            List<RefTreeNode> siblings = enabledRemoteRoots;
            string path = "";
            for (int i = 0; i < parts.Length - 1; i++)
            {
                path = path.Length == 0 ? parts[i] : $"{path}/{parts[i]}";
                if (!pathToNode.TryGetValue(path, out RefTreeNode? folder))
                {
                    folder = i == 0
                        ? new RefTreeNode { Name = parts[i], FullPath = path, Kind = RefTreeNodeKind.RemoteRepo, Remote = remote }
                        : new RefTreeNode { Name = parts[i], FullPath = path };
                    pathToNode.Add(path, folder);
                    siblings.Add(folder);
                }

                siblings = folder.Children;
            }

            RefTreeNode leaf = new()
            {
                Name = parts[^1],
                FullPath = branch.Name,
                ObjectId = branch.ObjectId,
                Kind = RefTreeNodeKind.RemoteBranch,
            };

            if (aheadBehindByRemoteRef?.TryGetValue(branch.CompleteName, out AheadBehindData aheadBehind) is true)
            {
                leaf.AheadBehindDisplay = aheadBehind.ToDisplay(reverse: true);
                leaf.RelatedBranch = $"{GitRefName.RefsHeadsPrefix}{aheadBehind.Branch}";
            }

            siblings.Add(leaf);
        }

        // remotes that have no branches still get a (childless) root
        HashSet<string> remotesWithBranches = [.. branches.Select(branch => branch.Name.SubstringUntil('/'))];
        foreach (Remote remote in remotes)
        {
            if (!remotesWithBranches.Contains(remote.Name))
            {
                enabledRemoteRoots.Add(new RefTreeNode { Name = remote.Name, FullPath = remote.Name, Kind = RefTreeNodeKind.RemoteRepo, Remote = remote });
            }
        }

        List<RefTreeNode> roots = [.. PrioritizedRemotes(enabledRemoteRoots, remotePrioritySetting)];

        if (disabledRemotes.Count > 0)
        {
            RefTreeNode inactiveGroup = new() { Name = "", FullPath = "", Kind = RefTreeNodeKind.InactiveGroup };
            List<RefTreeNode> disabledNodes = [.. disabledRemotes.Select(remote =>
                new RefTreeNode { Name = remote.Name, FullPath = remote.Name, Kind = RefTreeNodeKind.RemoteRepo, Remote = remote, Enabled = false })];

            inactiveGroup.Children.AddRange(PrioritizedRemotes(disabledNodes, remotePrioritySetting));
            roots.Add(inactiveGroup);
        }

        return roots;

        static IEnumerable<RefTreeNode> PrioritizedRemotes(List<RefTreeNode> remoteNodes, string prioritySetting)
            => RefPriorityOrder.OrderByPriority(
                [.. remoteNodes.OrderBy(node => node.FullPath)],
                node => node.FullPath,
                prioritySetting);
    }
}
