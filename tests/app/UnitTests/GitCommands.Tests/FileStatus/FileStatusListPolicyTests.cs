using GitCommands.FileStatus;

namespace GitCommandsTests.FileStatus;

public sealed class FileStatusListPolicyTests
{
    private sealed record Node(string Name, string? Item = null, bool Searchable = true)
    {
        public override string ToString() => Name;
    }

    [Test]
    public void Next_item_to_select_is_the_first_file_item_after_the_selection()
    {
        Node[] nodes = [new("group"), new("a", "a"), new("b", "b"), new("c", "c")];

        string? next = FileStatusSelectionPolicy.FindNextItemToSelect(
            nodes, hasSelection: true, node => node.Name == "b", node => node.Item);

        next.Should().Be("c");
    }

    [Test]
    public void Next_item_to_select_falls_back_to_the_first_item_when_selection_is_last()
    {
        Node[] nodes = [new("group"), new("a", "a"), new("b", "b")];

        string? next = FileStatusSelectionPolicy.FindNextItemToSelect(
            nodes, hasSelection: true, node => node.Name == "b", node => node.Item);

        next.Should().Be("a");
    }

    [Test]
    public void Next_item_to_select_is_the_first_item_without_a_selection()
    {
        Node[] nodes = [new("group"), new("a", "a")];

        FileStatusSelectionPolicy.FindNextItemToSelect(
            nodes, hasSelection: false, _ => false, node => node.Item).Should().Be("a");
    }

    [Test]
    public void Navigation_finds_previous_and_next_searchable_items()
    {
        Node group = new("group", Searchable: false);
        Node a = new("a");
        Node b = new("b");
        Node c = new("c");
        Node[] nodes = [group, a, b, c];

        FileStatusSelectionPolicy.FindPreviousItem(nodes, b, node => node.Searchable).Should().Be(a);
        FileStatusSelectionPolicy.FindNextItem(nodes, b, node => node.Searchable).Should().Be(c);
        FileStatusSelectionPolicy.FindPreviousItem(nodes, a, node => node.Searchable).Should().BeNull();
        FileStatusSelectionPolicy.FindNextItem(nodes, c, node => node.Searchable).Should().BeNull();
    }

    [Test]
    public void Navigation_throws_for_a_node_not_in_the_sequence()
    {
        Node[] nodes = [new("a")];

        Action find = () => FileStatusSelectionPolicy.FindPreviousItem(nodes, new Node("ghost"), _ => true);

        find.Should().Throw<ArgumentException>();
    }

    [TestCase(null, FileFilterValidity.Empty)]
    [TestCase("", FileFilterValidity.Empty)]
    [TestCase("readme", FileFilterValidity.Valid)]
    [TestCase("[", FileFilterValidity.Invalid)]
    public void File_filter_parses_with_validity(string? value, FileFilterValidity expected)
    {
        FileFilterResult result = FileFilterParser.Parse(value);

        result.Validity.Should().Be(expected);
        (result.Filter is not null).Should().Be(expected is FileFilterValidity.Valid);
        (result.ErrorMessage is not null).Should().Be(expected is FileFilterValidity.Invalid);

        if (expected is FileFilterValidity.Valid)
        {
            result.Filter!.IsMatch("README.md").Should().BeTrue(because: "the filter is case-insensitive");
        }
    }

    [TestCase("needle", "-e \"needle\"")]
    [TestCase("a b", "-e \"a b\"")]
    [TestCase(@"path\\file", @"-e ""path\\\\file""")]
    [TestCase("-e 'expr'", "-e 'expr'")]
    [TestCase("--and -e a", "--and -e a")]
    [TestCase(" ", " ")]
    [TestCase("", "")]
    public void Git_grep_search_argument_wraps_plain_text(string search, string expected)
    {
        GitGrepQuery.BuildSearchArgument(search).Should().Be(expected);
    }

    [Test]
    public void Working_dir_prefix_is_stripped_case_insensitively()
    {
        // posix separators are used so the case runs identically on all OSes;
        // backslash conversion is ToPosixPath's platform-specific business
        FileStatusFilterText.StripWorkingDirPrefix("/home/User/Repo/src/a.cs", "/home/user/repo/").Should().Be("src/a.cs");
        FileStatusFilterText.StripWorkingDirPrefix("unrelated", "/home/user/repo/").Should().BeNull();
        FileStatusFilterText.StripWorkingDirPrefix("src", "/home/user/repo/").Should().BeNull(because: "shorter than the working dir");
    }
}
