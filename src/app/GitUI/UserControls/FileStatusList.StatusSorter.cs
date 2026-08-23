using GitCommands.FileStatus;
using GitExtensions.Extensibility.Git;
using GitUI.UserControls;

namespace GitUI;

partial class FileStatusList
{
    /// <summary>
    ///  The WinForms face of the portable <see cref="StatusTreeSorter"/>: a TreeNode adapter
    ///  plus the leaf-presentation copy for merged single-item folders.
    /// </summary>
    internal sealed class StatusSorter : IStatusSorter
    {
        public TreeNode CreateTreeSortedByPath(IEnumerable<GitItemStatus> statuses, bool flat, bool mergeSingleItemsWithFolder, Func<GitItemStatus, TreeNode> createNode)
            => StatusTreeSorter.CreateTreeSortedByPath(new TreeNodeAdapter(), statuses, flat, mergeSingleItemsWithFolder, createNode);

        private sealed class TreeNodeAdapter : IStatusTreeAdapter<TreeNode>
        {
            public TreeNode CreateFolderNode(RelativePath path) => new(text: path.Value) { Tag = path };

            public object? GetTag(TreeNode node) => node.Tag;

            public string GetText(TreeNode node) => node.Text;

            public void SetText(TreeNode node, string text) => node.Text = text;

            public TreeNode? GetParent(TreeNode node) => node.Parent;

            public int GetChildCount(TreeNode node) => node.Nodes.Count;

            public TreeNode GetChild(TreeNode node, int index) => node.Nodes[index];

            public void AddChild(TreeNode parent, TreeNode child) => parent.Nodes.Add(child);

            public void InsertChild(TreeNode parent, int index, TreeNode child) => parent.Nodes.Insert(index, child);

            public void RemoveChild(TreeNode parent, TreeNode child) => parent.Nodes.Remove(child);

            public int IndexOfChild(TreeNode parent, TreeNode child) => parent.Nodes.IndexOf(child);

            public void ClearChildren(TreeNode node) => node.Nodes.Clear();

            public void AdoptSingleItemPresentation(TreeNode folder, TreeNode singleItem)
            {
                folder.ImageIndex = singleItem.ImageIndex;
                folder.SelectedImageIndex = singleItem.SelectedImageIndex;
                folder.StateImageIndex = 0;
                folder.Tag = singleItem.Tag;
                folder.Text = singleItem.Text;
            }
        }

        internal static class TestAccessor
        {
            public static string GetCommonPath(string a, string b) => StatusTreeSorter.GetCommonPath(RelativePath.From(a), RelativePath.From(b)).Value;
            public static int Compare(string l, string r) => new StatusTreeSorter.PathFirstComparer().Compare(new GitItemStatus(l), new GitItemStatus(r));
        }
    }
}
