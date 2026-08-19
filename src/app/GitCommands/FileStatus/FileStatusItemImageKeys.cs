using GitExtensions.Extensibility.Git;
using GitUI;

namespace GitCommands.FileStatus;

/// <summary>
///  Maps a file status to its icon key (extracted verbatim from FileStatusList's
///  GetItemImageKey/GetSubmoduleItemImageKey). The keys index the view's
///  image dictionary - <see cref="FileStatusIconNames"/> pins their historical values on both
///  sides so they cannot drift.
/// </summary>
public static class FileStatusItemImageKeys
{
    public static string Get(GitItemStatus gitItemStatus)
    {
        if (gitItemStatus.IsDeleted)
        {
            return gitItemStatus.DiffStatus switch
            {
                DiffBranchStatus.OnlyAChange => FileStatusIconNames.FileStatusRemovedOnlyA,
                DiffBranchStatus.OnlyBChange => FileStatusIconNames.FileStatusRemovedOnlyB,
                DiffBranchStatus.SameChange => FileStatusIconNames.FileStatusRemovedSame,
                DiffBranchStatus.UnequalChange => FileStatusIconNames.FileStatusRemovedUnequal,
                _ => FileStatusIconNames.FileStatusRemoved
            };
        }

        if (gitItemStatus.IsRangeDiff)
        {
            return FileStatusIconNames.DiffR;
        }

        if (!string.IsNullOrWhiteSpace(gitItemStatus.GrepString))
        {
            return FileStatusIconNames.DefaultFileImage;
        }

        if (gitItemStatus.IsNew || !gitItemStatus.IsTracked)
        {
            return gitItemStatus.DiffStatus switch
            {
                DiffBranchStatus.OnlyAChange => FileStatusIconNames.FileStatusAddedOnlyA,
                DiffBranchStatus.OnlyBChange => FileStatusIconNames.FileStatusAddedOnlyB,
                DiffBranchStatus.SameChange => FileStatusIconNames.FileStatusAddedSame,
                DiffBranchStatus.UnequalChange => FileStatusIconNames.FileStatusAddedUnequal,
                _ => FileStatusIconNames.FileStatusAdded
            };
        }

        if (gitItemStatus.IsUnmerged)
        {
            return FileStatusIconNames.Unmerged;
        }

        if (gitItemStatus.IsSubmodule)
        {
            return GetForSubmodule(gitItemStatus);
        }

        if (gitItemStatus.IsChanged || (gitItemStatus.IsRenamed && gitItemStatus.RenameCopyPercentage != "100"))
        {
            return gitItemStatus.DiffStatus switch
            {
                DiffBranchStatus.OnlyAChange => FileStatusIconNames.FileStatusModifiedOnlyA,
                DiffBranchStatus.OnlyBChange => FileStatusIconNames.FileStatusModifiedOnlyB,
                DiffBranchStatus.SameChange => FileStatusIconNames.FileStatusModifiedSame,
                DiffBranchStatus.UnequalChange => FileStatusIconNames.FileStatusModifiedUnequal,
                _ => FileStatusIconNames.FileStatusModified
            };
        }

        if (gitItemStatus.IsRenamed)
        {
            return gitItemStatus.DiffStatus switch
            {
                DiffBranchStatus.OnlyAChange => FileStatusIconNames.FileStatusRenamedOnlyA,
                DiffBranchStatus.OnlyBChange => FileStatusIconNames.FileStatusRenamedOnlyB,
                DiffBranchStatus.SameChange => FileStatusIconNames.FileStatusRenamedSame,
                DiffBranchStatus.UnequalChange => FileStatusIconNames.FileStatusRenamedUnequal,
                _ => FileStatusIconNames.FileStatusRenamed
            };
        }

        if (gitItemStatus.IsCopied)
        {
            return gitItemStatus.DiffStatus switch
            {
                DiffBranchStatus.OnlyAChange => FileStatusIconNames.FileStatusCopiedOnlyA,
                DiffBranchStatus.OnlyBChange => FileStatusIconNames.FileStatusCopiedOnlyB,
                DiffBranchStatus.SameChange => FileStatusIconNames.FileStatusCopiedSame,
                DiffBranchStatus.UnequalChange => FileStatusIconNames.FileStatusCopiedUnequal,
                _ => FileStatusIconNames.FileStatusCopied
            };
        }

        // Illegal flag combinations or no flags set?
        return FileStatusIconNames.FileStatusUnknown;
    }

    public static string GetForSubmodule(GitItemStatus gitItemStatus)
    {
        if (gitItemStatus.GetSubmoduleStatusAsync() is not Task<GitSubmoduleStatus> task
            || task is null
            || !task.IsCompleted
            || task.CompletedResult() is not GitSubmoduleStatus status
            || status is null)
        {
            return gitItemStatus.IsDirty ? FileStatusIconNames.SubmoduleDirty : FileStatusIconNames.SubmodulesManage;
        }

        return (status.Status, status.IsDirty) switch
        {
            (SubmoduleStatus.FastForward, true) => FileStatusIconNames.SubmoduleRevisionUpDirty,
            (SubmoduleStatus.FastForward, false) => FileStatusIconNames.SubmoduleRevisionUp,
            (SubmoduleStatus.Rewind, true) => FileStatusIconNames.SubmoduleRevisionDownDirty,
            (SubmoduleStatus.Rewind, false) => FileStatusIconNames.SubmoduleRevisionDown,
            (SubmoduleStatus.NewerTime, true) => FileStatusIconNames.SubmoduleRevisionSemiUpDirty,
            (SubmoduleStatus.NewerTime, false) => FileStatusIconNames.SubmoduleRevisionSemiUp,
            (SubmoduleStatus.OlderTime, true) => FileStatusIconNames.SubmoduleRevisionSemiDownDirty,
            (SubmoduleStatus.OlderTime, false) => FileStatusIconNames.SubmoduleRevisionSemiDown,
            (SubmoduleStatus.SameCommit, false) => FileStatusIconNames.FolderSubmodule,
            _ => FileStatusIconNames.SubmoduleDirty,
        };
    }
}
