namespace GitCommands.Actions;

/// <summary>What the left panel knows about a remote-repo node.</summary>
public sealed record LeftPanelRemoteContext(bool RemoteEnabled = true, bool HasHttpUrl = false);

/// <summary>What the left panel knows about a submodule node.</summary>
public sealed record LeftPanelSubmoduleContext(bool IsCurrent = false, bool IsBareRepository = false);

/// <summary>What the left panel knows about a worktree node.</summary>
public sealed record LeftPanelWorktreeContext(bool IsCurrent = false, bool IsDeleted = false, bool DirectoryExists = true);

/// <summary>What the left panel knows about a stash node.</summary>
public sealed record LeftPanelStashContext(bool IsBareRepository = false);

/// <summary>What the left panel knows about a multi-selection of ref nodes.</summary>
public sealed record LeftPanelRangeContext(int SelectedRefCount);

/// <summary>
///  The left panel's action registry (context-menu redesign, surface 3). Branch, remote-branch,
///  and tag nodes reuse <see cref="GridMenuRegistry.RefActions"/> - chips and panel nodes are one
///  surface - so this registry carries only the panel-unique node kinds, with section headers as
///  real menu targets. Evicted by review: sort-by/sort-order (a global setting), expand/collapse
///  (tree affordance), move up/down (the Left panel settings page), and the commit-field copy
///  submenu (the grid's Copy owns commit fields; stashes keep a plain Copy hash).
/// </summary>
public static class LeftPanelMenuRegistry
{
    public static IReadOnlyList<ActionDescriptor> RemoteRepoActions { get; } =
    [
        new("remote.fetch", "Fetch", "sync", ActionTier.Core),
        new("remote.fetchPrune", "Fetch & prune...", "sync", ActionTier.Common),
        new("remote.activate", "Activate", "state", ActionTier.Core),
        new("remote.activateFetch", "Activate & fetch", "state", ActionTier.Common),
        new("remote.deactivate", "Deactivate", "state", ActionTier.Core),
        new("remote.openUrl", "Open remote URL", "open", ActionTier.Common),
        new("remote.manage", "Manage this remote...", "manage", ActionTier.Core),
    ];

    public static IReadOnlyList<ActionDescriptor> RemotesSectionActions { get; } =
    [
        new("remotes.fetchAll", "Fetch all remotes", "sync", ActionTier.Core),
        new("remotes.fetchPruneAll", "Fetch & prune all remotes", "sync", ActionTier.Common),
        new("remotes.manage", "Manage remotes...", "manage", ActionTier.Core),
    ];

    /// <summary>Stash apply/pop/drop share their ids with the grid's stash-row menu - one hotkey serves both.</summary>
    public static IReadOnlyList<ActionDescriptor> StashNodeActions { get; } =
    [
        new("stash.show", "Show stash", "primary", ActionTier.Core),
        new("stash.apply", "Apply stash", "stash", ActionTier.Core),
        new("stash.pop", "Pop stash", "stash", ActionTier.Core),
        new("stash.drop", "Drop stash...", "stash", ActionTier.Core, Destructive: true),
        new("stash.copyHash", "Copy hash", "copy", ActionTier.Common),
    ];

    public static IReadOnlyList<ActionDescriptor> StashesSectionActions { get; } =
    [
        new("stashes.save", "Stash changes...", "stash", ActionTier.Core),
        new("stashes.saveStaged", "Stash staged", "stash", ActionTier.Common),
        new("stashes.manage", "Open stash manager...", "stash", ActionTier.Core),
    ];

    /// <summary>Per-submodule verbs; the shared ids match the file-status surface's submodule submenu.</summary>
    public static IReadOnlyList<ActionDescriptor> SubmoduleNodeActions { get; } =
    [
        new("submodule.switchTo", "Open", "open", ActionTier.Core),
        new("submodule.open", "Open in new window", "open", ActionTier.Common),
        new("submodule.update", "Update", "manage", ActionTier.Core),
        new("submodule.reset", "Reset changes...", "manage", ActionTier.Common, Destructive: true),
        new("submodule.stash", "Stash changes", "manage", ActionTier.Common),
        new("submodule.commit", "Commit changes...", "manage", ActionTier.Common),
    ];

    /// <summary>Repo-level submodule verbs, moved from the WinForms node menu by review.</summary>
    public static IReadOnlyList<ActionDescriptor> SubmodulesSectionActions { get; } =
    [
        new("submodules.updateAll", "Update all submodules", "manage", ActionTier.Core),
        new("submodules.syncAll", "Synchronize all", "manage", ActionTier.Common),
        new("submodules.manage", "Manage submodules...", "manage", ActionTier.Core),
    ];

    public static IReadOnlyList<ActionDescriptor> WorktreeNodeActions { get; } =
    [
        new("worktree.open", "Open", "open", ActionTier.Core),
        new("worktree.copyPath", "Copy path", "copy", ActionTier.Core),
        new("worktree.showInFolder", "Show in folder", "copy", ActionTier.Common),
        new("worktree.delete", "Delete...", "manage", ActionTier.Core, Destructive: true),
    ];

    public static IReadOnlyList<ActionDescriptor> WorktreesSectionActions { get; } =
    [
        new("worktrees.create", "Create worktree...", "manage", ActionTier.Core),
        new("worktrees.prune", "Prune worktrees", "manage", ActionTier.Common),
        new("worktrees.manage", "Manage worktrees...", "manage", ActionTier.Common),
    ];

    /// <summary>Branch path folders (local branches only, like WinForms' BranchPathNode).</summary>
    public static IReadOnlyList<ActionDescriptor> BranchFolderActions { get; } =
    [
        new("folder.createBranch", "Create branch here...", "manage", ActionTier.Common),
        new("folder.operateOnBranches", "Operate on branches in folder...", "manage", ActionTier.Common),
    ];

    /// <summary>Replaces the node menu when 2+ ref nodes are selected (the panel's range menu).</summary>
    public static IReadOnlyList<ActionDescriptor> RefRangeActions { get; } =
    [
        new("ref.filterForSelected", "Filter for selected", "range", ActionTier.Core),
        new("refs.compareSelected", "Compare selected", "range", ActionTier.Core),
        new("refs.operateOn", "Operate on selected refs...", "range", ActionTier.Core),
    ];

    /// <summary>
    ///  The remote-repo menu for a node: activate/deactivate swap structurally on the remote's
    ///  state, and the URL row exists only for browsable (http) remotes - matching WinForms.
    /// </summary>
    public static IReadOnlyList<ActionDescriptor> RemoteRepoMenuFor(LeftPanelRemoteContext context)
        => [.. RemoteRepoActions.Where(action => action.Id switch
        {
            "remote.activate" or "remote.activateFetch" => !context.RemoteEnabled,
            "remote.deactivate" or "remote.fetch" or "remote.fetchPrune" => context.RemoteEnabled,
            "remote.openUrl" => context.HasHttpUrl,
            _ => true,
        })];

    /// <summary>
    ///  The submodule menu for a node: "Open" is structural (absent on the current submodule),
    ///  and working-tree verbs vanish in bare repositories - matching WinForms.
    /// </summary>
    public static IReadOnlyList<ActionDescriptor> SubmoduleMenuFor(LeftPanelSubmoduleContext context)
        => [.. SubmoduleNodeActions.Where(action => action.Id switch
        {
            "submodule.switchTo" => !context.IsCurrent,
            "submodule.reset" or "submodule.stash" or "submodule.commit" => !context.IsBareRepository,
            _ => true,
        })];

    public static bool IsApplicable(ActionDescriptor action, LeftPanelWorktreeContext context)
        => action.Id switch
        {
            "worktree.open" or "worktree.delete" => !context.IsCurrent && !context.IsDeleted,
            "worktree.showInFolder" => context.DirectoryExists,
            _ => true,
        };

    public static bool IsApplicable(ActionDescriptor action, LeftPanelStashContext context)
        => !context.IsBareRepository;

    public static bool IsApplicable(ActionDescriptor action, LeftPanelRangeContext context)
        => action.Id switch
        {
            "refs.compareSelected" => context.SelectedRefCount == 2,
            _ => true,
        };
}
