namespace GitCommands.Actions;

/// <summary>
///  What the file menu knows about the selection and its surface. One context serves every
///  host of the file-status surface (the grid's revision diff list, the commit window's
///  unstaged/staged lists) - WinForms already collapsed these onto one menu; the registry
///  models that single menu with applicability doing the per-surface work.
/// </summary>
public sealed record FileMenuContext(
    int SelectedCount = 1,
    bool AnyWorkTree = false,
    bool AnyIndex = false,
    bool AnyTracked = true,
    bool AnySubmodule = false,
    bool AnyConflicted = false,
    bool IsArtificialRevision = false,
    bool IsBareRepository = false,
    bool SelectionOnDisk = false,
    bool IsDiffGridSurface = false);

/// <summary>
///  The file-status surface's action registry (context-menu redesign draft 2): one declared
///  list, tiered for the first time; a conflicted selection PREPENDS the resolve group.
///  Evicted from the WinForms menu by review: sort-and-group-by (toolbar owns it), the
///  Show-'Find...' toggle, tree select/expand/collapse chrome. Windows-only items
///  (Visual Studio, WSL/Cygwin path variants) stay out.
/// </summary>
public static class FileMenuRegistry
{
    /// <summary>Prepended when the selection contains conflicted (unmerged) files.</summary>
    public static IReadOnlyList<ActionDescriptor> ConflictActions { get; } =
    [
        new("conflict.ours", "Resolve using ours", "conflict", ActionTier.Core),
        new("conflict.theirs", "Resolve using theirs", "conflict", ActionTier.Core),
        new("conflict.openWindow", "Open mergetool...", "conflict", ActionTier.Core),
        new("conflict.markResolved", "Mark resolved (stage as-is)", "conflict", ActionTier.Core),
        new("conflict.delete", "Delete file...", "conflict", ActionTier.Core, Destructive: true),
    ];

    public static IReadOnlyList<ActionDescriptor> FileActions { get; } =
    [
        new("file.stage", "Stage", "stage", ActionTier.Core, Hotkey: "S"),
        new("file.unstage", "Unstage", "stage", ActionTier.Core, Hotkey: "U"),
        new("file.resetChanges", "Reset file changes...", "stage", ActionTier.Core, Destructive: true, Hotkey: "R"),
        new("file.interactiveAdd", "Interactive add...", "stage", ActionTier.Advanced),
        new("file.resetChunk", "Reset chunk of file...", "stage", ActionTier.Advanced),
        new("file.cherryPickChanges", "Cherry-pick file changes", "stage", ActionTier.Advanced),

        new("file.openDifftool", "Open in difftool", "inspect", ActionTier.Core, Hotkey: "F3"),
        new("file.history", "File history", "inspect", ActionTier.Core, Hotkey: "H"),
        new("file.blame", "Blame", "inspect", ActionTier.Core, Hotkey: "B"),
        new("file.filterInGrid", "Filter file in grid", "inspect", ActionTier.Common, Hotkey: "F"),
        new("file.showInFileTree", "Show in file tree", "inspect", ActionTier.Common, Hotkey: "T"),
        new("file.gitGrep", "Find in commit files (git-grep)...", "inspect", ActionTier.Common),

        new("file.open", "Open file", "open", ActionTier.Common),
        new("file.openWith", "Open file with...", "open", ActionTier.Common),
        new("file.edit", "Edit file", "open", ActionTier.Common, Hotkey: "F4"),
        new("file.saveAs", "Save this revision as...", "open", ActionTier.Common),
        new("file.openTemp", "Open this revision (temp file)", "open", ActionTier.Advanced),
        new("file.showInFolder", "Show in folder", "open", ActionTier.Common),

        new("file.rename", "Rename / move...", "manage", ActionTier.Common, Hotkey: "F2"),
        new("file.delete", "Delete file...", "manage", ActionTier.Common, Destructive: true, Hotkey: "Del"),
        new("file.gitignore", "Add to .gitignore", "manage", ActionTier.Common),
        new("file.gitignoreLocal", "Add to .git/info/exclude", "manage", ActionTier.Advanced),
        new("file.skipWorktree", "Skip worktree", "manage", ActionTier.Advanced),
        new("file.assumeUnchanged", "Assume unchanged", "manage", ActionTier.Advanced),
        new("file.stopTracking", "Stop tracking this file", "manage", ActionTier.Advanced),

        new("file.copyPath", "Full path", "copy", ActionTier.Core),
        new("file.copyRelativePath", "Relative path", "copy", ActionTier.Core),

        new("submodule.open", "Open in new window", "submodule", ActionTier.Core),
        new("submodule.update", "Update", "submodule", ActionTier.Core),
        new("submodule.reset", "Reset changes...", "submodule", ActionTier.Core, Destructive: true),
        new("submodule.stash", "Stash changes", "submodule", ActionTier.Core),
        new("submodule.commit", "Commit changes...", "submodule", ActionTier.Core),
    ];

    /// <summary>File-menu groups views render as named submenus.</summary>
    public static IReadOnlyDictionary<string, string> FileSubmenuGroups { get; } = new Dictionary<string, string>
    {
        ["copy"] = "Copy path",
        ["submodule"] = "Submodule",
    };

    /// <summary>
    ///  The file-menu action list for a selection: conflicts prepend their group; the submodule
    ///  group exists only for submodule selections (structural, not gray - matches WinForms).
    /// </summary>
    public static IReadOnlyList<ActionDescriptor> FileMenuFor(FileMenuContext context)
    {
        IReadOnlyList<ActionDescriptor> actions = context.AnySubmodule
            ? FileActions
            : [.. FileActions.Where(action => action.Group != "submodule")];
        return context.AnyConflicted ? [.. ConflictActions, .. actions] : actions;
    }

    public static bool IsApplicable(ActionDescriptor action, FileMenuContext context)
        => action.Id switch
        {
            "file.stage" => context.AnyWorkTree,
            "file.unstage" => context.AnyIndex,
            "file.resetChanges" => !context.IsBareRepository && context.AnyTracked && context.IsArtificialRevision,
            "file.interactiveAdd" or "file.resetChunk"
                => context.SelectedCount == 1 && context.AnyWorkTree && !context.AnySubmodule,
            "file.cherryPickChanges" => context.SelectedCount == 1 && !context.AnyWorkTree && !context.AnySubmodule,
            "file.history" => context.AnyTracked,
            "file.blame" => context.SelectedCount == 1 && context.AnyTracked && !context.AnySubmodule,
            "file.filterInGrid" => context.IsDiffGridSurface && context.AnyTracked,
            "file.showInFileTree" or "file.gitGrep" => context.IsDiffGridSurface,
            "file.open" or "file.openWith" or "file.edit" or "file.showInFolder"
                => context.SelectedCount == 1 && context.SelectionOnDisk,
            "file.saveAs" or "file.openTemp"
                => !context.IsArtificialRevision && !context.AnySubmodule,
            "file.rename" => context.SelectedCount == 1 && context.AnyTracked && !context.AnySubmodule && !context.IsBareRepository,
            "file.delete" => context.IsArtificialRevision && context.SelectionOnDisk && !context.AnySubmodule,
            "file.gitignore" or "file.gitignoreLocal" => context.AnyWorkTree && !context.AnySubmodule,
            "file.skipWorktree" or "file.assumeUnchanged" => context.AnyWorkTree && context.AnyTracked,
            "file.stopTracking" => context.SelectedCount == 1 && context.AnyTracked && !context.AnySubmodule,
            "submodule.open" => context.AnySubmodule,
            "submodule.update" or "submodule.reset" or "submodule.stash" or "submodule.commit"
                => context.AnySubmodule && context.AnyWorkTree,
            _ => true,
        };
}
