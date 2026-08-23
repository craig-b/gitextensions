using System.Text.RegularExpressions;
using GitCommands;
using GitCommands.FileStatus;
using GitExtensions.Extensibility.Git;
using GitUI;
using GitUIPluginInterfaces;

namespace GitCommandsTests.FileStatus;

public class FileStatusGroupPolicyTests
{
    private static FileStatusWithDescription Group(params GitItemStatus[] statuses)
        => new(firstRev: null, secondRev: new GitRevision(ObjectId.WorkTreeId), summary: "summary", statuses: statuses);

    [Test]
    public void Single_plain_group_shows_no_diff_groups()
    {
        FileStatusGroupFlags flags = FileStatusGroupPolicy.ComputeFlags([Group(new GitItemStatus("a"))], groupByRevision: false, GitGrepState.None);

        flags.Should().Be(new FileStatusGroupFlags(ShowDiffGroups: false, FilesPresent: true, HasGrepGroup: false, ShowGroupLabel: false));
    }

    [Test]
    public void Multiple_groups_show_diff_groups_and_labels()
    {
        FileStatusGroupFlags flags = FileStatusGroupPolicy.ComputeFlags([Group(new GitItemStatus("a")), Group()], groupByRevision: false, GitGrepState.None);

        flags.Should().Be(new FileStatusGroupFlags(ShowDiffGroups: true, FilesPresent: true, HasGrepGroup: false, ShowGroupLabel: true));
    }

    [Test]
    public void Group_by_revision_with_one_empty_group_shows_no_diff_groups()
    {
        FileStatusGroupFlags flags = FileStatusGroupPolicy.ComputeFlags([Group()], groupByRevision: true, GitGrepState.None);

        flags.ShowDiffGroups.Should().BeFalse();
        flags.FilesPresent.Should().BeFalse();
    }

    [Test]
    public void Small_diff_group_expands_and_large_late_group_collapses()
    {
        FileStatusWithDescription small = Group(new GitItemStatus("a"));
        FileStatusWithDescription[] many = [.. Enumerable.Range(0, 4).Select(_ => small)];

        FileStatusGroupPolicy.GetGroupExpansion(small, many, emptyGroup: false, hasGrepGroup: false, expandIfFewFiles: false, shownCount: 1)
            .Should().Be(GroupExpansion.Expanded);

        FileStatusWithDescription large = new(firstRev: null, secondRev: new GitRevision(ObjectId.WorkTreeId), summary: "s",
            statuses: [.. Enumerable.Range(0, 8).Select(i => new GitItemStatus($"f{i}"))], iconName: FileStatusIconNames.DiffB);

        FileStatusGroupPolicy.GetGroupExpansion(large, [small, small, large], emptyGroup: false, hasGrepGroup: false, expandIfFewFiles: false, shownCount: 8)
            .Should().Be(GroupExpansion.Collapsed);
    }

    [Test]
    public void Grep_groups_expand_fully_when_small_and_partially_when_large()
    {
        // Grep groups are recognized by their summary prefix.
        FileStatusWithDescription grep = new(firstRev: null, secondRev: new GitRevision(ObjectId.WorkTreeId), summary: "grep: pattern",
            statuses: [new GitItemStatus("a")], iconName: FileStatusDiffCalculator.GitGrepIconName);

        FileStatusGroupPolicy.GetGroupExpansion(grep, [grep], emptyGroup: false, hasGrepGroup: true, expandIfFewFiles: true, shownCount: 5)
            .Should().Be(GroupExpansion.Expanded);
        FileStatusGroupPolicy.GetGroupExpansion(grep, [grep], emptyGroup: false, hasGrepGroup: true, expandIfFewFiles: true, shownCount: 100)
            .Should().Be(GroupExpansion.PartiallyExpanded);
    }

    [Test]
    public void Group_name_shows_shown_count_only_when_filtered()
    {
        FileStatusWithDescription group = Group(new GitItemStatus("a"), new GitItemStatus("b"));

        FileStatusGroupPolicy.GetGroupName(group, shownCount: 2).Should().Be("(2) summary");
        FileStatusGroupPolicy.GetGroupName(group, shownCount: 1).Should().Be("(1/2) summary");
    }

    [Test]
    public void Filter_matches_name_and_old_name()
    {
        GitItemStatus renamed = new("src/new.txt") { OldName = "src/old.txt" };
        Func<DiffBranchStatus, bool> all = _ => true;

        FileStatusGroupPolicy.IsFilterMatch(renamed, new Regex("new"), all, TruncatePathMethod.None).Should().BeTrue();
        FileStatusGroupPolicy.IsFilterMatch(renamed, new Regex("old"), all, TruncatePathMethod.None).Should().BeTrue();
        FileStatusGroupPolicy.IsFilterMatch(renamed, new Regex("missing"), all, TruncatePathMethod.None).Should().BeFalse();
    }

    [Test]
    public void Filter_can_match_file_name_only()
    {
        GitItemStatus item = new("src/deep/name.txt");

        FileStatusGroupPolicy.IsFilterMatch(item, new Regex("^name"), _ => true, TruncatePathMethod.FileNameOnly).Should().BeTrue();
        FileStatusGroupPolicy.IsFilterMatch(item, new Regex("^name"), _ => true, TruncatePathMethod.None).Should().BeFalse();
    }

    [Test]
    public void Range_diff_rows_bypass_all_filters()
    {
        GitItemStatus rangeDiff = new("range") { IsRangeDiff = true };

        FileStatusGroupPolicy.IsFilterMatch(rangeDiff, new Regex("nomatch"), _ => false, TruncatePathMethod.None).Should().BeTrue();
    }

    [Test]
    public void Diff_status_filter_applies_before_the_name_filter()
    {
        GitItemStatus item = new("a") { DiffStatus = DiffBranchStatus.OnlyAChange };

        FileStatusGroupPolicy.IsFilterMatch(item, filter: null, status => status != DiffBranchStatus.OnlyAChange, TruncatePathMethod.None).Should().BeFalse();
    }

    [TestCase(true, false, false, false, "FileStatusRemoved")]
    [TestCase(false, true, false, false, "FileStatusAdded")]
    [TestCase(false, false, true, false, "FileStatusModified")]
    public void Image_keys_map_the_primary_states(bool deleted, bool isNew, bool changed, bool renamed, string expected)
    {
        GitItemStatus item = new("f") { IsDeleted = deleted, IsNew = isNew, IsChanged = changed, IsRenamed = renamed, IsTracked = !isNew };

        FileStatusItemImageKeys.Get(item).Should().Be(expected);
    }

    [Test]
    public void Image_keys_vary_by_diff_branch_status()
    {
        GitItemStatus item = new("f") { IsChanged = true, IsTracked = true, DiffStatus = DiffBranchStatus.OnlyAChange };

        FileStatusItemImageKeys.Get(item).Should().Be(FileStatusIconNames.FileStatusModifiedOnlyA);
    }

    [Test]
    public void Dirty_submodule_without_status_task_maps_to_dirty_icon()
    {
        GitItemStatus item = new("sub") { IsSubmodule = true, IsDirty = true, IsTracked = true, IsChanged = true };

        FileStatusItemImageKeys.Get(item).Should().Be(FileStatusIconNames.SubmoduleDirty);
    }
}
