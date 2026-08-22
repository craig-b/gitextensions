namespace GitCommands.Actions;

/// <summary>What the commit menu knows about the clicked row and repo.</summary>
public sealed record GridCommitMenuContext(
    bool IsArtificial = false,
    bool IsStash = false,
    bool InBisect = false,
    bool IsBareRepository = false,
    int SelectedCount = 1,
    bool HasCurrentBranch = true,
    bool HasBaseToCompare = false,
    bool HasBuildUrl = false,
    bool HasPullRequestUrl = false);

public enum RefMenuKind
{
    LocalBranch,
    RemoteBranch,
    Tag,
}

/// <summary>What the ref-chip menu knows about the clicked ref.</summary>
public sealed record RefMenuContext(RefMenuKind Kind, bool IsCurrent = false);

/// <summary>
///  The revision grid's action registry: the commit menu (row
///  right-click) and the ref menu (branch/tag chip right-click) as declared action lists.
///  Contextual rows (work tree/index, stashes, 2+ selected rows) REPLACE the commit menu
///  (keeping the copy group); bisect prepends its group. Views and the palette project
///  these via <see cref="MenuProjector"/>.
/// </summary>
public static class GridMenuRegistry
{
    /// <summary>The copy group, kept on every commit-menu variant - stash/artificial hashes stay copyable.</summary>
    public static IReadOnlyList<ActionDescriptor> CopyActions { get; } =
    [
        new("copy.hash", "Commit hash", "copy", ActionTier.Core),
        new("copy.message", "Message", "copy", ActionTier.Core),
        new("copy.author", "Author", "copy", ActionTier.Core),
        new("copy.date", "Date", "copy", ActionTier.Core),
        new("copy.refNames", "Branch and tag names", "copy", ActionTier.Common),
    ];

    public static IReadOnlyList<ActionDescriptor> ArtificialRowActions { get; } =
    [
        new("artificial.commit", "Commit...", "working", ActionTier.Core),
        new("artificial.resetChanges", "Reset changes...", "working", ActionTier.Core, Destructive: true),
    ];

    public static IReadOnlyList<ActionDescriptor> StashRowActions { get; } =
    [
        new("stash.apply", "Apply stash", "stash", ActionTier.Core),
        new("stash.pop", "Pop stash", "stash", ActionTier.Core),
        new("stash.drop", "Drop stash...", "stash", ActionTier.Core, Destructive: true),
    ];

    /// <summary>Replaces the commit menu when 2+ rows are selected (the range menu).</summary>
    public static IReadOnlyList<ActionDescriptor> RangeActions { get; } =
    [
        new("range.compareSelected", "Compare selected commits", "range", ActionTier.Core),
        new("range.cherryPick", "Cherry-pick selected commits...", "range", ActionTier.Core),
        new("range.squash", "Squash selected into one commit...", "range", ActionTier.Core),
        new("range.copyHashes", "Copy commit hashes", "range", ActionTier.Core),
    ];

    public static IReadOnlyList<ActionDescriptor> BisectActions { get; } =
    [
        new("bisect.good", "Mark revision as good", "bisect", ActionTier.Core),
        new("bisect.bad", "Mark revision as bad", "bisect", ActionTier.Core),
        new("bisect.skip", "Skip revision", "bisect", ActionTier.Core),
        new("bisect.stop", "Stop bisect", "bisect", ActionTier.Core),
    ];

    public static IReadOnlyList<ActionDescriptor> CommitActions { get; } =
    [
        new("commit.createBranch", "Create branch here...", "primary", ActionTier.Core, Hotkey: "Ctrl+B"),
        new("commit.cherryPick", "Cherry-pick this commit...", "primary", ActionTier.Core),
        new("commit.revert", "Revert this commit...", "primary", ActionTier.Core),
        new("commit.openDifftool", "Open in difftool", "primary", ActionTier.Core),

        new("commit.mergeIntoCurrent", "Merge into current branch...", "refs", ActionTier.Core),
        new("commit.checkoutBranch", "Checkout branch...", "refs", ActionTier.Core),
        new("commit.rebaseCurrentOnto", "Rebase current branch onto this...", "refs", ActionTier.Common),
        new("commit.createTag", "Create tag here...", "refs", ActionTier.Common, Hotkey: "Ctrl+T"),
        new("commit.resetCurrentToHere", "Reset current branch to here...", "refs", ActionTier.Common, Destructive: true),
        new("commit.resetAnotherToHere", "Reset another branch to here...", "refs", ActionTier.Advanced, Destructive: true),

        new("compare.toCurrentBranch", "Compare to current branch", "compare", ActionTier.Common),
        new("compare.toWorkingDir", "Compare to working directory", "compare", ActionTier.Common),
        new("compare.toBranch", "Compare to branch...", "compare", ActionTier.Common),
        new("compare.selectBase", "Select as BASE to compare", "compare", ActionTier.Advanced),
        new("compare.toBase", "Compare to BASE", "compare", ActionTier.Advanced),

        .. CopyActions,

        new("rewrite.edit", "Edit commit", "history-rewrite", ActionTier.Advanced),
        new("rewrite.reword", "Reword commit", "history-rewrite", ActionTier.Advanced),
        new("rewrite.fixup", "Create a fixup commit...", "history-rewrite", ActionTier.Advanced),
        new("rewrite.squash", "Create a squash commit...", "history-rewrite", ActionTier.Advanced),
        new("rewrite.amend", "Create an amend commit...", "history-rewrite", ActionTier.Advanced),
        new("commit.checkoutDetached", "Checkout this commit (detached)...", "history-rewrite", ActionTier.Advanced),
        new("commit.archive", "Archive this commit...", "history-rewrite", ActionTier.Advanced),

        new("open.buildReport", "View build report in a browser", "integrations", ActionTier.Common),
        new("open.pullRequest", "View pull request in a browser", "integrations", ActionTier.Common),
    ];

    public static IReadOnlyList<ActionDescriptor> RefActions { get; } =
    [
        new("ref.checkout", "Checkout", "primary", ActionTier.Core),
        new("ref.mergeIntoCurrent", "Merge into current branch...", "primary", ActionTier.Core),
        new("ref.rebaseCurrentOnto", "Rebase current branch onto this...", "primary", ActionTier.Common),
        new("ref.createBranchFrom", "Create branch from here...", "primary", ActionTier.Common),
        new("ref.push", "Push...", "sync", ActionTier.Core),
        new("ref.pushTag", "Push tag", "sync", ActionTier.Common),
        new("ref.pull", "Pull this branch...", "sync", ActionTier.Common),
        new("ref.compareToCurrent", "Compare to current branch", "compare", ActionTier.Common),
        new("ref.copyName", "Copy name", "copy", ActionTier.Core),
        new("ref.selectInLeftPanel", "Select in left panel", "copy", ActionTier.Common),
        new("ref.rename", "Rename...", "modify", ActionTier.Common, Hotkey: "F2"),
        new("ref.delete", "Delete...", "modify", ActionTier.Core, Destructive: true, Hotkey: "Del"),
    ];

    /// <summary>
    ///  Commit-menu groups views render as named submenus rather than separator-delimited
    ///  runs (the redesign's Copy / History rewrite / Run script submenus).
    /// </summary>
    public static IReadOnlyDictionary<string, string> CommitSubmenuGroups { get; } = new Dictionary<string, string>
    {
        ["copy"] = "Copy",
        ["history-rewrite"] = "History rewrite",
        ["scripts"] = "Run script",
    };

    /// <summary>
    ///  The commit-menu action list for a row: a multi-row selection or a contextual row
    ///  replaces it (contextual rows keep the copy group), bisect prepends.
    /// </summary>
    public static IReadOnlyList<ActionDescriptor> CommitMenuFor(GridCommitMenuContext context)
    {
        if (context.SelectedCount >= 2)
        {
            return RangeActions;
        }

        if (context.IsArtificial)
        {
            return [.. ArtificialRowActions, .. CopyActions];
        }

        if (context.IsStash)
        {
            return [.. StashRowActions, .. CopyActions];
        }

        return context.InBisect ? [.. BisectActions, .. CommitActions] : CommitActions;
    }

    public static bool IsApplicable(ActionDescriptor action, GridCommitMenuContext context)
        => action.Id switch
        {
            "commit.mergeIntoCurrent" or "commit.rebaseCurrentOnto" or "commit.resetCurrentToHere"
                or "compare.toCurrentBranch" => !context.IsBareRepository && context.HasCurrentBranch,
            "commit.createBranch" or "commit.checkoutBranch" or "commit.createTag"
                or "commit.resetAnotherToHere" or "commit.cherryPick" or "commit.revert"
                or "commit.checkoutDetached" or "commit.archive"
                or "rewrite.edit" or "rewrite.reword" or "rewrite.fixup" or "rewrite.squash" or "rewrite.amend"
                => !context.IsBareRepository,
            "range.cherryPick" or "range.squash" => !context.IsBareRepository && context.HasCurrentBranch,
            "compare.toBase" => context.HasBaseToCompare,
            "open.buildReport" => context.HasBuildUrl,
            "open.pullRequest" => context.HasPullRequestUrl,
            _ => true,
        };

    public static bool IsApplicable(ActionDescriptor action, RefMenuContext context)
        => action.Id switch
        {
            "ref.checkout" => context.Kind is not RefMenuKind.Tag && !context.IsCurrent,
            "ref.mergeIntoCurrent" or "ref.rebaseCurrentOnto" => !context.IsCurrent,
            "ref.push" or "ref.rename" => context.Kind is RefMenuKind.LocalBranch,
            "ref.pushTag" => context.Kind is RefMenuKind.Tag,
            "ref.pull" => context.Kind is RefMenuKind.RemoteBranch,
            "ref.delete" => !context.IsCurrent,
            "ref.compareToCurrent" => !context.IsCurrent,
            _ => true,
        };
}
