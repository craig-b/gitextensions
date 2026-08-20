using GitExtensions.Extensibility.Git;

namespace GitCommands.LeftPanel;

public enum RefTreeNodeKind
{
    Folder,
    LocalBranch,
    RemoteBranch,
    Tag,

    /// <summary>A configured remote repository (the first path segment of its branches).</summary>
    RemoteRepo,

    /// <summary>The group holding disabled remotes; views supply its localized caption.</summary>
    InactiveGroup,
}

/// <summary>A node of the left panel's ref hierarchy: a folder, or a ref leaf carrying its ObjectId.</summary>
public sealed class RefTreeNode
{
    public required string Name { get; init; }

    public required string FullPath { get; init; }

    /// <summary>The ref's commit for leaves; null for path folders.</summary>
    public ObjectId? ObjectId { get; init; }

    public bool IsCurrent { get; init; }

    public RefTreeNodeKind Kind { get; init; } = RefTreeNodeKind.Folder;

    /// <summary>The remote, on <see cref="RefTreeNodeKind.RemoteRepo"/> nodes.</summary>
    public Remote? Remote { get; init; }

    /// <summary>Whether the remote is enabled, on <see cref="RefTreeNodeKind.RemoteRepo"/> nodes.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The "n↑ m↓" decoration on branch leaves that track (or are tracked by) another branch.</summary>
    public string? AheadBehindDisplay { get; set; }

    /// <summary>The counterpart branch the ahead/behind decoration relates to.</summary>
    public string? RelatedBranch { get; set; }

    public List<RefTreeNode> Children { get; } = [];
}

/// <summary>
///  Builds the branch/tag hierarchy the left panel shows: refs arrive in
///  priority order (see <see cref="RefPriorityOrder"/>), each name splits on '/', and folders
///  are created on first encounter - the same fold semantics as the WinForms panel's
///  CreateRootNode/pathToNode walk, so "develop/features/x" and "develop/issues/y" share the
///  "develop" folder and folder order follows the first ref that needed the folder.
/// </summary>
public static class RefTreeBuilder
{
    public static IReadOnlyList<RefTreeNode> Build(IEnumerable<IGitRef> refs, Func<IGitRef, string> getDisplayName, string prioritySetting, string? currentRefName = null, RefTreeNodeKind leafKind = RefTreeNodeKind.LocalBranch)
    {
        List<IGitRef> ordered = [.. refs];
        List<RefTreeNode> roots = [];
        Dictionary<string, RefTreeNode> pathToNode = [];

        foreach (IGitRef gitRef in RefPriorityOrder.OrderByPriority(ordered, getDisplayName, prioritySetting))
        {
            string fullPath = getDisplayName(gitRef);
            string[] parts = fullPath.Split('/');

            List<RefTreeNode> siblings = roots;
            string path = "";
            for (int i = 0; i < parts.Length - 1; i++)
            {
                path = path.Length == 0 ? parts[i] : $"{path}/{parts[i]}";
                if (!pathToNode.TryGetValue(path, out RefTreeNode? folder))
                {
                    folder = new RefTreeNode { Name = parts[i], FullPath = path };
                    pathToNode.Add(path, folder);
                    siblings.Add(folder);
                }

                siblings = folder.Children;
            }

            siblings.Add(new RefTreeNode
            {
                Name = parts[^1],
                FullPath = fullPath,
                ObjectId = gitRef.ObjectId,
                IsCurrent = fullPath == currentRefName,
                Kind = leafKind,
            });
        }

        return roots;
    }
}
