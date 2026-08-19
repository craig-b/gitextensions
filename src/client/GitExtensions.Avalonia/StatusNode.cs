using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GitCommands.FileStatus;
using GitExtensions.Extensibility.Git;

namespace GitExtensions.Avalonia;

/// <summary>
///  The commit screen's tree item: the Avalonia counterpart of a WinForms TreeNode, built by
///  the portable <see cref="StatusTreeSorter"/> through <see cref="StatusNodeAdapter"/>.
/// </summary>
public sealed class StatusNode
{
    public string Text { get; set; } = "";

    public object? Tag { get; set; }

    public StatusNode? Parent { get; set; }

    public ObservableCollection<StatusNode> Children { get; } = [];

    /// <summary>The file behind a leaf (or a merged single-item folder); null for plain folders.</summary>
    public GitItemStatus? Status => Tag as GitItemStatus;

    public IEnumerable<GitItemStatus> DescendantStatuses()
    {
        if (Status is GitItemStatus status)
        {
            yield return status;
        }

        foreach (GitItemStatus descendant in Children.SelectMany(child => child.DescendantStatuses()))
        {
            yield return descendant;
        }
    }

    public static StatusNode BuildTree(IEnumerable<GitItemStatus> statuses)
        => StatusTreeSorter.CreateTreeSortedByPath(
            new StatusNodeAdapter(),
            statuses,
            flat: false,
            mergeSingleItemsWithFolder: true,
            status => new StatusNode { Text = status.Name, Tag = status });

    private sealed class StatusNodeAdapter : IStatusTreeAdapter<StatusNode>
    {
        public StatusNode CreateFolderNode(RelativePath path) => new() { Text = path.Value, Tag = path };

        public object? GetTag(StatusNode node) => node.Tag;

        public string GetText(StatusNode node) => node.Text;

        public void SetText(StatusNode node, string text) => node.Text = text;

        public StatusNode? GetParent(StatusNode node) => node.Parent;

        public int GetChildCount(StatusNode node) => node.Children.Count;

        public StatusNode GetChild(StatusNode node, int index) => node.Children[index];

        public void AddChild(StatusNode parent, StatusNode child)
        {
            child.Parent = parent;
            parent.Children.Add(child);
        }

        public void InsertChild(StatusNode parent, int index, StatusNode child)
        {
            child.Parent = parent;
            parent.Children.Insert(index, child);
        }

        public void RemoveChild(StatusNode parent, StatusNode child)
        {
            parent.Children.Remove(child);
            child.Parent = null;
        }

        public int IndexOfChild(StatusNode parent, StatusNode child) => parent.Children.IndexOf(child);

        public void ClearChildren(StatusNode node) => node.Children.Clear();

        public void AdoptSingleItemPresentation(StatusNode folder, StatusNode singleItem)
        {
            folder.Tag = singleItem.Tag;
            folder.Text = singleItem.Text;
        }
    }
}
