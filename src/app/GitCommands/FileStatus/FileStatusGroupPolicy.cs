using System.Text.RegularExpressions;
using GitExtensions.Extensibility.Git;
using GitUI;

namespace GitCommands.FileStatus;

/// <summary>The git-grep contribution to the file list, moved from FileStatusList's private enum.</summary>
public enum GitGrepState
{
    Unknown,
    None,
    Preparing,
    Provided
}

/// <summary>The portable expansion decision; the view maps it to its tree's expand/collapse calls.</summary>
public enum GroupExpansion
{
    Expanded,
    Collapsed,

    /// <summary>Top level expanded, folder children replaced by placeholders until opened.</summary>
    PartiallyExpanded,
}

public sealed record FileStatusGroupFlags(bool ShowDiffGroups, bool FilesPresent, bool HasGrepGroup, bool ShowGroupLabel);

/// <summary>
///  The pure decisions of FileStatusList's tree construction (extracted from GetNodes): which flags govern the layout, how far each diff group expands, and the
///  group caption. The view keeps the TreeNode building, image lists, and async submodule
///  status updates.
/// </summary>
public static class FileStatusGroupPolicy
{
    public static FileStatusGroupFlags ComputeFlags(IReadOnlyList<FileStatusWithDescription> items, bool groupByRevision, GitGrepState gitGrepState)
    {
        bool showDiffGroups = items.Count > 1 || (groupByRevision && !(items.Count == 1 && items[0].Statuses.Count == 0));
        bool filesPresent = items.Any(x => x.Statuses.Count > 0);
        bool hasGrepGroup = gitGrepState != GitGrepState.None && (gitGrepState != GitGrepState.Unknown || items.Any(FileStatusDiffCalculator.IsGrepItemStatuses));
        bool showGroupLabel = (filesPresent && (items.Count > 1 || groupByRevision)) || hasGrepGroup;

        return new(showDiffGroups, filesPresent, hasGrepGroup, showGroupLabel);
    }

    /// <summary>
    ///  How far a diff group starts out expanded: grep results always show (partially when
    ///  large), small plain-diff groups and the first of few groups expand, the rest collapse.
    /// </summary>
    public static GroupExpansion GetGroupExpansion(
        FileStatusWithDescription group,
        IReadOnlyList<FileStatusWithDescription> items,
        bool emptyGroup,
        bool hasGrepGroup,
        bool expandIfFewFiles,
        int shownCount)
        => emptyGroup
            ? GroupExpansion.Collapsed
            : hasGrepGroup
                ? FileStatusDiffCalculator.IsGrepItemStatuses(group)
                    ? expandIfFewFiles && shownCount < 100
                        ? GroupExpansion.Expanded
                        : GroupExpansion.PartiallyExpanded
                    : GroupExpansion.Collapsed
                : ((group.Statuses.Count <= 7 && group.IconName == FileStatusIconNames.Diff) || items.Count < 3 || group == items[0]) && group.Statuses.Count > 0
                    ? GroupExpansion.Expanded
                    : GroupExpansion.Collapsed;

    public static string GetGroupName(FileStatusWithDescription group, int shownCount)
    {
        // Show shown and total number of files only if different; avoid showing "1/0" for "- No changes -"
        string shownDisplay = shownCount >= group.Statuses.Count ? "" : $"{shownCount}/";
        return $"({shownDisplay}{group.Statuses.Count}) {group.Summary}";
    }

    /// <summary>
    ///  The filter-match rule (extracted from FileStatusList.IsFilterMatch): range-diff rows
    ///  always show; otherwise the diff-status filter buttons and the name filter (against the
    ///  new and old name, optionally file-name-only) both apply.
    /// </summary>
    public static bool IsFilterMatch(GitItemStatus item, Regex? filter, Func<DiffBranchStatus, bool> isDiffStatusMatch, TruncatePathMethod truncatePathMethod)
    {
        if (item.IsRangeDiff)
        {
            return true;
        }

        if (!isDiffStatusMatch(item.DiffStatus))
        {
            return false;
        }

        if (filter is null)
        {
            return true;
        }

        string name = item.Name.TrimEnd(PathUtil.PosixDirectorySeparatorChar);
        string? oldName = item.OldName;

        if (truncatePathMethod == TruncatePathMethod.FileNameOnly)
        {
            name = Path.GetFileName(name);
            oldName = Path.GetFileName(oldName);
        }

        if (filter.IsMatch(name))
        {
            return true;
        }

        return oldName is not null && filter.IsMatch(oldName);
    }
}
