using GitCommands.Clone;

namespace GitCommandsTests.Clone;

public sealed class CloneBranchSelectionTests
{
    private const string DefaultCaption = "(default: remote HEAD)";
    private const string NoneCaption = "(none)";

    [Test]
    public void ToBranchArgument_maps_the_default_head_caption_to_an_empty_branch()
    {
        CloneBranchSelection.ToBranchArgument(DefaultCaption, DefaultCaption, NoneCaption).Should().BeEmpty();
    }

    [Test]
    public void ToBranchArgument_maps_the_none_caption_to_null()
    {
        CloneBranchSelection.ToBranchArgument(NoneCaption, DefaultCaption, NoneCaption).Should().BeNull();
    }

    [Test]
    public void ToBranchArgument_passes_any_other_text_through_as_a_literal_branch()
    {
        CloneBranchSelection.ToBranchArgument("feature/x", DefaultCaption, NoneCaption).Should().Be("feature/x");
    }

    [Test]
    public void ToBranchArgument_passes_null_text_through_as_null()
    {
        // null equals neither caption, so it falls through the literal-branch branch unchanged.
        CloneBranchSelection.ToBranchArgument(null, DefaultCaption, NoneCaption).Should().BeNull();
    }

    [Test]
    public void MergeBranchList_puts_the_sentinels_ahead_of_the_branch_names()
    {
        (IReadOnlyList<string> items, string? reselect) = CloneBranchSelection.MergeBranchList(
            [DefaultCaption, NoneCaption],
            ["main", "dev"],
            currentText: null);

        items.Should().Equal(DefaultCaption, NoneCaption, "main", "dev");
        reselect.Should().BeNull();
    }

    [Test]
    public void MergeBranchList_reselects_text_still_present_in_the_merged_list()
    {
        (_, string? reselect) = CloneBranchSelection.MergeBranchList(
            [DefaultCaption, NoneCaption],
            ["main", "dev"],
            currentText: "main");

        reselect.Should().Be("main");
    }

    [Test]
    public void MergeBranchList_drops_text_absent_from_the_merged_list()
    {
        (_, string? reselect) = CloneBranchSelection.MergeBranchList(
            [DefaultCaption, NoneCaption],
            ["main", "dev"],
            currentText: "gone");

        reselect.Should().BeNull();
    }
}
