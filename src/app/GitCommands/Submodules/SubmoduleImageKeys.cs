using GitExtensions.Extensibility.Git;

namespace GitCommands.Submodules;

/// <summary>
///  The submodule (status, dirty) → icon-key decision tables, as string
///  keys the views resolve against their image collections. Two historical variants exist and
///  differ only for a dirty submodule with unknown status: menus show the plain folder, the
///  left-panel tree shows the dirty icon.
/// </summary>
public static class SubmoduleImageKeys
{
    public const string FolderSubmodule = nameof(FolderSubmodule);
    public const string SubmoduleDirty = nameof(SubmoduleDirty);
    public const string FileStatusModified = nameof(FileStatusModified);
    public const string SubmoduleRevisionUp = nameof(SubmoduleRevisionUp);
    public const string SubmoduleRevisionUpDirty = nameof(SubmoduleRevisionUpDirty);
    public const string SubmoduleRevisionDown = nameof(SubmoduleRevisionDown);
    public const string SubmoduleRevisionDownDirty = nameof(SubmoduleRevisionDownDirty);
    public const string SubmoduleRevisionSemiUp = nameof(SubmoduleRevisionSemiUp);
    public const string SubmoduleRevisionSemiUpDirty = nameof(SubmoduleRevisionSemiUpDirty);
    public const string SubmoduleRevisionSemiDown = nameof(SubmoduleRevisionSemiDown);
    public const string SubmoduleRevisionSemiDownDirty = nameof(SubmoduleRevisionSemiDownDirty);

    /// <summary>The variant the submodule menus use (a null status is always the plain folder).</summary>
    public static string GetMenuImageKey(SubmoduleStatus? status, bool isDirty)
        => (status, isDirty) switch
        {
            (null, _) => FolderSubmodule,
            (SubmoduleStatus.FastForward, true) => SubmoduleRevisionUpDirty,
            (SubmoduleStatus.FastForward, false) => SubmoduleRevisionUp,
            (SubmoduleStatus.Rewind, true) => SubmoduleRevisionDownDirty,
            (SubmoduleStatus.Rewind, false) => SubmoduleRevisionDown,
            (SubmoduleStatus.NewerTime, true) => SubmoduleRevisionSemiUpDirty,
            (SubmoduleStatus.NewerTime, false) => SubmoduleRevisionSemiUp,
            (SubmoduleStatus.OlderTime, true) => SubmoduleRevisionSemiDownDirty,
            (SubmoduleStatus.OlderTime, false) => SubmoduleRevisionSemiDown,
            (_, true) => SubmoduleDirty,
            (_, false) => FileStatusModified,
        };

    /// <summary>The variant the left-panel tree uses (a dirty null-status submodule shows the dirty icon).</summary>
    public static string GetNodeImageKey(SubmoduleStatus? status, bool? isDirty)
        => (status, isDirty) switch
        {
            (SubmoduleStatus.FastForward, true) => SubmoduleRevisionUpDirty,
            (SubmoduleStatus.FastForward, false) => SubmoduleRevisionUp,
            (SubmoduleStatus.Rewind, true) => SubmoduleRevisionDownDirty,
            (SubmoduleStatus.Rewind, false) => SubmoduleRevisionDown,
            (SubmoduleStatus.NewerTime, true) => SubmoduleRevisionSemiUpDirty,
            (SubmoduleStatus.NewerTime, false) => SubmoduleRevisionSemiUp,
            (SubmoduleStatus.OlderTime, true) => SubmoduleRevisionSemiDownDirty,
            (SubmoduleStatus.OlderTime, false) => SubmoduleRevisionSemiDown,
            (_, true) => SubmoduleDirty,
            (_, false) => FileStatusModified,
            _ => FolderSubmodule,
        };
}
