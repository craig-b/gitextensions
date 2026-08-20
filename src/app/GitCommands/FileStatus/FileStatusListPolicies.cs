using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;

namespace GitCommands.FileStatus;

/// <summary>Whether a root node is skipped when picking the first visible item.</summary>
public static class FileStatusRootSkip
{
    public static bool ShouldSkip(bool showDiffGroups, bool rootExpanded, bool rootIsPlaceholderOnly, bool isFileTreeMode, bool hasFilter, bool grepActive)
        => (showDiffGroups && !rootExpanded)
            || rootIsPlaceholderOnly
            || (isFileTreeMode && !hasFilter && !grepActive);
}

/// <summary>What selection to apply after the list view rebuilds.</summary>
public enum PostUpdateSelection
{
    FireEmpty,
    SelectOnlyRoot,
    SelectFirstVisible,
    RestorePrevious,
}

public static class FileStatusPostUpdate
{
    public static PostUpdateSelection AfterUpdate(int rootCount, bool onlyRootIsLeaf, bool updateCausedByFilter, bool selectFirstOnSetItems, int previouslySelectedCount)
    {
        if (rootCount == 0)
        {
            return PostUpdateSelection.FireEmpty;
        }

        if (rootCount == 1 && onlyRootIsLeaf)
        {
            return PostUpdateSelection.SelectOnlyRoot;
        }

        return !updateCausedByFilter && selectFirstOnSetItems
            ? PostUpdateSelection.SelectFirstVisible
            : previouslySelectedCount > 0
                ? PostUpdateSelection.RestorePrevious
                : PostUpdateSelection.SelectFirstVisible;
    }
}

/// <summary>The diff A/B status filter, as flags instead of four checkbox reads.</summary>
[Flags]
public enum DiffBranchStatusFilter
{
    None = 0,
    UnequalChange = 1,
    OnlyBChange = 2,
    OnlyAChange = 4,
    SameChange = 8,
    All = UnequalChange | OnlyBChange | OnlyAChange | SameChange,
}

public static class DiffAbFilter
{
    public static bool Matches(DiffBranchStatus status, DiffBranchStatusFilter filter)
        => status switch
        {
            DiffBranchStatus.UnequalChange => filter.HasFlag(DiffBranchStatusFilter.UnequalChange),
            DiffBranchStatus.OnlyBChange => filter.HasFlag(DiffBranchStatusFilter.OnlyBChange),
            DiffBranchStatus.OnlyAChange => filter.HasFlag(DiffBranchStatusFilter.OnlyAChange),
            DiffBranchStatus.SameChange => filter.HasFlag(DiffBranchStatusFilter.SameChange),
            _ => true,
        };

    /// <summary>The A/B filter buttons appear only when some group is an A/B diff group.</summary>
    public static bool IsApplicable(IEnumerable<string> groupIconNames)
        => groupIconNames.Any(iconName => iconName is FileStatusIconNames.DiffB or FileStatusIconNames.DiffA);
}

/// <summary>The file-status toolbar's separator/visibility/enablement rules.</summary>
public readonly record struct FileStatusToolbarState(
    bool ShowCollapseGroups,
    bool ShowRefreshSeparator,
    bool ShowAsTreeSeparator,
    bool DenseTreeEnabled,
    bool ShowGroupNodesEnabled,
    bool ShowDiffAbFilters,
    bool ShowGrepButton)
{
    public static FileStatusToolbarState Compute(
        bool canUseGrep,
        bool hasRevisionRootNode,
        bool refreshButtonVisible,
        bool flatList,
        bool hasGrouping,
        bool hasDiffAbGroups)
    {
        bool hasGroups = canUseGrep || hasRevisionRootNode;
        return new FileStatusToolbarState(
            ShowCollapseGroups: hasGroups,
            ShowRefreshSeparator: hasGroups && refreshButtonVisible,
            ShowAsTreeSeparator: hasGroups || refreshButtonVisible,
            DenseTreeEnabled: !flatList,
            ShowGroupNodesEnabled: hasGrouping && flatList,
            ShowDiffAbFilters: hasDiffAbGroups,
            ShowGrepButton: canUseGrep);
    }
}

/// <summary>The revision-dependent toolbar enablement (the second UpdateToolbar overload).</summary>
public readonly record struct FileStatusRevisionToolbarState(bool RefreshEnabled, bool WorktreeOptionsEnabled)
{
    public static FileStatusRevisionToolbarState Compute(IReadOnlyList<GitRevision> revisions)
    {
        bool withArtificial = revisions.Any(revision => revision.IsArtificial);
        return new FileStatusRevisionToolbarState(
            RefreshEnabled: withArtificial,
            WorktreeOptionsEnabled: withArtificial && revisions.Any(revision => revision.ObjectId == ObjectId.WorkTreeId));
    }
}
