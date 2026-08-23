namespace GitCommands.FileStatus;

/// <summary>
///  The icon-name keys the portable diff calculator attaches to its groups; the view maps each
///  key to an actual image (FileStatusList's state-image dictionary). The values are the
///  historical WinForms resource names - they are dictionary keys, not resource lookups.
/// </summary>
public static class FileStatusIconNames
{
    public const string Diff = "Diff";
    public const string DiffA = "DiffA";
    public const string DiffB = "DiffB";
    public const string DiffC = "DiffC";
    public const string DiffR = "DiffR";

    public const string DefaultFileImage = "DefaultFileImage";
    public const string Unmerged = "Unmerged";

    public const string FileStatusUnknown = "FileStatusUnknown";
    public const string FileStatusAdded = "FileStatusAdded";
    public const string FileStatusAddedOnlyA = "FileStatusAddedOnlyA";
    public const string FileStatusAddedOnlyB = "FileStatusAddedOnlyB";
    public const string FileStatusAddedSame = "FileStatusAddedSame";
    public const string FileStatusAddedUnequal = "FileStatusAddedUnequal";
    public const string FileStatusRemoved = "FileStatusRemoved";
    public const string FileStatusRemovedOnlyA = "FileStatusRemovedOnlyA";
    public const string FileStatusRemovedOnlyB = "FileStatusRemovedOnlyB";
    public const string FileStatusRemovedSame = "FileStatusRemovedSame";
    public const string FileStatusRemovedUnequal = "FileStatusRemovedUnequal";
    public const string FileStatusModified = "FileStatusModified";
    public const string FileStatusModifiedOnlyA = "FileStatusModifiedOnlyA";
    public const string FileStatusModifiedOnlyB = "FileStatusModifiedOnlyB";
    public const string FileStatusModifiedSame = "FileStatusModifiedSame";
    public const string FileStatusModifiedUnequal = "FileStatusModifiedUnequal";
    public const string FileStatusRenamed = "FileStatusRenamed";
    public const string FileStatusRenamedOnlyA = "FileStatusRenamedOnlyA";
    public const string FileStatusRenamedOnlyB = "FileStatusRenamedOnlyB";
    public const string FileStatusRenamedSame = "FileStatusRenamedSame";
    public const string FileStatusRenamedUnequal = "FileStatusRenamedUnequal";
    public const string FileStatusCopied = "FileStatusCopied";
    public const string FileStatusCopiedOnlyA = "FileStatusCopiedOnlyA";
    public const string FileStatusCopiedOnlyB = "FileStatusCopiedOnlyB";
    public const string FileStatusCopiedSame = "FileStatusCopiedSame";
    public const string FileStatusCopiedUnequal = "FileStatusCopiedUnequal";

    public const string SubmodulesManage = "SubmodulesManage";
    public const string FolderSubmodule = "FolderSubmodule";
    public const string SubmoduleDirty = "SubmoduleDirty";
    public const string SubmoduleRevisionUp = "SubmoduleRevisionUp";
    public const string SubmoduleRevisionUpDirty = "SubmoduleRevisionUpDirty";
    public const string SubmoduleRevisionDown = "SubmoduleRevisionDown";
    public const string SubmoduleRevisionDownDirty = "SubmoduleRevisionDownDirty";
    public const string SubmoduleRevisionSemiUp = "SubmoduleRevisionSemiUp";
    public const string SubmoduleRevisionSemiUpDirty = "SubmoduleRevisionSemiUpDirty";
    public const string SubmoduleRevisionSemiDown = "SubmoduleRevisionSemiDown";
    public const string SubmoduleRevisionSemiDownDirty = "SubmoduleRevisionSemiDownDirty";
}
