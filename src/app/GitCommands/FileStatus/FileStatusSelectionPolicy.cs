namespace GitCommands.FileStatus;

/// <summary>
///  The file list's selection policies, generic over the view's node
///  type: which item to re-select after a reload, and previous/next navigation over the
///  flattened item order.
/// </summary>
public static class FileStatusSelectionPolicy
{
    /// <summary>
    ///  The item to select after the list reloads: the first file item after the last
    ///  currently-selected node, else the first file item overall.
    /// </summary>
    public static TItem? FindNextItemToSelect<TNode, TItem>(
        IReadOnlyCollection<TNode> flattenedNodes,
        bool hasSelection,
        Func<TNode, bool> isSelected,
        Func<TNode, TItem?> getFileItem)
        where TItem : class
    {
        if (hasSelection)
        {
            bool selectionPassed = false;
            foreach (TNode node in flattenedNodes)
            {
                if (isSelected(node))
                {
                    selectionPassed = true;
                    continue;
                }

                if (selectionPassed && getFileItem(node) is TItem item)
                {
                    return item;
                }
            }
        }

        return flattenedNodes.Select(getFileItem).FirstOrDefault(item => item is not null);
    }

    /// <summary>
    ///  The searchable node before <paramref name="currentNode"/> in the flattened order,
    ///  or null at the start. Throws if <paramref name="currentNode"/> is not in the sequence.
    /// </summary>
    public static TNode? FindPreviousItem<TNode>(IEnumerable<TNode> flattenedNodes, TNode currentNode, Func<TNode, bool> isSearchable)
        where TNode : class
    {
        TNode? previousNode = null;
        foreach (TNode node in flattenedNodes)
        {
            if (node == currentNode)
            {
                return previousNode;
            }

            if (isSearchable(node))
            {
                previousNode = node;
            }
        }

        throw new ArgumentException(@$"{nameof(currentNode)} ""{currentNode}"" is not an item of the flattened sequence!");
    }

    /// <summary>
    ///  The searchable node after <paramref name="currentNode"/> in the flattened order,
    ///  or null at the end.
    /// </summary>
    public static TNode? FindNextItem<TNode>(IEnumerable<TNode> flattenedNodes, TNode currentNode, Func<TNode, bool> isSearchable)
        where TNode : class
    {
        bool currentNodeFound = false;
        foreach (TNode node in flattenedNodes)
        {
            if (node == currentNode)
            {
                currentNodeFound = true;
                continue;
            }

            if (currentNodeFound && isSearchable(node))
            {
                return node;
            }
        }

        return null;
    }
}
