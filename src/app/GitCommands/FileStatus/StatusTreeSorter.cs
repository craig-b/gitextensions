using GitExtensions.Extensibility.Git;
using Microsoft;

namespace GitCommands.FileStatus;

/// <summary>
///  The node operations <see cref="StatusTreeSorter"/> needs from a tree view's node type -
///  implemented over WinForms' TreeNode today, over any other client's tree tomorrow. Folder
///  nodes carry their <see cref="RelativePath"/> as the tag.
/// </summary>
public interface IStatusTreeAdapter<TNode>
{
    /// <summary>Creates a folder node: text = the path value, tag = the path.</summary>
    TNode CreateFolderNode(RelativePath path);

    object? GetTag(TNode node);

    string GetText(TNode node);

    void SetText(TNode node, string text);

    TNode? GetParent(TNode node);

    int GetChildCount(TNode node);

    TNode GetChild(TNode node, int index);

    void AddChild(TNode parent, TNode child);

    void InsertChild(TNode parent, int index, TNode child);

    void RemoveChild(TNode parent, TNode child);

    int IndexOfChild(TNode parent, TNode child);

    void ClearChildren(TNode node);

    /// <summary>
    ///  A folder whose only child is a leaf adopts that leaf's identity: its text, tag, and
    ///  whatever presentation (icons) the view attaches to leaves.
    /// </summary>
    void AdoptSingleItemPresentation(TNode folder, TNode singleItem);
}

/// <summary>
///  Builds the path-sorted status tree the file-status list displays (extracted from
///  FileStatusList.StatusSorter): folders sort before files at each level, sibling
///  folders are never split apart, single-leaf folders optionally merge with their leaf, and
///  each node's text drops its parent folder prefix. The algorithm is verbatim; it drives an
///  <see cref="IStatusTreeAdapter{TNode}"/> instead of WinForms TreeNodes.
/// </summary>
public static class StatusTreeSorter
{
    public static TNode CreateTreeSortedByPath<TNode>(
        IStatusTreeAdapter<TNode> tree,
        IEnumerable<GitItemStatus> statuses,
        bool flat,
        bool mergeSingleItemsWithFolder,
        Func<GitItemStatus, TNode> createNode)
        where TNode : class
    {
        TNode root = tree.CreateFolderNode(RelativePath.From(""));

        TNode parent = root;
        foreach (GitItemStatus status in statuses.OrderBy(s => s, new PathFirstComparer()))
        {
            parent = flat ? root : GetOrCreateParent(tree, parent, status.Path, root);
            TNode leaf = createNode(status);
            tree.AddChild(parent, leaf);
        }

        if (!flat)
        {
            foreach (TNode node in Items(root))
            {
                RemoveParentPath(node);
            }
        }

        return root;

        // Self plus descendants, depth first, reading children live: a node whose children were
        // just merged away is never descended into (same contract as WinForms' Items()).
        IEnumerable<TNode> Items(TNode node)
        {
            yield return node;

            for (int index = 0; index < tree.GetChildCount(node); index++)
            {
                foreach (TNode descendant in Items(tree.GetChild(node, index)))
                {
                    yield return descendant;
                }
            }
        }

        void RemoveParentPath(TNode node)
        {
            if (mergeSingleItemsWithFolder
                && tree.GetChildCount(node) == 1
                && tree.GetChildCount(tree.GetChild(node, 0)) == 0
                && tree.GetParent(node) is not null)
            {
                TNode singleItem = tree.GetChild(node, 0);
                tree.ClearChildren(node);
                tree.AdoptSingleItemPresentation(node, singleItem);
            }

            if (tree.GetParent(node) is TNode nodeParent && tree.GetTag(nodeParent) is RelativePath parentPath)
            {
                if (parentPath.Length > 0 && tree.GetText(node).StartsWith(parentPath.Value))
                {
                    tree.SetText(node, tree.GetText(node)[(parentPath.Length + 1)..]);
                }
            }
        }
    }

    public static RelativePath GetCommonPath(RelativePath relativePathA, RelativePath relativePathB)
    {
        string a = $"{relativePathA}/";
        string b = $"{relativePathB}/";
        for (int commonEnd = 0; ; ++commonEnd)
        {
            if (commonEnd >= a.Length || commonEnd >= b.Length || a[commonEnd] != b[commonEnd])
            {
                // Revert possible partial match
                while (commonEnd > 0 && a[--commonEnd] != '/')
                {
                }

                return RelativePath.From(a[..commonEnd]);
            }
        }
    }

    private static TNode GetOrCreateParent<TNode>(IStatusTreeAdapter<TNode> tree, TNode previousParent, RelativePath currentPath, TNode root)
        where TNode : class
    {
        Validates.NotNull(tree.GetTag(previousParent));
        RelativePath previousPath = (RelativePath)tree.GetTag(previousParent)!;
        if (previousPath == currentPath)
        {
            return previousParent;
        }

        RelativePath commonPath = GetCommonPath(previousPath, currentPath);
        TNode commonParent = GetOrCreateCommonParent();
        if (currentPath == commonPath)
        {
            return commonParent;
        }

        TNode parent = tree.CreateFolderNode(currentPath);
        tree.AddChild(commonParent, parent);
        return parent;

        TNode GetOrCreateCommonParent()
        {
            if (commonPath.Length == 0)
            {
                return root;
            }

            TNode splitCandidate = previousParent;
            RelativePath splitCandidatePath = previousPath;
            while (tree.GetParent(splitCandidate) is TNode candidateParent
                && tree.GetTag(candidateParent) is RelativePath path
                && path.Value.StartsWith(commonPath.Value))
            {
                splitCandidate = candidateParent;
                splitCandidatePath = path;
            }

            return splitCandidatePath == commonPath
                ? splitCandidate
                : Split(tree, splitCandidate, commonPath);
        }
    }

    private static TNode Split<TNode>(IStatusTreeAdapter<TNode> tree, TNode subNode, RelativePath commonPath)
        where TNode : class
    {
        TNode parentNode = tree.GetParent(subNode) ?? throw new ArgumentNullException($"{nameof(subNode)}.Parent");
        int index = tree.IndexOfChild(parentNode, subNode);
        tree.RemoveChild(parentNode, subNode);
        TNode commonFolderNode = tree.CreateFolderNode(commonPath);
        tree.AddChild(commonFolderNode, subNode);
        tree.InsertChild(parentNode, index, commonFolderNode);
        return commonFolderNode;
    }

    public sealed class PathFirstComparer : IComparer<GitItemStatus>
    {
        public int Compare(GitItemStatus? l, GitItemStatus? r)
            => (l, r) switch
            {
                (null, null) => 0,
                (_, null) => -1,
                (null, _) => 1,
                _ => CompareNonNull(l, r)
            };

        private static int CompareNonNull(GitItemStatus l, GitItemStatus r)
        {
            int pathComparison = (l.Path.Value, r.Path.Value) switch
            {
                ("", "") => 0,
                (_, "") => -1,
                ("", _) => 1,
                _ => ComparePath(l.Path.Value.AsSpan(), r.Path.Value.AsSpan())
            };

            return pathComparison switch
            {
                -1 => StartsWith(r.Path, l.Path) ? 1 : -1,
                1 => StartsWith(l.Path, r.Path) ? -1 : 1,
                _ => StringComparer.InvariantCulture.Compare(l.Name, r.Name)
            };

            static int ComparePath(ReadOnlySpan<char> l, ReadOnlySpan<char> r)
            {
                if (l.IsEmpty || r.IsEmpty)
                {
                    return l.IsEmpty && r.IsEmpty ? 0 : l.IsEmpty ? -1 : 1;
                }

                Split(l, out ReadOnlySpan<char> topL, out ReadOnlySpan<char> subL);
                Split(r, out ReadOnlySpan<char> topR, out ReadOnlySpan<char> subR);
                return topL.CompareTo(topR, StringComparison.InvariantCulture) switch
                {
                    -1 => -1,
                    +1 => +1,
                    _ => ComparePath(subL, subR)
                };

                static void Split(ReadOnlySpan<char> path, out ReadOnlySpan<char> top, out ReadOnlySpan<char> sub)
                {
                    int separatorIndex = path.IndexOf('/');
                    if (separatorIndex == -1)
                    {
                        top = path;
                        sub = ReadOnlySpan<char>.Empty;
                        return;
                    }

                    top = path[..separatorIndex];
                    sub = path[(separatorIndex + 1)..];
                }
            }

            static bool StartsWith(RelativePath longPath, RelativePath shortPath)
            {
                return longPath.Value.StartsWith(shortPath.Value, StringComparison.InvariantCulture)
                    && longPath.Value[shortPath.Length] == '/';
            }
        }
    }
}
