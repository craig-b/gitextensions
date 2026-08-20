using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;

namespace GitCommands.Stash;

/// <summary>The stash dialog's list-selection decisions.</summary>
public static class StashSelectionPolicy
{
    /// <summary>
    ///  The list index to preselect for an initial "stash@{n}" argument. Index 0 is the
    ///  working-directory pseudo-item, so stash n sits at list index n + 1.
    /// </summary>
    public static int? ParseInitialIndex(string? initialStash)
    {
        if (initialStash is null)
        {
            return null;
        }

        string index = initialStash.SubstringAfter('{').SubstringUntil('}');
        return int.TryParse(index, out int result) ? result + 1 : null;
    }

    /// <summary>
    ///  The index to select when the list (re)loads: a remembered index survives a drop by
    ///  clamping to the shrunken list, manage mode jumps to the first real stash once, and
    ///  the working-directory item is the fallback.
    /// </summary>
    public static int ResolveStartupSelection(int lastSelectedIndex, bool manageStashes, int itemCount)
    {
        if (lastSelectedIndex > 0)
        {
            return Math.Min(lastSelectedIndex, itemCount - 1);
        }

        if (manageStashes && itemCount > 1)
        {
            return 1;
        }

        return itemCount > 0 ? 0 : -1;
    }

    /// <summary>Hotkey navigation mirrors the revision grid: "next" moves toward the newest (lower index).</summary>
    public static int? Navigate(int selectedIndex, int itemCount, bool next)
    {
        int index = selectedIndex + (next ? -1 : 1);
        return index >= 0 && index < itemCount ? index : null;
    }
}

/// <summary>What the selected stash-list item allows.</summary>
public readonly record struct StashSelectionCapabilities(bool MessageEditable, bool DropAllowed, bool ApplyAllowed)
{
    /// <summary>The working-directory pseudo-item takes a new message but cannot be dropped or applied.</summary>
    public static StashSelectionCapabilities Evaluate(bool isWorkingDirItem)
        => new(MessageEditable: isWorkingDirItem, DropAllowed: !isWorkingDirItem, ApplyAllowed: !isWorkingDirItem);

    /// <summary>A partial stash needs the working-directory item selected and files chosen.</summary>
    public static bool PartialStashAllowed(bool isWorkingDirItem, bool hasSelectedFiles)
        => isWorkingDirItem && hasSelectedFiles;
}

/// <summary>
///  The revision pair(s) a stash-list selection diffs. The working-directory item splits
///  into index←HEAD and worktree←index halves - unless HEAD is unborn or detached to
///  nothing, which degrades to a single worktree diff.
/// </summary>
public sealed record StashDiffRevisions(GitRevision? First, GitRevision Second, GitRevision? SplitIndexRevision)
{
    public bool ShowSplit => SplitIndexRevision is not null;

    public static StashDiffRevisions ForWorkingDir(ObjectId headId)
    {
        GitRevision workTreeRev = new(ObjectId.WorkTreeId) { ParentIds = [ObjectId.IndexId] };
        if (headId.IsZero)
        {
            return new StashDiffRevisions(First: null, Second: workTreeRev, SplitIndexRevision: null);
        }

        GitRevision headRev = new(headId);
        GitRevision indexRev = new(ObjectId.IndexId) { ParentIds = [headId] };
        return new StashDiffRevisions(First: headRev, Second: workTreeRev, SplitIndexRevision: indexRev);
    }

    public static StashDiffRevisions ForStash(ObjectId parentId, ObjectId stashId)
    {
        GitRevision? firstRev = parentId.IsZero ? null : new(parentId);
        GitRevision secondRev = new(stashId);
        if (!parentId.IsZero)
        {
            secondRev.ParentIds = [parentId];
        }

        return new StashDiffRevisions(firstRev, secondRev, SplitIndexRevision: null);
    }
}

public static class StashSaveMessage
{
    /// <summary>The dialog's historical rule: a non-blank message is passed with a leading space.</summary>
    public static string Normalize(string? messageText)
        => !string.IsNullOrWhiteSpace(messageText) ? " " + messageText.Trim() : string.Empty;
}
