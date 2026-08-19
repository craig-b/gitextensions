using GitCommands.FileStatus;
using GitExtensions.Extensibility.Git;

namespace GitCommandsTests.FileStatus;

/// <summary>
///  Portable tests for <see cref="StatusTreeSorter"/> through a fake node type. The WinForms
///  Verify-snapshot tests (GitUI.Tests' FileStatusListSorterTests) still cover the TreeNode
///  adapter on Windows; these cover the algorithm itself on the Linux probe.
/// </summary>
public class StatusTreeSorterTests
{
    private sealed class FakeNode
    {
        public string Text = "";
        public object? Tag;
        public FakeNode? Parent;
        public List<FakeNode> Children = [];
        public bool AdoptedLeafPresentation;
    }

    private sealed class FakeAdapter : IStatusTreeAdapter<FakeNode>
    {
        public FakeNode CreateFolderNode(RelativePath path) => new() { Text = path.Value, Tag = path };
        public object? GetTag(FakeNode node) => node.Tag;
        public string GetText(FakeNode node) => node.Text;
        public void SetText(FakeNode node, string text) => node.Text = text;
        public FakeNode? GetParent(FakeNode node) => node.Parent;
        public int GetChildCount(FakeNode node) => node.Children.Count;
        public FakeNode GetChild(FakeNode node, int index) => node.Children[index];

        public void AddChild(FakeNode parent, FakeNode child)
        {
            child.Parent = parent;
            parent.Children.Add(child);
        }

        public void InsertChild(FakeNode parent, int index, FakeNode child)
        {
            child.Parent = parent;
            parent.Children.Insert(index, child);
        }

        public void RemoveChild(FakeNode parent, FakeNode child)
        {
            parent.Children.Remove(child);
            child.Parent = null;
        }

        public int IndexOfChild(FakeNode parent, FakeNode child) => parent.Children.IndexOf(child);

        public void ClearChildren(FakeNode node) => node.Children.Clear();

        public void AdoptSingleItemPresentation(FakeNode folder, FakeNode singleItem)
        {
            folder.Tag = singleItem.Tag;
            folder.Text = singleItem.Text;
            folder.AdoptedLeafPresentation = true;
        }
    }

    private static FakeNode Sort(string[] names, bool flat = false, bool merge = false)
        => StatusTreeSorter.CreateTreeSortedByPath(
            new FakeAdapter(),
            names.Select(name => new GitItemStatus(name)),
            flat,
            merge,
            status => new FakeNode { Text = status.Name!, Tag = status });

    private static string Render(FakeNode node)
        => node.Children.Count == 0
            ? node.Text
            : $"{node.Text}({string.Join(",", node.Children.Select(Render))})";

    [Test]
    public void Folders_sort_before_files_at_each_level()
    {
        FakeNode root = Sort(["zzz_file", "a/file", "b/file"]);

        Render(root).Should().Be("(a(file),b(file),zzz_file)");
    }

    [Test]
    public void Flat_mode_keeps_the_sort_but_builds_no_folders()
    {
        FakeNode root = Sort(["zzz_file", "a/file"], flat: true);

        Render(root).Should().Be("(a/file,zzz_file)");
    }

    [Test]
    public void Sibling_folders_are_not_split()
    {
        FakeNode root = Sort(["core/c.1", "core/c.2", "core.dot/cd.3", "core/api/c_a.0"]);

        Render(root).Should().Be("(core(api(c_a.0),c.1,c.2),core.dot(cd.3))");
    }

    [Test]
    public void Path_comparison_does_not_compare_the_separator()
    {
        StatusTreeSorter.PathFirstComparer comparer = new();
        comparer.Compare(new GitItemStatus("dir/sub/file"), new GitItemStatus("dir.ext/file")).Should().Be(-1);
        comparer.Compare(new GitItemStatus("dir.ext/file"), new GitItemStatus("dir/sub/file")).Should().Be(1);
    }

    [Test]
    public void Single_leaf_folders_merge_with_their_leaf_when_requested()
    {
        FakeNode root = Sort(["1/2/file12", "1/3/file13"], merge: true);

        // The subfolders 2 and 3 each hold one leaf and merge with it, displaying as a single
        // folder-plus-file row; folder 1 keeps two children.
        Render(root).Should().Be("(1(2/file12,3/file13))");
        root.Children[0].Children.Should().OnlyContain(node => node.AdoptedLeafPresentation);
    }

    [Test]
    public void The_root_never_merges_with_a_single_leaf()
    {
        FakeNode root = Sort(["root_file"], merge: true);

        root.AdoptedLeafPresentation.Should().BeFalse();
        Render(root).Should().Be("(root_file)");
    }

    [Test]
    public void Node_text_drops_the_parent_folder_prefix()
    {
        FakeNode root = Sort(["a/b/file"]);

        FakeNode folder = root.Children.Single();
        folder.Text.Should().Be("a/b");
        folder.Children.Single().Text.Should().Be("file");
    }

    [TestCase("", "", "")]
    [TestCase("a", "a", "a")]
    [TestCase("a", "b", "")]
    [TestCase("a", "ab", "")]
    [TestCase("a", "a/b", "a")]
    [TestCase("a", "a/b/c", "a")]
    [TestCase("a/b", "a/bc", "a")]
    [TestCase("a/b", "a/b", "a/b")]
    [TestCase("a/b", "a/b/c", "a/b")]
    [TestCase("a/b/cc", "a/b/ccd", "a/b")]
    [TestCase("a/b/cc", "a/b/cc/de", "a/b/cc")]
    public void GetCommonPath(string a, string b, string expected)
    {
        StatusTreeSorter.GetCommonPath(RelativePath.From(a), RelativePath.From(b)).Value.Should().Be(expected);
        StatusTreeSorter.GetCommonPath(RelativePath.From(b), RelativePath.From(a)).Value.Should().Be(expected);
    }
}
