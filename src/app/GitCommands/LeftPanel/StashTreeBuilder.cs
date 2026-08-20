using GitCommands.Git;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;

namespace GitCommands.LeftPanel;

/// <summary>A stash row of the left panel, in git's stash-list order (newest first).</summary>
public sealed class StashTreeNode
{
    public required ObjectId ObjectId { get; init; }

    public required string ReflogSelector { get; init; }

    /// <summary>The panel path, e.g. "stash@{0}" (the "refs/" prefix stripped).</summary>
    public required string FullPath { get; init; }

    /// <summary>The label, e.g. "stash@{0}: WIP on main".</summary>
    public required string DisplayName { get; init; }
}

/// <summary>
///  Builds the stash list the left panel shows: the order is git's
///  own stash-list order, and the naming rules mirror the panel's historical ones.
/// </summary>
public static class StashTreeBuilder
{
    public static IReadOnlyList<StashTreeNode> Build(IEnumerable<GitRevision> stashes)
        => [.. stashes.Select(stash =>
        {
            string reflogSelector = stash.ReflogSelector!;
            return new StashTreeNode
            {
                ObjectId = stash.ObjectId,
                ReflogSelector = reflogSelector,
                FullPath = reflogSelector.RemovePrefix("refs/"),
                DisplayName = $"{reflogSelector.RemovePrefix(GitRefName.RefsStashPrefix)}: {stash.Subject}",
            };
        })];
}
