namespace GitCommands.Actions;

/// <summary>What the selected lines of a diff pane can be patched against.</summary>
public enum DiffLineTarget
{
    /// <summary>No line patching (blob view, image, word diff, bare repo...).</summary>
    None,

    /// <summary>An unstaged working-tree diff: lines can be staged or reset.</summary>
    WorkTree,

    /// <summary>A staged (index) diff: lines can be unstaged or reset.</summary>
    Index,

    /// <summary>A committed revision's diff: lines can be applied to or reverted from the working tree.</summary>
    Committed,
}

/// <summary>What the diff-pane menu knows about the pane and its selection.</summary>
public sealed record DiffMenuContext(
    bool HasSelection = false,
    bool IsPatchView = false,
    bool SupportsLinePatching = false,
    DiffLineTarget Target = DiffLineTarget.None,
    bool IsCommitWindow = false);

/// <summary>
///  The diff-pane action registry (context-menu redesign, surface 4). Per the pointer rule,
///  only selection/target-dependent verbs live here: the line-patch verbs (with honest
///  per-state captions - WinForms' "Stage selected lines" on a committed diff actually
///  cherry-picks the lines into the working tree), the copy transforms, and the commit
///  window's add-selection. Evicted: the 13 view toggles, Find, Go to line, next/previous
///  change - all pane-global, so they live on the view bar, the palette, and hotkeys.
/// </summary>
public static class DiffMenuRegistry
{
    public static IReadOnlyList<ActionDescriptor> DiffActions { get; } =
    [
        new("diff.stageLines", "Stage selected lines", "patch", ActionTier.Core, Hotkey: "S"),
        new("diff.unstageLines", "Unstage selected lines", "patch", ActionTier.Core, Hotkey: "U"),
        new("diff.resetLines", "Reset selected lines...", "patch", ActionTier.Core, Destructive: true, Hotkey: "R"),
        new("diff.applyLines", "Apply selected lines to working tree", "patch", ActionTier.Common),
        new("diff.revertLines", "Revert selected lines...", "patch", ActionTier.Common, Destructive: true),

        new("diff.copy", "Copy", "copy", ActionTier.Core, Hotkey: "Ctrl+C"),
        new("diff.copyPatch", "Copy patch", "copy", ActionTier.Common),
        new("diff.copyNewVersion", "Copy new version", "copy", ActionTier.Common),
        new("diff.copyOldVersion", "Copy old version", "copy", ActionTier.Common),

        new("diff.addToCommitMessage", "Add selection to commit message", "commit", ActionTier.Common),
    ];

    /// <summary>The blame author-gutter menu (a separate small strip in WinForms' BlameControl).</summary>
    public static IReadOnlyList<ActionDescriptor> BlameGutterActions { get; } =
    [
        new("blame.blameThis", "Blame this revision", "blame", ActionTier.Core),
        new("blame.blamePrevious", "Blame previous revision", "blame", ActionTier.Core),
        new("blame.showChanges", "Show changes", "blame", ActionTier.Core),
        new("blame.copyHash", "Copy commit hash", "copy", ActionTier.Common),
    ];

    /// <summary>
    ///  The diff menu for a pane: the patch group swaps structurally on the line target
    ///  (honest captions per state), copy transforms exist only in patch view, and the
    ///  commit row only in the commit window.
    /// </summary>
    public static IReadOnlyList<ActionDescriptor> DiffMenuFor(DiffMenuContext context)
        => [.. DiffActions.Where(action => action.Id switch
        {
            "diff.stageLines" => context.SupportsLinePatching && context.Target is DiffLineTarget.WorkTree,
            "diff.unstageLines" => context.SupportsLinePatching && context.Target is DiffLineTarget.Index,
            "diff.resetLines" => context.SupportsLinePatching && context.Target is DiffLineTarget.WorkTree or DiffLineTarget.Index,
            "diff.applyLines" or "diff.revertLines" => context.SupportsLinePatching && context.Target is DiffLineTarget.Committed,
            "diff.copyPatch" or "diff.copyNewVersion" or "diff.copyOldVersion" => context.IsPatchView,
            "diff.addToCommitMessage" => context.IsCommitWindow,
            _ => true,
        })];

    public static bool IsApplicable(ActionDescriptor action, DiffMenuContext context)
        => action.Id switch
        {
            // No whole-document fallback (reviewed): every copy transform needs a selection.
            // The patch verbs work off the caret's line when nothing is selected, like WinForms.
            "diff.copy" or "diff.copyPatch" or "diff.copyNewVersion" or "diff.copyOldVersion"
                or "diff.addToCommitMessage" => context.HasSelection,
            _ => true,
        };
}
